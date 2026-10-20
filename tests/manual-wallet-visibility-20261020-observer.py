"""Narrow read-only observations of the configured development database."""
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

import psycopg
from psycopg.rows import dict_row

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/manual-wallet-visibility-20261020'
OUT.mkdir(parents=True, exist_ok=True)
tenant = 'c9c5c1ae-949e-498e-bc63-07242daeb49d'
cs = os.environ.get('ConnectionStrings__DefaultConnection')
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    path = ROOT / 'PointOfSale.API' / name
    if path.exists() and not os.environ.get('ConnectionStrings__DefaultConnection'):
        match = re.search(r'"DefaultConnection"\s*:\s*"([^"\r\n]+)"', path.read_text(encoding='utf-8-sig'))
        if match:
            cs = match[1]
parts = dict((k.strip().lower(), v.strip()) for k, v in (x.split('=', 1) for x in cs.split(';') if '=' in x))
with psycopg.connect(host=parts.get('host', parts.get('server')), port=parts.get('port', 5432), dbname=parts.get('database'), user=parts.get('username', parts.get('user id')), password=parts.get('password'), row_factory=dict_row) as db:
    db.execute('SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY')
    def q(sql, args=()):
        return db.execute(sql, args).fetchall()
    result = {
        'observedUtc': datetime.now(timezone.utc).isoformat(), 'readOnly': True,
        'target': q("select current_database() database, inet_server_addr() host, current_setting('transaction_read_only') read_only"),
        'methods': q('select "Id","TenantId","Code","DisplayName","IsEnabled","IsSandbox","SupportsOffline","Version",("ConfigEncrypted" is not null and length("ConfigEncrypted")>0) has_encrypted_config, md5(coalesce("ConfigEncrypted",\'\')) encrypted_hash from "TenantPaymentMethods" where "TenantId"=%s and not "IsDeleted" order by "Code"', (tenant,)),
        'orders': []}
    for order in q('select "Id","OrderNumber","Total","PaymentStatus","Status","Currency","PaymentMethodCode","CustomerNotes" from "Orders" where "TenantId"=%s and "CustomerNotes" like %s', (tenant, 'MWVIS20261020%')):
        oid = order['Id']
        result['orders'].append({
            'order': order,
            'items': q('select i."Id",i."UnitPrice",i."Quantity",i."DiscountAmount",i."TaxAmount",i."Total",i."PaidAmount" from "OrderItems" i join "Orders" o on o."Id"=i."OrderId" where o."TenantId"=%s and i."OrderId"=%s', (tenant, oid)),
            'intents': q('select "Id","Amount","Currency","Status","ProofImageUrl","Provider" from "PaymentIntents" where "TenantId"=%s and "OrderId"=%s', (tenant, oid)),
            'journals': q('select j."Id",j."SourceType",j."Status",j."IdempotencyKey",sum(l."DebitAmount") debit,sum(l."CreditAmount") credit from "LedgerJournalEntries" j left join "LedgerLines" l on l."JournalEntryId"=j."Id" where j."TenantId"=%s and j."SourceId"=%s group by j."Id"', (tenant, oid)),
            'ledgerLines': q('select l."Id",l."JournalEntryId",a."AccountCode",l."DebitAmount",l."CreditAmount" from "LedgerLines" l join "LedgerAccounts" a on a."Id"=l."AccountId" join "LedgerJournalEntries" j on j."Id"=l."JournalEntryId" where j."TenantId"=%s and j."SourceId"=%s order by l."Id"', (tenant, oid)),
            'outbox': q('select "Id","EventType","JournalId","ProcessedAtUtc" from "AccountingOutboxMessages" where "TenantId"=%s and "BusinessEntityId"=%s', (tenant, oid))})
    db.rollback()
stage = sys.argv[1] if len(sys.argv) > 1 else 'before'
dest = OUT / f'sql-{stage}.json'
dest.write_text(json.dumps(result, default=str, indent=2), encoding='utf-8')
print(json.dumps({'path': str(dest.relative_to(ROOT)), 'methods': result['methods'], 'orderCount': len(result['orders'])}, default=str))
