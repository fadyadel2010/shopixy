"""Read-only configured DB evidence; never emits secrets or real contact data."""
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

import psycopg
from psycopg.rows import dict_row

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/registration-remove-phone-20261020'
OUT.mkdir(parents=True, exist_ok=True)
STAGE = sys.argv[1]
TENANT = 'c9c5c1ae-949e-498e-bc63-07242daeb49d'
PREFIX = 'regphone20261020-remove-'
config_hashes = {}
connection = None
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    path = ROOT / 'PointOfSale.API' / name
    if path.exists():
        config_hashes[name] = hashlib.sha256(path.read_bytes()).hexdigest()
        match = re.search(r'"DefaultConnection"\s*:\s*"([^"\r\n]+)"', path.read_text(encoding='utf-8-sig'))
        if match:
            connection = match[1]
connection = os.environ.get('ConnectionStrings__DefaultConnection', connection)
parts = dict((k.strip().lower(), v.strip()) for k, v in (x.split('=', 1) for x in connection.split(';') if '=' in x))
with psycopg.connect(host=parts.get('host', parts.get('server')), port=parts.get('port', 5432), dbname=parts.get('database'), user=parts.get('username', parts.get('user id')), password=parts.get('password'), row_factory=dict_row) as db:
    db.execute('SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY')
    def q(sql, params=()):
        return db.execute(sql, params).fetchall()
    result = {
        'observedUtc': datetime.now(timezone.utc).isoformat(),
        'target': q("select current_database() database, inet_server_addr() host, current_setting('transaction_read_only') read_only"),
        'tenant': q('select "Id","Name","Slug","Country","StorefrontBaseUrl" from "Tenants" where "Id"=%s', (TENANT,)),
        'configurationHashes': config_hashes,
        'customers': q('select "Id","TenantId","Email","DisplayName","Phone","IsActive","EmailVerified","EmailVerifiedAtUtc","PhoneVerified",("PasswordHash" is not null) has_password,"CreatedAtUtc","LastLoginAtUtc" from "OnlineCustomers" where "Email" like %s order by "Email"', (PREFIX + '%@example.test',)),
        'verifications': q('select c."Email",v."OnlineCustomerId",v."TenantId",v."IsUsed",v."CreatedAtUtc",v."UsedAtUtc" from "OnlineCustomerEmailVerifications" v join "OnlineCustomers" c on c."Id"=v."OnlineCustomerId" where c."Email" like %s order by c."Email"', (PREFIX + '%@example.test',)),
        'sessions': q('select r."OnlineCustomerId",r."TenantId",r."RevokedAtUtc" from "OnlineCustomerRefreshTokens" r join "OnlineCustomers" c on c."Id"=r."OnlineCustomerId" where c."Email" like %s', (PREFIX + '%@example.test',)),
        'existingPhoneFingerprints': q('select "Id",md5(coalesce("Phone",\'<NULL>\')) digest from "OnlineCustomers" where "Email" not like %s order by "Id"', (PREFIX + '%@example.test',)),
        'existingAhramIdentityFingerprints': q('select "Id",md5((to_jsonb(c)-\'LastLoginAtUtc\')::text) digest from "OnlineCustomers" c where "TenantId"=%s and "Email" not like %s order by "Id"', (TENANT, PREFIX + '%@example.test')),
        'storedPhoneQa': q('select "Id","TenantId","Phone","EmailVerified","IsActive" from "OnlineCustomers" where "Email"=%s', ('regphone20261020-valid-formatted-v2@example.test',)),
        'posUsers': q('select count(*) count from "Users" where lower("Email") like %s', (PREFIX + '%@example.test',)),
        'posCustomers': q('select count(*) count from "Customers" where lower("Email") like %s', (PREFIX + '%@example.test',)),
    }
    db.rollback()
if STAGE == 'final':
    before = json.loads((OUT / 'sql-before.json').read_text(encoding='utf-8'))
    result['preservation'] = {key: before[key] == json.loads(json.dumps(result[key], default=str)) for key in ['configurationHashes', 'existingPhoneFingerprints', 'existingAhramIdentityFingerprints', 'storedPhoneQa']}
(OUT / ('sql-' + STAGE + '.json')).write_text(json.dumps(result, default=str, indent=2), encoding='utf-8')
print(json.dumps({'stage': STAGE, 'target': result['target'], 'customerCount': len(result['customers']), 'customers': result['customers'], 'preservation': result.get('preservation')}, default=str))
