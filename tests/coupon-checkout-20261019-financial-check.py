"""Verify only task-manifested SQL snapshots and configured Mongo projections."""
import os
import re
import json
from decimal import Decimal
from pathlib import Path
from datetime import datetime, timezone
from pymongo import MongoClient

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/coupon-checkout-20261019'
state = json.loads((OUT / 'sql-closeout.json').read_text(encoding='utf-8'))
before = json.loads((OUT / 'sql-financial-final.json').read_text(encoding='utf-8'))
pre_cleanup = {o['identity']['Id']: o for o in before['orders']}
checks = []
def check(name, condition):
    checks.append({'name': name, 'passed': bool(condition)})
    if not condition:
        raise AssertionError(name)
def money(value):
    return Decimal(str(value))

summary = []
for entry in state['orders']:
    order = entry['order'][0]['record']
    oid = order['Id']
    lines = [item['record'] for item in entry['items']]
    gross = sum((money(i['UnitPrice']) * i['Quantity'] for i in lines), Decimal(0))
    discount = sum((money(i['DiscountAmount']) for i in lines), Decimal(0))
    net = sum((money(i['Total']) for i in lines), Decimal(0))
    paid = sum((money(i['PaidAmount']) for i in lines), Decimal(0))
    accepted_payments = sum((money(p['record']['Amount']) for p in entry['intents']
                             if p['record']['Status'] == 2 and p['record']['Currency'] == order['Currency']), Decimal(0))
    remaining_payable = max(max(Decimal(0), net - paid), max(Decimal(0), money(order['Total']) - max(paid, accepted_payments)))
    check(order['OrderNumber'] + ' accepted pricing', gross == money(order['Subtotal']) and
          discount == money(order['TotalDiscountAmount']) and
          net + money(order['ShippingCost']) + money(order['PaymentFee']) == money(order['Total']))
    check(order['OrderNumber'] + ' tenant', order['TenantId'] == state['tenants'][0]['Id'])
    for journal in entry['journals']:
        check(str(journal['Id']) + ' balanced/posted', money(journal['debit']) == money(journal['credit']) and journal['Status'] == 1)
    check(order['OrderNumber'] + ' unique journals', len({j['IdempotencyKey'] for j in entry['journals']}) == len(entry['journals']))
    wallet = [j for j in entry['journals'] if j['SourceType'] == 'Order_Wallet']
    if order['PaymentStatus'] == 2:
        check(order['OrderNumber'] + ' accepted payment covers entire order including charges', accepted_payments == money(order['Total']) and remaining_payable == 0)
        check(order['OrderNumber'] + ' net lines fully allocated', paid == net and all(money(i['PaidAmount']) == money(i['Total']) for i in lines))
        check(order['OrderNumber'] + ' exactly one full wallet settlement', len(wallet) == 1 and money(wallet[0]['debit']) == money(order['Total']))
        check(order['OrderNumber'] + ' accounting receipts processed', all(m['JournalId'] and m['ProcessedAtUtc'] for m in entry['accountingOutbox']))
        check(order['OrderNumber'] + ' cleanup preserved posted history', pre_cleanup[oid]['journals'] == entry['journals'])
        check(order['OrderNumber'] + ' one confirmed coupon usage' if order['CouponId'] else order['OrderNumber'] + ' no coupon usage',
              len([u for u in state['usages'] if u['record']['OrderId'] == oid]) == (1 if order['CouponId'] else 0))
    else:
        check(order['OrderNumber'] + ' no successful financial effects', paid == 0 and not entry['journals'] and not entry['accountingOutbox'])
    summary.append({'orderId': oid, 'orderNumber': order['OrderNumber'], 'couponCode': order['CouponCode'], 'gross': gross,
                    'discount': discount, 'netMerchandise': net, 'shipping': order['ShippingCost'], 'tax': order['TaxAmount'],
                    'fee': order['PaymentFee'], 'total': order['Total'], 'allocated': paid,
                    'acceptedPaymentTotal': accepted_payments, 'remainingPayableAfterDecision': remaining_payable,
                    'legacyItemOnlyBalanceDueUtility': max(Decimal(0), money(order['Total']) - paid),
                    'itemRemaining': max(Decimal(0), net - paid), 'orderStatus': order['Status'], 'paymentStatus': order['PaymentStatus'],
                    'paymentIntentIds': [p['record']['Id'] for p in entry['intents']],
                    'paymentAmounts': [p['record']['Amount'] for p in entry['intents']],
                    'proofs': [p['record']['ProofImageUrl'] for p in entry['intents']],
                    'journals': entry['journals'], 'ledgerLines': entry['ledgerLines']})

wallet_method = next(m for m in state['methods'] if m['Code'] == 'MANUALWALLET')
check('Final wallet disabled without encrypted synthetic recipient', not wallet_method['IsEnabled'] and not wallet_method['has_encrypted_config'])
check('Synthetic coupons inactive with history retained', len(state['coupons']) == 3 and all(not c['record']['IsActive'] for c in state['coupons']))
check('Exact original order SQL lookup absent', not state['diagnostic'])
check('Expired task reservation released', next(r['record']['Status'] for r in state['reservations'] if r['record']['OrderId'] == '4ccce167-b11c-4caa-9ac9-d09dbb494fdf') == 2)
pending = json.loads((OUT / 'sql-underpay-noeffects.json').read_text(encoding='utf-8'))
under = next(o for o in pending['orders'] if o['identity']['Id'] == '190460e3-9b58-4b54-9573-5fd8f2a412b6')
check('Failed underpayment left pending with original amount and no allocation/outbox/journal',
      under['intents'][0]['record']['Status'] == 7 and money(under['intents'][0]['record']['Amount']) == Decimal('522.5') and
      all(money(i['record']['PaidAmount']) == 0 for i in under['items']) and not under['journals'] and not under['accountingOutbox'])

settings = {}
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    path = ROOT / 'PointOfSale.API' / name
    if path.exists():
        section = re.search(r'"MongoDB"\s*:\s*\{([^}]+)\}', path.read_text(encoding='utf-8-sig'))
        if section:
            settings.update(dict(re.findall(r'"(ConnectionString|DatabaseName)"\s*:\s*"([^"\r\n]+)"', section[1])))
with MongoClient(os.environ.get('MongoDB__ConnectionString', settings['ConnectionString']), serverSelectionTimeoutMS=10000) as client:
    db = client[os.environ.get('MongoDB__DatabaseName', settings['DatabaseName'])]
    docs = list(db.orders.find({'TenantId': state['tenants'][0]['Id'], 'OrderId': {'$in': [o['orderId'] for o in summary]}},
                              {'_id': 0, 'TenantId': 1, 'OrderId': 1, 'OrderNumber': 1, 'Status': 1, 'PaymentStatus': 1, 'Total': 1, 'Currency': 1, 'DiscountAmount': 1, 'LastMutationVersion': 1}))
    diagnostic = list(db.orders.find({'OrderNumber': 'ORD-A758-2026-01040'}, {'_id': 0, 'OrderId': 1, 'TenantId': 1, 'OrderNumber': 1}))
    check('Exact original order Mongo lookup absent across configured projection', not diagnostic)
    check('All QA orders projected exactly once', len(docs) == len(summary) and len({d['OrderId'] for d in docs}) == len(summary))
    for doc in docs:
        order = next(o for o in summary if o['orderId'] == doc['OrderId'])
        check(doc['OrderNumber'] + ' projection total', money(str(doc['Total'])) == money(order['total']) and doc['Currency'] == 'EGP')
        check(doc['OrderNumber'] + ' projection states',
              doc['Status'] == {0: 'Pending', 2: 'Processing', 6: 'Cancelled'}[order['orderStatus']] and
              doc['PaymentStatus'] == {0: 'Pending', 2: 'Collected', 8: 'Rejected', 11: 'Expired'}[order['paymentStatus']])
    (OUT / 'mongo-closeout.json').write_text(json.dumps({'observedUtc': datetime.now(timezone.utc).isoformat(), 'readOnly': True, 'databaseName': db.name, 'qaProjections': docs, 'diagnostic': diagnostic}, default=str, indent=2), encoding='utf-8')
(OUT / 'financial-consistency-summary.json').write_text(json.dumps({'observedUtc': datetime.now(timezone.utc).isoformat(), 'checks': checks, 'orders': summary}, default=str, indent=2), encoding='utf-8')
print(json.dumps({'checks': len(checks), 'passed': sum(c['passed'] for c in checks), 'orders': len(summary), 'journals': sum(len(o['journals']) for o in summary), 'mongoCount': len(docs)}))
