"""Correlate real browser results with read-only SQL; preserve original observations."""
import json
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / '.ai/evidence/registration-remove-phone-20261020'
def load(name):
    return json.loads((OUT / (name + '.json')).read_text(encoding='utf-8-sig'))

before = load('sql-before')
pre_retry = load('sql-before-retry')
final = load('sql-final')
form = load('browser-form')
loss = load('browser-loss')
retry = load('browser-retry')
verify = load('browser-verify')
existing = load('browser-existing')
primary = load('browser-primary-final')
checks = []
def check(name, passed):
    checks.append({'name': name, 'passed': bool(passed)})

check('No task accounts existed before testing', not before['customers'])
check('All eleven form/layout/validation/request checks passed', len(form['checks']) == 11 and all(c['pass'] for c in form['checks']))
check('Primary EN/LTR and AR/RTL phone-free form and validation passed', not primary['failed'])
check('Exactly two distinct synthetic SQL identities retained, no duplicate retry', len(final['customers']) == 2 and len({c['Email'] for c in final['customers']}) == 2)
check('Real normal and response-loss requests omitted phone', all(set(r['requestKeys']) == {'email', 'displayName', 'password'} for r in [form['registration'], loss['server'], retry]))
check('Both actual registration requests returned 200', form['registration']['status'] == loss['server']['status'] == 200)
check('Response-loss UI retained values and did not retry automatically', not loss['failed'] and loss['postCount'] == 1 and loss['valuesPreserved'])
check('SQL confirmed committed response-loss identity before retry', any(c['Id'] == loss['server']['body']['customer']['id'] for c in pre_retry['customers']))
check('Actual duplicate retry returned EMAIL_EXISTS and retained values', retry['status'] == 400 and retry['body']['code'] == 'EMAIL_EXISTS' and retry['valuesPreserved'])
check('Both identities activated under existing policy and phones persisted NULL', all(c['Phone'] is None and c['IsActive'] and c['has_password'] and not c['PhoneVerified'] for c in final['customers']))
check('Registration initially left email unverified with no login sessions', all(not c['EmailVerified'] for c in pre_retry['customers']) and not pre_retry['sessions'])
check('Exactly two tenant-bound verification records consumed', len(final['verifications']) == 2 and all(v['IsUsed'] and v['TenantId'] == final['tenant'][0]['Id'] for v in final['verifications']))
check('Both verified accounts logged in and logged out', len(verify['results']) == 2 and all(r['verification']['status'] == r['login']['status'] == r['refreshed']['status'] == 200 and r['loggedOut'] for r in verify['results']))
for browser in verify['results']:
    customer = next(c for c in final['customers'] if c['Email'] == browser['email'])
    verification = next(v for v in final['verifications'] if v['OnlineCustomerId'] == customer['Id'])
    check(browser['kind'] + ': SQL, login and refreshed profile identify the same customer', customer['Id'] == browser['login']['customerId'] == browser['refreshed']['body']['id'])
    check(browser['kind'] + ': verification/activation readback matches real verification', customer['EmailVerified'] and customer['IsActive'] and customer['EmailVerifiedAtUtc'] == verification['UsedAtUtc'])
check('New QA sessions revoked through logout', len(final['sessions']) == 2 and all(s['RevokedAtUtc'] for s in final['sessions']))
check('No POS users or POS customers created', final['posUsers'][0]['count'] == final['posCustomers'][0]['count'] == 0)
check('All 54 pre-existing customer phone fingerprints unchanged', len(before['existingPhoneFingerprints']) == 54 and before['existingPhoneFingerprints'] == final['existingPhoneFingerprints'])
check('Saved application configuration unchanged', before['configurationHashes'] == final['configurationHashes'])
check('Stored-phone QA profile and verification/activation data unchanged', before['storedPhoneQa'] == final['storedPhoneQa'] and not existing['failed'])
old = {r['Id']: r['digest'] for r in before['existingAhramIdentityFingerprints']}
changed = [r['Id'] for r in final['existingAhramIdentityFingerprints'] if old.get(r['Id']) != r['digest']]
check('Only the deliberately logged-in existing synthetic identity has whole-row drift', changed == [existing['me']['body']['id']])
result = {'checks': checks, 'passed': sum(c['passed'] for c in checks), 'failed': sum(not c['passed'] for c in checks),
          'originalWholeIdentityFingerprintMatched': final['preservation']['existingAhramIdentityFingerprints'],
          'existingIdentityDrift': changed,
          'driftExplanation': 'Existing synthetic QA login updates LastLoginAtUtc AND UpdatedAtUtc (StorefrontAuthService). Original fingerprint excluded only LastLoginAtUtc. Original mismatch retained; no baseline replaced.'}
(OUT / 'integrity.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
raise SystemExit(bool(result['failed']))
