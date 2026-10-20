"""Narrow, read-only observations of the configured development registration database."""
import hashlib
import json
import os
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

import psycopg
from psycopg.rows import dict_row

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/registration-phone-20261020'
OUT.mkdir(parents=True, exist_ok=True)
STAGE = sys.argv[1] if len(sys.argv) > 1 else 'before'
TENANT = 'c9c5c1ae-949e-498e-bc63-07242daeb49d'
PREFIX = 'regphone20261020-'
config = {}
config_files = []
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    p = ROOT / 'PointOfSale.API' / name
    if p.exists():
        text = p.read_text(encoding='utf-8-sig')
        config_files.append({'path': str(p.relative_to(ROOT)), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
        match = re.search(r'"DefaultConnection"\s*:\s*"([^"\r\n]+)"', text)
        if match:
            config['connection'] = match[1]
        smtp = re.search(r'"Smtp"\s*:\s*\{([^}]+)\}', text, re.S)
        if smtp:
            for key in ['Enabled', 'Host', 'Port']:
                match = re.search(r'"' + key + r'"\s*:\s*("[^"\r\n]*"|true|false|\d+)', smtp[1])
                if match:
                    config['smtp' + key] = json.loads(match[1])
cs = os.environ.get('ConnectionStrings__DefaultConnection', config['connection'])
parts = dict((k.strip().lower(), v.strip()) for k, v in (x.split('=', 1) for x in cs.split(';') if '=' in x))
with psycopg.connect(host=parts.get('host', parts.get('server')), port=parts.get('port', 5432), dbname=parts.get('database'), user=parts.get('username', parts.get('user id')), password=parts.get('password'), row_factory=dict_row) as db:
    db.execute('SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY')
    def q(sql, params=()):
        return db.execute(sql, params).fetchall()
    result = {
        'observedUtc': datetime.now(timezone.utc).isoformat(),
        'target': q("select current_database() database, inet_server_addr() host, current_setting('transaction_read_only') read_only"),
        'configurationFiles': config_files,
        'smtp': {k: v for k, v in config.items() if k.startswith('smtp')},
        'tenant': q('select "Id","Name","Slug","Country","StorefrontBaseUrl" from "Tenants" where "Id"=%s', (TENANT,)),
        'columns': q("select table_name,column_name,data_type,is_nullable,character_maximum_length from information_schema.columns where table_schema='public' and table_name in ('OnlineCustomers','PhoneRiskProfiles','OnlineCustomerEmailVerifications','OnlineCustomerAuthProviders') order by table_name,ordinal_position"),
        'indexes': q("select tablename,indexname,indexdef from pg_indexes where schemaname='public' and tablename in ('OnlineCustomers','PhoneRiskProfiles') order by tablename,indexname"),
        'constraints': q("select conrelid::regclass::text table_name, conname, pg_get_constraintdef(oid) definition from pg_constraint where conrelid in ('\"OnlineCustomers\"'::regclass,'\"PhoneRiskProfiles\"'::regclass) order by conname"),
        'qaCustomers': q('select "Id","TenantId","Email","NormalizedEmail","DisplayName","Phone","Provider",("ProviderUserId" is not null) has_provider_link,("PasswordHash" is not null) has_password,"IsActive","EmailVerified","EmailVerifiedAtUtc","PhoneVerified","CreatedAtUtc","LastLoginAtUtc" from "OnlineCustomers" where "Email" like %s order by "Email"', (PREFIX + '%@example.test',)),
        'qaVerifications': q('select v."Id",v."TenantId",v."OnlineCustomerId",v."IsUsed",v."CreatedAtUtc",v."ExpiresAtUtc",v."UsedAtUtc" from "OnlineCustomerEmailVerifications" v join "OnlineCustomers" c on c."Id"=v."OnlineCustomerId" where c."Email" like %s order by c."Email",v."CreatedAtUtc"', (PREFIX + '%@example.test',)),
        'qaRefreshTokens': q('select r."Id",r."TenantId",r."OnlineCustomerId",r."CreatedAtUtc",r."ExpiresAtUtc",r."RevokedAtUtc" from "OnlineCustomerRefreshTokens" r join "OnlineCustomers" c on c."Id"=r."OnlineCustomerId" where c."Email" like %s order by c."Email"', (PREFIX + '%@example.test',)),
        'safePhoneCollisions': q('select count(*) count from "OnlineCustomers" where "Phone" in (%s,%s,%s,%s,%s,%s)', ('+12025550101','+12025550102','+12025550103','+12025550104','+12025550105','+12025550106')),
        'providerConfigurationDigest': q('select "Id",md5(to_jsonb(p)::text) digest from "OnlineCustomerAuthProviders" p where "TenantId"=%s order by "Id"', (TENANT,)),
        'storefrontProviderSettingsDigest': q('select "TenantId",md5(to_jsonb(p)::text) digest from "StorefrontAuthProviderSettings" p where "TenantId"=%s', (TENANT,)),
        'qaPosUsers': q('select count(*) count from "Users" where lower("Email") like %s', (PREFIX + '%@example.test',)),
        'qaPosCustomers': q('select count(*) count from "Customers" where lower("Email") like %s', (PREFIX + '%@example.test',)),
        'existingCustomerDigests': q('select "Id",md5(to_jsonb(c)::text) digest from "OnlineCustomers" c where "TenantId"=%s and "Email" not like %s order by "Id"', (TENANT, PREFIX + '%@example.test')),
    }
    db.rollback()
path = OUT / ('sql-' + STAGE + '.json')
path.write_text(json.dumps(result, default=str, indent=2), encoding='utf-8')
if STAGE == 'before':
    status = subprocess.check_output(['git', 'status', '--porcelain=v1', '-uall'], cwd=ROOT)
    (OUT / 'workspace-before.txt').write_bytes(status)
    files = []
    for line in status.decode().splitlines():
        name = line[3:]
        p = ROOT / name
        if p.is_file() and not any(x in name for x in ['.opencode-tmp/', '.ai/evidence/', '.playwright-cli/']):
            files.append({'path': name, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
    (OUT / 'existing-work-hashes.json').write_text(json.dumps(files, indent=2), encoding='utf-8')
print(json.dumps({**{k: result[k] for k in ['target','tenant','smtp','safePhoneCollisions']},
                  'qaCustomerCount': len(result['qaCustomers']),
                  'qaVerificationCount': len(result['qaVerifications']),
                  'evidence': str(path.relative_to(ROOT))}, default=str))
