"""Extract only task-owned registration evidence and verify preservation."""
import hashlib
import json
import os
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/registration-phone-20261020'
PRIVATE = Path(os.environ['LOCALAPPDATA']) / 'Temp/opencode/registration-phone-20261020'


def text(p):
    raw = p.read_bytes()
    return raw.decode('utf-16') if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else raw.decode('utf-8-sig')


baseline = OUT / 'browser-baseline.txt'
if baseline.exists():
    match = re.search(r'### Result\s*\n([^\r\n]+)', text(baseline))
    if match:
        (OUT / 'browser-baseline.json').write_text(json.dumps(json.loads(match[1]), indent=2), encoding='utf-8')
exceptions = []
for p in PRIVATE.glob('api-*.log'):
    for line in text(p).splitlines():
        if not line.startswith('{'):
            continue
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue
        if ('/api/storefront/auth/register' in entry.get('Message', '') or
                entry.get('Category') == 'PointOfSale.API.Services.Storefront.StorefrontEmailVerificationService') and entry.get('Exception'):
            exceptions.append({k: entry.get(k) for k in ['Timestamp','LogLevel','Category','Message','Exception']})
(OUT / 'registration-exceptions.json').write_text(json.dumps(exceptions, indent=2), encoding='utf-8')
allowed = {
    'PointOfSale.API/Services/Storefront/StorefrontAuthService.cs',
    'PointOfSale.API/Controllers/StorefrontAuthController.cs',
    'PointOfSale.API/Services/Storefront/StorefrontEmailVerificationService.cs',
    'PointOfSale.Storefront/src/auth/api/authApi.ts',
    'PointOfSale.Storefront/src/auth/components/RegisterForm.tsx',
    'PointOfSale.Storefront/src/auth/pages/RegisterPage.tsx',
    'PointOfSale.Storefront/src/auth/store/authStore.ts',
    'PointOfSale.Storefront/src/auth/store/authStore.test.ts',
    'PointOfSale.Storefront/src/auth/types/auth.types.ts',
    'PointOfSale.Storefront/src/i18n/locales/en.json',
    'PointOfSale.Storefront/src/i18n/locales/ar.json',
    'PointOfSale.Tests/Security/StorefrontAuthenticationLifecycleTests.cs',
    'tests/registration-phone-20261020-observer.py',
}
preservation = {'unchanged': [], 'taskTouched': [], 'unexpectedChanges': []}
for entry in json.loads((OUT / 'existing-work-hashes.json').read_text()):
    p = ROOT / entry['path']
    current = hashlib.sha256(p.read_bytes()).hexdigest() if p.exists() else None
    bucket = 'unchanged' if current == entry['sha256'] else 'taskTouched' if entry['path'] in allowed else 'unexpectedChanges'
    preservation[bucket].append(entry['path'])
latest = OUT / 'sql-final.json'
if latest.exists():
    before = json.loads((OUT / 'sql-before.json').read_text())
    after = json.loads(latest.read_text())
    interim = json.loads((OUT / 'sql-post-matrix.json').read_text())
    preservation['existingCustomerRowsUnchanged'] = before['existingCustomerDigests'] == after['existingCustomerDigests']
    preservation['providerIdentityConfigurationUnchanged'] = before['providerConfigurationDigest'] == after['providerConfigurationDigest']
    preservation['providerSettingsUnchangedSincePostMatrix'] = interim['storefrontProviderSettingsDigest'] == after['storefrontProviderSettingsDigest']
    preservation['configurationFilesUnchanged'] = before['configurationFiles'] == after['configurationFiles']
    preservation['noPosUserCreated'] = after['qaPosUsers'][0]['count'] == 0
    preservation['noPosCustomerCreated'] = after['qaPosCustomers'][0]['count'] == 0
(OUT / 'preservation.json').write_text(json.dumps(preservation, indent=2), encoding='utf-8')
print(json.dumps({'registrationExceptions':len(exceptions),'preservedExistingWorkFiles':len(preservation['unchanged']),'unexpectedChanges':preservation['unexpectedChanges'], **{k:v for k,v in preservation.items() if isinstance(v,bool)}}))
