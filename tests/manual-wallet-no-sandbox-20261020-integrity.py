"""Independently check retained readbacks; never mutate application data."""
import hashlib
import json
from decimal import Decimal
from pathlib import Path

root = Path(__file__).resolve().parents[1]
out = root / '.ai/evidence/manual-wallet-no-sandbox-20261020'
load = lambda name: json.loads((out / name).read_text(encoding='utf-8-sig'))
checks = []
def check(name, passed):
    checks.append({'name': name, 'passed': bool(passed)})

before = load('sql-before.json')
final = load('sql-final.json')
methods = {m['Code']: m for m in final['methods']}
original = {m['Code']: m for m in before['methods']}
wallet = methods['MANUALWALLET']
check('Original disabled/empty recipient restored', not wallet['IsEnabled'] and not wallet['has_encrypted_config'] and wallet['encrypted_hash'] == original['MANUALWALLET']['encrypted_hash'])
check('Original legacy mode bit preserved', wallet['IsSandbox'] == original['MANUALWALLET']['IsSandbox'])
check('COD row unchanged', methods['COD'] == original['COD'])
check('No Stripe/Paymob tenant configuration introduced', sorted(methods) == sorted(original))
check('No task order committed by stale refused checkout', load('sql-stale-refused.json')['orders'] == [])
check('Exactly three intentional QA orders retained', len(final['orders']) == 3)
approved = next(o for o in final['orders'] if o['order']['OrderNumber'].endswith('01084'))
rejected = next(o for o in final['orders'] if o['order']['OrderNumber'].endswith('01085'))
cod = next(o for o in final['orders'] if o['order']['OrderNumber'].endswith('01086'))
check('Approved order/intent/accepted quote amounts agree', Decimal(approved['order']['Total']) == Decimal(approved['intents'][0]['Amount']) == Decimal(load('browser-submit.json')['pricing']['total']) == 35)
check('Net merchandise allocation is 25, not shipping-inclusive 35', sum(Decimal(i['PaidAmount']) for i in approved['items']) == 25)
check('Exactly one settlement journal after concurrent stale decisions', len(approved['journals']) == 1)
check('Journal balanced at authoritative payable', len(approved['journals']) == 1 and Decimal(approved['journals'][0]['debit']) == Decimal(approved['journals'][0]['credit']) == 35)
check('Wallet clearing debit1140 / revenue credit4020', {(l['AccountCode'], Decimal(l['DebitAmount']), Decimal(l['CreditAmount'])) for l in approved['ledgerLines']} == {('1140', Decimal(35), Decimal(0)), ('4020', Decimal(0), Decimal(35))})
check('Accounting outbox points to the same single journal', len(approved['outbox']) == 1 and approved['outbox'][0]['JournalId'] == approved['journals'][0]['Id'])
check('Rejected payment has no allocation or settlement', not rejected['journals'] and not rejected['outbox'] and all(Decimal(i['PaidAmount']) == 0 for i in rejected['items']) and rejected['intents'][0]['Status'] == 8)
check('Independent COD has no wallet intent or settlement', not cod['intents'] and not cod['journals'] and not cod['outbox'])
check('Actual tenant/invalid-write/stale-review HTTP guards passed', load('browser-isolation.json')['passed'])
check('Normal no-Sandbox Admin configuration checks passed', all(c['passed'] for c in load('browser-config-verified.json')['checks']))
check('Successful valid rejection through Admin UI', load('browser-reject-confirm.json')['passed'])
check('Unconfigured Enable rejected with actionable recipient error', load('browser-restore.json')['enable']['status'] == 400 and 'wallet number is required' in json.dumps(load('browser-restore.json')['enable']))
check('Final fresh public read only COD with private no-store', load('browser-closeout-verified.json')['passed'] and 'private, no-store' in load('browser-closeout-verified.json')['publicResult']['headers']['cache-control'])
prior = json.loads((root / '.ai/evidence/manual-wallet-visibility-20261020/sql-final.json').read_text(encoding='utf-8-sig'))
history = load('sql-history.json')
prior_orders = {o['order']['Id']: o for o in prior['orders']}
check('Previous visibility QA orders and financial history unchanged', all(prior_orders[o['order']['Id']] == o for o in history['orders']) and len(history['orders']) == len(prior_orders))
runtime_before = load('runtime-before.json')
runtime_final = load('runtime-final.json')
hashes = {f['path']: f['hash'] for f in runtime_before['files']}
for f in runtime_final['files']:
    if f['path'].endswith(('appsettings.json', 'appsettings.Development.json', 'checkoutStore.ts')):
        check('Preserved ' + f['path'], hashes[f['path']] == f['hash'])
dll = next(f for f in runtime_final['files'] if f['path'].endswith('PointOfSale.API.dll'))
check('API DLL unchanged since headed workflows', hashlib.sha256((root / dll['path']).read_bytes()).hexdigest().upper() == dll['hash'])
projections = {p['OrderId']: p for p in load('mongo-final.json')['qaProjections']}
check('Task projections agree with review outcomes and amounts', len(projections) == 3 and projections[approved['order']['Id']]['PaymentStatus'] == 'Collected' and projections[rejected['order']['Id']]['PaymentStatus'] == 'Rejected' and projections[cod['order']['Id']]['PaymentStatus'] == 'Pending' and all(Decimal(p['Total']) == 35 for p in projections.values()))
result = {'checks': checks, 'passed': sum(c['passed'] for c in checks), 'failed': sum(not c['passed'] for c in checks)}
(out / 'integrity.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
raise SystemExit(1 if result['failed'] else 0)
