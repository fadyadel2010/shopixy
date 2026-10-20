"""Narrow, read-only observer for the configured coupon/checkout QA task."""
import os
import re
import json
from pathlib import Path
from datetime import datetime, timezone
import psycopg
from psycopg.rows import dict_row

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.ai/evidence/coupon-checkout-20261019'
OUT.mkdir(parents=True, exist_ok=True)
cs = os.environ.get('ConnectionStrings__DefaultConnection')
for name in ['appsettings.json', 'appsettings.Development.json', 'appsettings.Development.local.json']:
    p = ROOT / 'PointOfSale.API' / name
    if p.exists() and not os.environ.get('ConnectionStrings__DefaultConnection'):
        match = re.search(r'"DefaultConnection"\s*:\s*"([^"\r\n]+)"', p.read_text(encoding='utf-8-sig'))
        if match:
            cs = match[1]
parts = dict((k.strip().lower(), v.strip()) for k, v in (x.split('=', 1) for x in cs.split(';') if '=' in x))
with psycopg.connect(host=parts.get('host', parts.get('server')), port=parts.get('port', 5432), dbname=parts.get('database'), user=parts.get('username', parts.get('user id')), password=parts.get('password'), row_factory=dict_row) as db:
    db.execute('SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY')
    def q(sql, params=()):
        return db.execute(sql, params).fetchall()
    tenants = q('select "Id","Name","Slug","Status","Currency","StorefrontBaseUrl","DeletedAtUtc" from "Tenants" where lower("Slug") in (\'nissan\',\'ahramstore\') or "Id"::text like \'a758%%\'')
    tids = [t['Id'] for t in tenants]
    result = {'observedUtc': datetime.now(timezone.utc).isoformat(), 'readOnly': True,
        'target': q("select current_database() database, inet_server_addr() host, current_setting('transaction_read_only') read_only"),
        'tenants': tenants,
        'domains': q('select "Id","TenantId","HostName","NormalizedHostName" from "Domains" where "TenantId"=ANY(%s) or lower("HostName") like \'%%nissan%%\'', (tids,)),
        'coupons': q('select to_jsonb(c) record from "Coupons" c where "Code"=\'1441\' or ("TenantId"=ANY(%s) and "Code" like \'CCQA20261019%%\')', (tids,)),
        'methods': q('select "Id","TenantId","Code","DisplayName","IsEnabled","Version",("ConfigEncrypted" is not null and length("ConfigEncrypted")>0) has_encrypted_config, md5(coalesce("ConfigEncrypted",\'\')) encrypted_hash from "TenantPaymentMethods" where "TenantId"=ANY(%s) order by "TenantId","Code"', (tids,)),
        'diagnostic': q('select to_jsonb(o) - ARRAY[\'GuestName\',\'GuestEmail\',\'GuestPhone\',\'ShippingAddress\',\'BillingAddress\',\'CustomerNotes\'] record, md5(to_jsonb(o)::text) protected_hash from "Orders" o where "OrderNumber"=%s', ('ORD-A758-2026-01040',)),
        'products': q('select p."Id",p."TenantId",p."Name",p."Slug",p."IsActive",p."IsDeleted",v."Id" variant_id,v."SKU",v."Price",v."OnlinePrice",v."IsActive" variant_active from "Products" p join "ProductVariants" v on v."ProductId"=p."Id" where p."TenantId"=ANY(%s) and (lower(p."Name") like \'%%iphone%%\' or p."Slug"=\'cert-media-simple-20261007\')', (tids,))}
    coupon_ids = [c['record']['Id'] for c in result['coupons']]
    result['reservations'] = q('select to_jsonb(r) record from "CouponReservations" r where "CouponId"=ANY(%s::uuid[])', (coupon_ids,))
    result['usages'] = q('select to_jsonb(u) record from "CouponUsages" u where "CouponId"=ANY(%s::uuid[])', (coupon_ids,))
    result['failedCheckoutEvents'] = q('select "Id","EventType","EventPayload","CreatedAtUtc" from "OutboxMessages" where "EventType" like \'%%CheckoutFailed%%\' and "EventPayload" like %s', ('%d48bf4b1-3b66-4879-92a5-12f74b00bacb%',))
    orders = q('select "Id","OrderNumber","TenantId" from "Orders" where "OrderNumber"=%s or "CustomerNotes" like %s', ('ORD-A758-2026-01040', 'CCQA20261019%'))
    result['orders'] = []
    for order in orders:
        oid = order['Id']
        result['orders'].append({'identity': order,
            'order': q('select to_jsonb(o) - ARRAY[\'GuestName\',\'GuestEmail\',\'GuestPhone\',\'ShippingAddress\',\'BillingAddress\'] record, md5(to_jsonb(o)::text) protected_hash from "Orders" o where "Id"=%s', (oid,)),
            'items': q('select to_jsonb(i) record from "OrderItems" i where "OrderId"=%s order by "Id"', (oid,)),
            'intents': q('select to_jsonb(p) - ARRAY[\'WalletPhoneNumber\',\'ProviderMetadata\',\'RawResponse\'] record from "PaymentIntents" p where "OrderId"=%s order by "Id"', (oid,)),
            'journals': q('select j."Id",j."SourceId",j."SourceType",j."Status",j."IdempotencyKey",sum(l."DebitAmount") debit,sum(l."CreditAmount") credit from "LedgerJournalEntries" j left join "LedgerLines" l on l."JournalEntryId"=j."Id" where j."SourceId"=%s group by j."Id" order by j."Id"', (oid,)),
            'ledgerLines': q('select l."Id",l."JournalEntryId",a."AccountCode",l."DebitAmount",l."CreditAmount" from "LedgerLines" l join "LedgerAccounts" a on a."Id"=l."AccountId" join "LedgerJournalEntries" j on j."Id"=l."JournalEntryId" where j."SourceId"=%s order by l."Id"', (oid,)),
            'accountingOutbox': q('select "Id","EventType","IdempotencyKey","JournalId","ProcessedAtUtc" from "AccountingOutboxMessages" where "BusinessEntityId"=%s order by "Id"', (oid,))})
        result['orders'][-1]['idempotency'] = q('select to_jsonb(r) - ARRAY[\'ResponsePayload\',\'ResponseJson\'] record from "OrderRequestIdempotency" r where "OrderId"=%s', (oid,))
    db.rollback()
path = OUT / ('sql-' + os.environ.get('CC_OBSERVER_STAGE', 'before') + '.json')
path.write_text(json.dumps(result, default=str, indent=2), encoding='utf-8')
print(json.dumps({'path': str(path.relative_to(ROOT)), 'tenants': tenants, 'coupons': result['coupons'], 'diagnosticCount': len(result['diagnostic']), 'orderCount': len(orders)}, default=str))
