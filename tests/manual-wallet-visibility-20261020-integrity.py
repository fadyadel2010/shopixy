"""Assert the narrow financial/configuration closeout and preserve pre-existing edits."""
import hashlib
import json
from decimal import Decimal
from pathlib import Path

root = Path(__file__).resolve().parents[1]
out = root / '.ai/evidence/manual-wallet-visibility-20261020'
read = lambda name: json.loads((out / name).read_text(encoding='utf-8-sig'))
checks = []
def check(name, passed):
    checks.append({'name': name, 'passed': bool(passed)})

before, final = read('sql-before.json'), read('sql-final.json')
bm, fm = ({m['Code']: m for m in data['methods']} for data in [before, final])
check('COD persisted configuration/version unchanged', bm['COD'] == fm['COD'])
check('Only original two payment rows remain; no Stripe/Paymob mutation', set(fm) == set(bm) == {'COD', 'MANUALWALLET'})
wallet = fm['MANUALWALLET']
check('Final wallet disabled with no encrypted synthetic recipient', not wallet['IsEnabled'] and not wallet['has_encrypted_config'])
check('Original wallet metadata, Sandbox and support flags preserved', all(wallet[k] == bm['MANUALWALLET'][k] for k in ['Id', 'TenantId', 'Code', 'DisplayName', 'IsSandbox', 'SupportsOffline', 'encrypted_hash']))
check('Exactly two new task orders; stale requests created none', len(final['orders']) == 2 and len(read('sql-stale-refused.json')['orders']) == 0)
orders = {o['order']['PaymentMethodCode']: o for o in final['orders']}
check('Wallet and COD both retained', set(orders) == {'COD', 'MANUALWALLET'})
wallet_order = orders['MANUALWALLET']
intent = wallet_order['intents'][0]
quote = read('browser-submit.json')['pricing']
check('Quote, persisted wallet order and intent amount agree at35', Decimal(str(quote['total'])) == Decimal(wallet_order['order']['Total']) == Decimal(intent['Amount']) == 35)
check('Wallet intent Paid and order Collected/Processing', intent['Status'] == 2 and wallet_order['order']['PaymentStatus'] == 2 and wallet_order['order']['Status'] == 2)
check('Net merchandise allocation25, shipping covered at order level', sum(Decimal(i['PaidAmount']) for i in wallet_order['items']) == 25)
check('One posted balanced wallet journal35', len(wallet_order['journals']) == 1 and all(j['Status'] == 1 and Decimal(j['debit']) == Decimal(j['credit']) == 35 for j in wallet_order['journals']))
check('Accounting outbox processed and points to retained journal', len(wallet_order['outbox']) == 1 and wallet_order['outbox'][0]['ProcessedAtUtc'] and wallet_order['outbox'][0]['JournalId'] == wallet_order['journals'][0]['Id'])
cod = orders['COD']
check('COD35 remains pending unpaid with no wallet intent or journal', Decimal(cod['order']['Total']) == 35 and cod['order']['PaymentStatus'] == 0 and cod['order']['Status'] == 0 and not cod['intents'] and not cod['journals'] and not cod['outbox'])
projections = {p['OrderId']: p for p in read('mongo-final.json')['qaProjections']}
for entry in final['orders']:
    order = entry['order']
    projection = projections.get(order['Id'])
    check('Projection identity/tenant/total/currency agrees: ' + order['OrderNumber'], projection and projection['TenantId'] == wallet['TenantId'] and Decimal(projection['Total']) == Decimal(order['Total']) and projection['Currency'] == order['Currency'])
    expected = ('Processing', 'Collected') if order['PaymentMethodCode'] == 'MANUALWALLET' else ('Pending', 'Pending')
    check('Projection lifecycle agrees: ' + order['OrderNumber'], projection and (projection['Status'], projection['PaymentStatus']) == expected)
for file in read('existing-work-hashes.json'):
    check('Unrelated original edit byte-preserved: ' + file['path'], hashlib.sha256((root / file['path']).read_bytes()).hexdigest().upper() == file['hash'])
result = {'checks': checks, 'passed': sum(c['passed'] for c in checks), 'failed': sum(not c['passed'] for c in checks)}
(out / 'integrity.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
raise SystemExit(1 if result['failed'] else 0)
