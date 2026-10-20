"""Correlate explicit browser results with independent, read-only SQL evidence."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/registration-phone-20261020'
sql = json.loads((OUT / 'sql-final.json').read_text())
checks = []


def check(name, condition):
    checks.append({'name': name, 'passed': bool(condition)})


customers = sql['qaCustomers']
by_email = {c['Email']: c for c in customers}
tenant = sql['tenant'][0]['Id']
check('Every retained task email identifies exactly one customer', len(by_email) == len(customers))
check('Every task customer belongs to the resolved AhramStore tenant', all(c['TenantId'] == tenant for c in customers))
check('Every task customer is a local password identity, not a social or POS identity', all(c['Provider'] == 'Local' and c['has_password'] and not c['has_provider_link'] for c in customers))
check('No registration phone is implicitly marked verified', all(not c['PhoneVerified'] for c in customers))
check('Rollback-only test identities are absent', not any('-rollback-' in c['Email'] for c in customers))

for stage in ['baseline', 'matrix', 'matrix-final']:
    data = json.loads((OUT / ('browser-' + stage + '.json')).read_text())
    for result in data['results']:
        email = result['payload']['email']
        customer = by_email.get(email)
        if result['status'] == 200:
            check(stage + '/' + result['name'] + ': HTTP customer ID matches SQL', customer is not None and customer['Id'] == result['body']['customer']['id'])
            check(stage + '/' + result['name'] + ': response and SQL phone agree', customer is not None and customer['Phone'] == result['body']['customer'].get('phone'))
            check(stage + '/' + result['name'] + ': single tenant-owned verification record', sum(v['OnlineCustomerId'] == customer['Id'] and v['TenantId'] == tenant for v in sql['qaVerifications']) == 1 if customer else False)
        else:
            check(stage + '/' + result['name'] + ': rejected request created no customer', customer is None)
        if stage != 'baseline':
            check(stage + '/' + result['name'] + ': one POST despite UI submission', result['postCount'] == 1)
            if result['status'] == 400:
                check(stage + '/' + result['name'] + ': controlled field validation preserved form', result['body']['code'] == 'INVALID_PHONE' and result['body']['data']['field'] == 'phone' and result['valuesPreserved'])

verification = json.loads((OUT / 'browser-verification-final.json').read_text())
for result in verification['results']:
    customer = by_email['regphone20261020-' + result['name'] + '@example.test']
    record = next(v for v in sql['qaVerifications'] if v['OnlineCustomerId'] == customer['Id'])
    check(result['name'] + ': verified state and consumed token share persisted timestamp', customer['EmailVerified'] and customer['EmailVerifiedAtUtc'] is not None and record['IsUsed'] and customer['EmailVerifiedAtUtc'] == record['UsedAtUtc'])
    check(result['name'] + ': real subsequent login and refresh preserve identity', result['afterLogin']['customerId'] == customer['Id'] == result['refreshed']['body']['id'] and result['afterLogin']['emailVerified'] and result['refreshed']['body']['emailVerified'])

lost = json.loads((OUT / 'browser-response-loss.json').read_text())
recovered = json.loads((OUT / 'browser-recovery.json').read_text())
recovery_sql = by_email['regphone20261020-response-loss@example.test']
before_retry = json.loads((OUT / 'sql-response-loss-before-retry.json').read_text())
check('Lost real response was inspected in SQL BEFORE retry', len([c for c in before_retry['qaCustomers'] if c['Id'] == recovery_sql['Id']]) == 1)
check('Lost-response retry and login recovered the same committed identity', lost['server']['body']['customer']['id'] == recovered['login']['customerId'] == recovery_sql['Id'] and recovered['retry']['body']['code'] == 'EMAIL_EXISTS')
check('Lost-response registration dispatched only one verification', sum(v['OnlineCustomerId'] == recovery_sql['Id'] for v in sql['qaVerifications']) == 1)
spoof = json.loads((OUT / 'browser-tenant.json').read_text())
check('Foreign-header UI submission is forbidden and persisted nothing', spoof['status'] == 403 and 'regphone20261020-tenant-spoof@example.test' not in by_email)
check('Every verification record has the correct customer/tenant relationship', all(v['TenantId'] == tenant and any(c['Id'] == v['OnlineCustomerId'] for c in customers) for v in sql['qaVerifications']))
check('Every refresh record has the correct customer/tenant relationship', all(r['TenantId'] == tenant and any(c['Id'] == r['OnlineCustomerId'] for c in customers) for r in sql['qaRefreshTokens']))
check('All task-owned browser login sessions were logged out', all(r['RevokedAtUtc'] is not None for r in sql['qaRefreshTokens']))
readback = json.loads((OUT / 'browser-readback.json').read_text())
for result in readback['results']:
    customer = by_email['regphone20261020-' + result['name'] + '@example.test']
    check(result['name'] + ': refreshed visible profile matches SQL identity/phone/verification',
          result['refreshedAccountVisible'] and result['emailVisible'] and
          result['me']['body']['id'] == customer['Id'] and
          result['me']['body'].get('phone') == customer['Phone'] and
          result['me']['body']['emailVerified'] == customer['EmailVerified'])
check('No POS users or POS customers were created', sql['qaPosUsers'][0]['count'] == sql['qaPosCustomers'][0]['count'] == 0)
result = {'passed': sum(c['passed'] for c in checks), 'failed': sum(not c['passed'] for c in checks), 'retainedQaAccounts': len(customers), 'checks': checks}
(OUT / 'integrity-checks.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({k: result[k] for k in ['passed','failed','retainedQaAccounts']}))
raise SystemExit(1 if result['failed'] else 0)
