"""Check only this task's SQL snapshots, real HTTP captures and Mongo projections."""
import json
import os
import re
import hashlib
from decimal import Decimal
from pathlib import Path
from datetime import datetime, timezone
from pymongo import MongoClient

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/shared-checkout-20261019'
state = json.loads((OUT / 'sql-closeout.json').read_text(encoding='utf-8'))
before = json.loads((OUT / 'sql-underpay-noeffects.json').read_text(encoding='utf-8'))
prior = {o['identity']['Id']: o for o in before['orders']}
checks = []
def check(name, condition):
    checks.append({'name': name, 'passed': bool(condition)})
    if not condition:
        raise AssertionError(name)
def money(value):
    return Decimal(str(value))
def cli_result(name):
    raw = (OUT / name).read_bytes()
    text = raw.decode('utf-16') if raw.startswith(b'\xff\xfe') else raw.decode('utf-8-sig')
    text = text.replace('\r\n', '\n')
    return json.loads(text.split('### Result\n')[1].split('\n###')[0])

accepted = {}
for name in ['baseline-cod-submit.txt', 'baseline-wallet-submit.txt', 'verified-cod-submit.txt',
             'verified-wallet-submit.txt', 'verified-coupon-cod-submit.txt', 'ordinary-coupon-wallet-submit.txt', 'restored-cod-submit.txt']:
    captured = cli_result(name)
    check(name + ' actual HTTP200', captured['status'] == 200)
    accepted[captured['body']['orderId']] = captured

summary = []
for entry in state['orders']:
    order = entry['order'][0]['record']
    oid = order['Id']
    lines = [i['record'] for i in entry['items']]
    gross = sum((money(i['UnitPrice']) * i['Quantity'] for i in lines), Decimal(0))
    discount = sum((money(i['DiscountAmount']) for i in lines), Decimal(0))
    net = sum((money(i['Total']) for i in lines), Decimal(0))
    paid = sum((money(i['PaidAmount']) for i in lines), Decimal(0))
    accepted_payments = sum((money(p['record']['Amount']) for p in entry['intents'] if p['record']['Status'] == 2), Decimal(0))
    remaining = max(max(Decimal(0), net-paid), max(Decimal(0), money(order['Total'])-max(paid, accepted_payments)))
    check(order['OrderNumber']+' persisted pricing', gross == money(order['Subtotal']) and discount == money(order['TotalDiscountAmount']) and net+money(order['ShippingCost'])+money(order['PaymentFee']) == money(order['Total']))
    check(order['OrderNumber']+' tenant/currency', order['TenantId']=='c9c5c1ae-949e-498e-bc63-07242daeb49d' and order['Currency']=='EGP')
    if oid in accepted:
        capture = accepted[oid]
        check(order['OrderNumber']+' accepted quote/order agreement', money(capture['body']['totalAmount'])==money(order['Total'])==money(capture['request']['expectedPricing']['total']))
    check(order['OrderNumber']+' unique business postings', len({j['IdempotencyKey'] for j in entry['journals']})==len(entry['journals']))
    for j in entry['journals']:
        check(str(j['Id'])+' posted balanced', j['Status']==1 and money(j['debit'])==money(j['credit']))
    wallet_journals = [j for j in entry['journals'] if j['SourceType']=='Order_Wallet']
    if order['PaymentStatus']==2:
        check(order['OrderNumber']+' fully settled net line and order', paid==net and accepted_payments==money(order['Total']) and remaining==0)
        check(order['OrderNumber']+' single wallet journal', len(wallet_journals)==1 and money(wallet_journals[0]['debit'])==money(order['Total']))
        check(order['OrderNumber']+' accepted intent amount', len(entry['intents'])==1 and money(entry['intents'][0]['record']['Amount'])==money(order['Total']))
        check(order['OrderNumber']+' accounting receipts processed', all(m['JournalId'] and m['ProcessedAtUtc'] for m in entry['accountingOutbox']))
        check(order['OrderNumber']+' posted history preserved through replays/restoration', prior[oid]['journals']==entry['journals'])
        check(order['OrderNumber']+' coupon exactly once', len(entry['usages'])==(1 if order['CouponId'] else 0))
    else:
        check(order['OrderNumber']+' no successful allocation/journal', paid==0 and not entry['journals'] and not entry['accountingOutbox'])
    if order['PaymentMethodCode']=='COD':
        check(order['OrderNumber']+' COD independent of wallet intents', not entry['intents'] and bool(order['PaymentReference']))
    if entry['idempotency']:
        check(order['OrderNumber']+' one durable binding', len(entry['idempotency'])==1 and entry['idempotency'][0]['record']['OrderId']==oid)
    summary.append({'orderId':oid,'orderNumber':order['OrderNumber'],'coupon':order['CouponCode'],'gross':gross,'discount':discount,
        'netLine':net,'shipping':order['ShippingCost'],'tax':order['TaxAmount'],'fee':order['PaymentFee'],'total':order['Total'],
        'paidLine':paid,'netLineRemaining':max(Decimal(0),net-paid),'acceptedPaymentTotal':accepted_payments,'remainingPayable':remaining,
        'status':order['Status'],'paymentStatus':order['PaymentStatus'],'checkoutState':order['CheckoutState'],'reference':order['PaymentReference'],
        'intents':[p['record'] for p in entry['intents']],'journals':entry['journals'],'ledgerLines':entry['ledgerLines']})

under = prior['2d59dc7c-4b3c-4e0b-81eb-50ec3e6e88b0']
check('Genuine underpayment remained550 against560 without side effects before explicit rejection',
      under['intents'][0]['record']['Status']==7 and money(under['intents'][0]['record']['Amount'])==550 and
      not under['journals'] and not under['accountingOutbox'] and all(money(i['record']['PaidAmount'])==0 for i in under['items']))
decision = cli_result('underpayment-ui-decision.txt')['decision']
check('Real Admin underpayment409 with correct remaining560 and shortfall10', decision['status']==409 and '560.00' in decision['body']['message'] and 'shortfall 10.00' in decision['body']['message'])
concurrent = cli_result('coupon-concurrent-ui-approval.txt')
check('Two real Admin tabs exactly one200 and one409', sorted([concurrent['decision']['status'],concurrent['concurrentDecision']['status']])==[200,409])
retries = cli_result('replays-stale-tenant.txt')
check('Concurrent exact checkout replay same order', all(r['status']==200 and r['orderId']==retries['replay']['originalOrderId'] for r in retries['replay']['responses']))
check('Stale decisions409', retries['decisions']['staleApprove']['status']==409 and retries['decisions']['staleReject']['status']==409)
check('Foreign tenant read/write/proof403', all(retries['decisions'][key]['status']==403 for key in ['foreignRead','foreignDecision','foreignProof']))
wallet = next(m for m in state['methods'] if m['Code']=='MANUALWALLET')
cod = next(m for m in state['methods'] if m['Code']=='COD')
check('Final wallet disabled with no synthetic encrypted recipient', not wallet['IsEnabled'] and not wallet['has_encrypted_config'])
check('COD configuration unchanged', cod['IsEnabled'] and cod['Version']==13)
task_coupons = [c['record'] for c in state['coupons'] if c['record']['Code'].startswith('SHQA20261019')]
check('Both task coupons inactive; usage preserved', len(task_coupons)==2 and all(not c['IsActive'] for c in task_coupons) and sum(c['CurrentUsage'] for c in task_coupons)==1)
check('Original order and coupon1441 not fabricated', not state['diagnostic'] and all(c['record']['Code']!='1441' for c in state['coupons']))
candidate = next(o for o in summary if o['orderNumber']=='ORD-C9C5-2026-01078')
check('Stale candidate did not override owned cart coupon', candidate['coupon']=='SHQA20261019WALLET' and candidate['discount']==Decimal('27.5'))

settings = {}
for name in ['appsettings.json','appsettings.Development.json','appsettings.Development.local.json']:
    path = ROOT / 'PointOfSale.API' / name
    if path.exists():
        section = re.search(r'"MongoDB"\s*:\s*\{([^}]+)\}', path.read_text(encoding='utf-8-sig'))
        if section:
            settings.update(dict(re.findall(r'"(ConnectionString|DatabaseName)"\s*:\s*"([^"\r\n]+)"', section[1])))
with MongoClient(os.environ.get('MongoDB__ConnectionString',settings['ConnectionString']),serverSelectionTimeoutMS=10000) as client:
    db = client[os.environ.get('MongoDB__DatabaseName',settings['DatabaseName'])]
    docs = list(db.orders.find({'TenantId':'c9c5c1ae-949e-498e-bc63-07242daeb49d','OrderId':{'$in':[o['orderId'] for o in summary]}},
        {'_id':0,'TenantId':1,'OrderId':1,'OrderNumber':1,'Status':1,'PaymentStatus':1,'Total':1,'Currency':1,'LastMutationVersion':1}))
    original = list(db.orders.find({'OrderNumber':'ORD-A758-2026-01040'},{'_id':0,'OrderId':1,'TenantId':1,'OrderNumber':1}))
    check('Original Mongo order absent', not original)
    check('Each QA order has exactly one projection', len(docs)==len(summary) and len({d['OrderId'] for d in docs})==len(summary))
    for doc in docs:
        order = next(o for o in summary if o['orderId']==doc['OrderId'])
        check(order['orderNumber']+' projection amount/currency', money(str(doc['Total']))==money(order['total']) and doc['Currency']=='EGP')
        check(order['orderNumber']+' projection states', doc['Status']=={0:'Pending',2:'Processing',6:'Cancelled'}[order['status']] and doc['PaymentStatus']=={0:'Pending',2:'Collected',8:'Rejected',11:'Expired'}[order['paymentStatus']])
    (OUT/'mongo-closeout.json').write_text(json.dumps({'observedUtc':datetime.now(timezone.utc).isoformat(),'readOnly':True,'database':db.name,'qaProjections':docs,'original':original},default=str,indent=2),encoding='utf-8')

preservation = []
for item in json.loads((OUT/'existing-work-hashes.json').read_text()):
    path = ROOT / item['path']
    if path.is_file():
        changed = hashlib.sha256(path.read_bytes()).hexdigest()!=item['sha256']
        if changed:
            preservation.append(item['path'])
check('Previously modified work preserved except intentional checkout/task files', all(p.replace('\\','/')=='PointOfSale.API/Services/Checkout/CheckoutService.cs' or 'shared-checkout-20261019' in p for p in preservation))
(OUT/'preservation-check.json').write_text(json.dumps({'changedPreviouslyCapturedFiles':preservation,'scope':'Hash comparison of existing modified/untracked source and report files; not whole-database immutability'},indent=2),encoding='utf-8')
(OUT/'financial-consistency-summary.json').write_text(json.dumps({'observedUtc':datetime.now(timezone.utc).isoformat(),'checks':checks,'orders':summary},default=str,indent=2),encoding='utf-8')
print(json.dumps({'checks':len(checks),'passed':sum(c['passed'] for c in checks),'orders':len(summary),'journals':sum(len(o['journals']) for o in summary),'projections':len(docs)}))
