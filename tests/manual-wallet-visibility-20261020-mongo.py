"""Read only the task-owned QA projections, with explicit tenant ownership."""
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path

from pymongo import MongoClient

root = Path(__file__).resolve().parents[1]
out = root / '.ai/evidence/manual-wallet-visibility-20261020'
settings = {}
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    path = root / 'PointOfSale.API' / name
    if path.exists():
        section = re.search(r'"MongoDB"\s*:\s*\{([^}]+)\}', path.read_text(encoding='utf-8-sig'))
        if section:
            settings.update(dict(re.findall(r'"(ConnectionString|DatabaseName)"\s*:\s*"([^"\r\n]+)"', section[1])))
manifest = json.loads((out / 'sql-final.json').read_text())
ids = [entry['order']['Id'] for entry in manifest['orders']]
with MongoClient(os.environ.get('MongoDB__ConnectionString', settings['ConnectionString']), serverSelectionTimeoutMS=10000) as client:
    db = client[os.environ.get('MongoDB__DatabaseName', settings['DatabaseName'])]
    docs = list(db.orders.find({'TenantId': 'c9c5c1ae-949e-498e-bc63-07242daeb49d', 'OrderId': {'$in': ids}}, {'_id': 0, 'TenantId': 1, 'OrderId': 1, 'OrderNumber': 1, 'Status': 1, 'PaymentStatus': 1, 'Total': 1, 'Currency': 1, 'LastMutationVersion': 1}))
result = {'observedUtc': datetime.now(timezone.utc).isoformat(), 'readOnly': True, 'qaProjections': docs}
(out / 'mongo-final.json').write_text(json.dumps(result, default=str, indent=2), encoding='utf-8')
print(json.dumps(result, default=str))
