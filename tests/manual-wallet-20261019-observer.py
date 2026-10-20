"""Task-owned read-only configured PostgreSQL observer. Never prints connection secrets."""
import os, re, json, hashlib
from pathlib import Path
from datetime import datetime, timezone
import psycopg
from psycopg.rows import dict_row

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/manual-wallet-20261019'
OUT.mkdir(parents=True, exist_ok=True)
cs = os.environ.get('ConnectionStrings__DefaultConnection')
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    p = ROOT / 'PointOfSale.API' / name
    if p.exists() and not os.environ.get('ConnectionStrings__DefaultConnection'):
        m = re.search(r'"DefaultConnection"\s*:\s*"([^"\r\n]+)"', p.read_text(encoding='utf-8-sig'))
        if m: cs = m[1]
parts = dict((k.strip().lower(), v.strip()) for k,v in (x.split('=',1) for x in cs.split(';') if '=' in x))
stage = os.environ.get('MW_OBSERVER_STAGE', 'before')
with psycopg.connect(host=parts.get('host', parts.get('server')), port=parts.get('port',5432), dbname=parts.get('database'), user=parts.get('username',parts.get('user id')), password=parts.get('password'), row_factory=dict_row) as db:
    db.execute('SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY')
    def q(sql, params=()): return db.execute(sql, params).fetchall()
    tenant = 'c9c5c1ae-949e-498e-bc63-07242daeb49d'
    result = {'observedUtc':datetime.now(timezone.utc).isoformat(), 'readOnly':True,
        'target':q('select current_database() database, inet_server_addr() host, current_setting(\'transaction_read_only\') read_only'),
        'methods':q('select "Id","TenantId","Code","IsEnabled","Version",("ConfigEncrypted" is not null and length("ConfigEncrypted")>0) as has_encrypted_config, md5(coalesce("ConfigEncrypted",\'\')) encrypted_hash from "TenantPaymentMethods" where "TenantId"=%s order by "Code"',(tenant,)),
        'diagnostic': q('select to_jsonb(o) - ARRAY[\'GuestName\',\'GuestEmail\',\'GuestPhone\',\'ShippingAddress\',\'BillingAddress\',\'CustomerNotes\'] as record, md5(to_jsonb(o)::text) as protected_hash from "Orders" o where "OrderNumber"=%s',('ORD-A758-2026-01040',))}
    orders = q('select "Id","OrderNumber","TenantId" from "Orders" where "OrderNumber"=%s or "CustomerNotes" like %s',('ORD-A758-2026-01040','MWQA20261019%'))
    result['orders'] = []
    for o in orders:
        oid = o['Id']
        entry = {'identity':o,
            'order':q('select to_jsonb(o) - ARRAY[\'GuestName\',\'GuestEmail\',\'GuestPhone\',\'ShippingAddress\',\'BillingAddress\'] record, md5(to_jsonb(o)::text) protected_hash from "Orders" o where "Id"=%s',(oid,)),
            'items':q('select to_jsonb(i) record from "OrderItems" i where "OrderId"=%s order by "Id"',(oid,)),
            'intents':q('select to_jsonb(p) - ARRAY[\'WalletPhoneNumber\',\'ProviderMetadata\',\'RawResponse\'] record, md5(to_jsonb(p)::text) protected_hash from "PaymentIntents" p where "OrderId"=%s order by "Id"',(oid,)),
            'journals':q('select j."Id",j."SourceId",j."SourceType",j."Status",j."IdempotencyKey",sum(l."DebitAmount") debit,sum(l."CreditAmount") credit from "LedgerJournalEntries" j left join "LedgerLines" l on l."JournalEntryId"=j."Id" where j."SourceId"=%s group by j."Id" order by j."Id"',(oid,)),
            'ledgerLines':q('select l."Id",l."JournalEntryId",a."AccountCode",l."DebitAmount",l."CreditAmount" from "LedgerLines" l join "LedgerAccounts" a on a."Id"=l."AccountId" join "LedgerJournalEntries" j on j."Id"=l."JournalEntryId" where j."SourceId"=%s order by l."Id"',(oid,)),
            'accountingOutbox':q('select "Id","EventType","IdempotencyKey","JournalId","ProcessedAtUtc" from "AccountingOutboxMessages" where "BusinessEntityId"=%s order by "Id"',(oid,))}
        result['orders'].append(entry)
    db.rollback()
path = OUT / f'sql-{stage}.json'
path.write_text(json.dumps(result,default=str,indent=2),encoding='utf-8')
print(json.dumps({'path':str(path.relative_to(ROOT)), 'diagnosticCount':len(result['diagnostic']), 'orderCount':len(orders), 'methods':result['methods']},default=str))
