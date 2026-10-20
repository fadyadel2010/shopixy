"""Read-only configured-development observer; never changes application data."""
import os
import re
import json
import hashlib
import subprocess
from pathlib import Path
from datetime import datetime, timezone
import psycopg
from psycopg.rows import dict_row

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/shared-checkout-20261019'
OUT.mkdir(parents=True, exist_ok=True)
stage = os.environ.get('SHARED_STAGE', 'before')
cs = os.environ.get('ConnectionStrings__DefaultConnection')
if not cs:
    for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
        p = ROOT / 'PointOfSale.API' / name
        if p.exists():
            match = re.search(r'"DefaultConnection"\s*:\s*"([^"\r\n]+)"', p.read_text(encoding='utf-8-sig'))
            if match:
                cs = match[1]
parts = dict((k.strip().lower(), v.strip()) for k, v in (x.split('=', 1) for x in cs.split(';') if '=' in x))
tenant = 'c9c5c1ae-949e-498e-bc63-07242daeb49d'
with psycopg.connect(host=parts.get('host', parts.get('server')), port=parts.get('port', 5432), dbname=parts.get('database'), user=parts.get('username', parts.get('user id')), password=parts.get('password'), row_factory=dict_row) as db:
    db.execute('SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY')
    def q(sql, params=()):
        return db.execute(sql, params).fetchall()
    result = {'observedUtc': datetime.now(timezone.utc).isoformat(), 'readOnly': True,
        'target': q("select current_database() database, inet_server_addr() host, current_setting('transaction_read_only') read_only"),
        'tenants': q('select "Id","Name","Slug","Status","Currency","StorefrontBaseUrl" from "Tenants" where "Id"=%s or lower("Slug")=\'nissan\'', (tenant,)),
        'diagnostic': q('select "Id","OrderNumber","TenantId","Total","Currency" from "Orders" where "OrderNumber"=%s', ('ORD-A758-2026-01040',)),
        'methods': q('select "Id","TenantId","Code","DisplayName","IsEnabled","Version",("ConfigEncrypted" is not null and length("ConfigEncrypted")>0) has_encrypted_config, md5(coalesce("ConfigEncrypted",\'\')) encrypted_hash from "TenantPaymentMethods" where "TenantId"=%s order by "Code"', (tenant,)),
        'coupons': q('select to_jsonb(c) record from "Coupons" c where "Code"=\'1441\' or ("TenantId"=%s and ("Code" like \'SHQA20261019%%\' or "IsActive"=true))', (tenant,)),
        'products': q('select p."Id",p."Name",p."Slug",v."Id" variant_id,v."SKU",v."Price",v."OnlinePrice",v."IsActive" from "Products" p join "ProductVariants" v on v."ProductId"=p."Id" where p."TenantId"=%s and (p."Slug" in (\'iphone-17-pro-max\',\'cert-media-simple-20261007\') or v."SKU"=\'IPHONE-18-BLACK\')', (tenant,))}
    orders = q('select "Id","OrderNumber","TenantId" from "Orders" where "TenantId"=%s and ("CustomerNotes" like \'SHQA20261019%%\' or "OrderNumber" like \'SHQA-20261019%%\') order by "CreatedAt"', (tenant,))
    result['orders'] = []
    for order in orders:
        oid = order['Id']
        result['orders'].append({'identity': order,
            'order': q('select to_jsonb(o) - ARRAY[\'GuestName\',\'GuestEmail\',\'GuestPhone\',\'ShippingAddress\',\'BillingAddress\'] record from "Orders" o where "Id"=%s', (oid,)),
            'items': q('select to_jsonb(i) record from "OrderItems" i where "OrderId"=%s order by "Id"', (oid,)),
            'intents': q('select to_jsonb(p) - ARRAY[\'WalletPhoneNumber\',\'ProviderMetadata\',\'RawResponse\'] record from "PaymentIntents" p where "OrderId"=%s', (oid,)),
            'reservations': q('select to_jsonb(r) record from "CouponReservations" r where "OrderId"=%s', (oid,)),
            'usages': q('select to_jsonb(u) record from "CouponUsages" u where "OrderId"=%s', (oid,)),
            'stock': q('select to_jsonb(r) record from "InventoryReservations" r where "OrderId"=%s', (oid,)),
            'checkoutSessions': q('select "Id","TenantId","CartId","OnlineCustomerId","Status","ConvertedOrderId","ConvertedAtUtc" from "CheckoutSessions" where "ConvertedOrderId"=%s', (oid,)),
            'outbox': q('select "Id","EventType","EventVersion","ProcessedAtUtc","RetryCount","RequiresOperatorReconciliation" from "OutboxMessages" where "AggregateId"=%s and "TenantId"=%s order by "CreatedAtUtc"', (oid, tenant)),
            'journals': q('select j."Id",j."SourceId",j."SourceType",j."Status",j."IdempotencyKey",sum(l."DebitAmount") debit,sum(l."CreditAmount") credit from "LedgerJournalEntries" j left join "LedgerLines" l on l."JournalEntryId"=j."Id" where j."SourceId"=%s group by j."Id" order by j."Id"', (oid,)),
            'ledgerLines': q('select l."Id",l."JournalEntryId",a."AccountCode",l."DebitAmount",l."CreditAmount" from "LedgerLines" l join "LedgerAccounts" a on a."Id"=l."AccountId" join "LedgerJournalEntries" j on j."Id"=l."JournalEntryId" where j."SourceId"=%s order by l."Id"', (oid,)),
            'accountingOutbox': q('select "Id","EventType","IdempotencyKey","JournalId","ProcessedAtUtc" from "AccountingOutboxMessages" where "BusinessEntityId"=%s', (oid,)),
            'idempotency': q('select to_jsonb(r) - ARRAY[\'ResponsePayload\',\'ResponseJson\'] record from "OrderRequestIdempotency" r where "OrderId"=%s', (oid,))})
    result['failedEvents'] = q('select "Id","EventType","EventPayload","CreatedAtUtc" from "OutboxMessages" where "EventType" like \'%%CheckoutFailed%%\' and "CreatedAtUtc">now()-interval \'2 hours\' order by "CreatedAtUtc" desc limit 10')
    db.rollback()
path = OUT / ('sql-' + stage + '.json')
path.write_text(json.dumps(result, default=str, indent=2), encoding='utf-8')
if stage == 'before':
    status = subprocess.check_output(['git', 'status', '--porcelain=v1', '-uall'], cwd=ROOT)
    (OUT / 'workspace-before.txt').write_bytes(status)
    files = []
    for line in status.decode().splitlines():
        name = line[3:]
        p = ROOT / name
        if p.is_file() and not any(x in name for x in ['.opencode-tmp/', '.ai/evidence/', '.playwright-cli/']):
            files.append({'path': name, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
    (OUT / 'existing-work-hashes.json').write_text(json.dumps(files, indent=2), encoding='utf-8')
print(json.dumps({'path': str(path.relative_to(ROOT)), 'target': result['target'], 'methods': result['methods'], 'originalOrderCount': len(result['diagnostic']), 'couponCodes': [c['record']['Code'] for c in result['coupons']], 'orders': orders}, default=str))
