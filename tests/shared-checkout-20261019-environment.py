"""Save only non-secret configured service identities and source configuration hashes."""
import hashlib
import json
import re
from pathlib import Path
from datetime import datetime, timezone

root = Path(__file__).resolve().parents[1]
result = {'observedUtc': datetime.now(timezone.utc).isoformat(), 'configurationFiles': [], 'services': {}}
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    path = root / 'PointOfSale.API' / name
    if not path.exists():
        continue
    text = path.read_text(encoding='utf-8-sig')
    result['configurationFiles'].append({'path': str(path.relative_to(root)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    for section, allowed in {'RabbitMQ':['Host','VirtualHost','Port'], 'MongoDB':['DatabaseName'], 'Redis':['ConnectionString'], 'R2':['BucketName','ServiceUrl','UseDevelopmentObjectStorage','Endpoint']}.items():
        match = re.search(r'"'+section+r'"\s*:\s*\{([^}]+)\}', text)
        if match:
            for key in allowed:
                value = re.search(r'"'+key+r'"\s*:\s*("[^"]*"|true|false|\d+)', match[1])
                if value:
                    v = value[1].strip('"')
                    if key == 'ConnectionString':
                        v = v.split(',')[0]
                    result['services'].setdefault(section, {})[key] = v
    match = re.search(r'"MongoDB"\s*:\s*\{[^}]*"ConnectionString"\s*:\s*"([^"]+)"', text)
    if match:
        result['services'].setdefault('MongoDB', {})['endpoint'] = re.sub(r'//[^/@]+@', '//[redacted]@', match[1]).split('?')[0]
result['frontend'] = {'admin':'http://localhost:5173', 'storefront':'http://ahramstore.localhost:5174', 'api':'http://localhost:5010', 'proxy':'/api preserves Host; Vite default API target localhost:5010'}
for project in ['PointOfSale.Web', 'PointOfSale.Storefront']:
    for path in (root / project).glob('.env*'):
        for line in path.read_text(encoding='utf-8-sig').splitlines():
            if re.match(r'^VITE_(DEV_API_TARGET|API_BASE_URL|API_URL|TENANT_ID|ADMIN_ORIGIN)=', line):
                result['frontend'][str(path.relative_to(root)) + ':' + line.split('=')[0]] = line.split('=', 1)[1]
out = root / '.ai/evidence/shared-checkout-20261019/environment.json'
out.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result))
