# AF-18: Testing Instructions for ZEZO Affiliate Cross-Tenant Support

## What We Fixed

1. Added detailed debug logging to middleware to show:
   - V2 query (PromotionLinks) - should return NULL for ZEZO
   - V1 query (AffiliateAccounts) - should find ZEZO
   - ANY account query - shows Status, IsDeleted, TenantId

2. The frontend IS correctly configured to send `X-Affiliate-Ref` header on all requests

## Testing Steps

### 1. Stop and Restart API
```bash
# Stop the API (Ctrl+C)
# Rebuild (already done)
# Start the API
dotnet run --project PointOfSale.API/PointOfSale.API.csproj
```

### 2. Test from CORRECT Tenant (Mena Store)
ZEZO belongs to Mena tenant, so test from Mena store:

```
http://mena.localhost:5174/products/some-product?ref=ZEZO906409
```

### 3. Check Browser Console
Open browser DevTools → Console tab. You should see:
```
[AffiliateRef] Captured ref code from URL: ZEZO906409
```

### 4. Check Session Storage
Open browser DevTools → Application tab → Session Storage → `http://mena.localhost:5174`

You should see:
```
affiliate_ref_code: ZEZO906409
```

### 5. Check Network Tab
Open browser DevTools → Network tab → Click on any API request

Check Request Headers - you should see:
```
X-Affiliate-Ref: ZEZO906409
X-Tenant-Id: 8a9f672b-43d5-4a2c-9762-5df63410fb73
```

### 6. Check API Logs
Look for these log entries:

```
[AffiliateMW] Ref found: ZEZO906409 | Source: QueryParam | Path: /...
[AF-18:Debug] V2 query starting | ReferralCode: ZEZO906409 | CurrentTenant: 8a9f672b...
[AF-18:Debug] V2 query result | Found: False | LinkId: 
[AF-18:Debug] V1 fallback query starting | ReferralCode: ZEZO906409 | CurrentTenant: 8a9f672b... | Looking for Status: 1 (Active=1)
[AF-18:Debug] ANY account with ref code | Found: True | Id: 019c8fb7... | Status: Active | IsDeleted: False | Tenant: 8a9f672b...
[AF-18:Debug] V1 fallback query result | Found: True | AffiliateId: 019c8fb7...
[Affiliate:Mode] Activated (v1 fallback) | AffiliateId: 019c8fb7... | Ref: ZEZO906409 | AffiliateTenant: 8a9f672b... | OrderTenant: 8a9f672b... | CrossTenant: False
```

### 7. Place Test Order
1. Add product to cart
2. Go to checkout: `http://mena.localhost:5174/checkout`
3. Fill in shipping address
4. Complete checkout

### 8. Check Order in Database
```sql
SELECT 
    "Id",
    "OrderNumber",
    "AffiliateAccountId",
    "AffiliateReferralCode",
    "TenantId"
FROM "Orders"
WHERE "AffiliateReferralCode" = 'ZEZO906409'
ORDER BY "CreatedAt" DESC
LIMIT 1;
```

Should show:
- AffiliateAccountId: `019c8fb7-0756-7ab3-9187-086089e86e06`
- AffiliateReferralCode: `ZEZO906409`
- TenantId: `8a9f672b-43d5-4a2c-9762-5df63410fb73` (Mena)

### 9. Mark Order as Completed
```sql
UPDATE "Orders"
SET "Status" = 'Completed'
WHERE "OrderNumber" = 'ORD-XXXX-XXXX-XXXXX';
```

### 10. Verify Commission Recorded
```sql
SELECT 
    "Id",
    "OrderId",
    "AffiliateAccountId",
    "CommissionAmount",
    "Status"
FROM "AffiliateCommissions"
WHERE "AffiliateAccountId" = '019c8fb7-0756-7ab3-9187-086089e86e06'
ORDER BY "CreatedAt" DESC
LIMIT 1;
```

### 11. Verify ZEZO Appears in Admin
Go to: `http://mena.localhost:5173/admin/affiliates`

ZEZO should now appear in the list.

## Expected Debug Log Output

If everything works, you should see:

```
[AffiliateMW] Ref found: ZEZO906409 | Source: QueryParam | Path: /products/...
[AF-18:Debug] V2 query starting | ReferralCode: ZEZO906409 | CurrentTenant: 8a9f672b...
[AF-18:Debug] V2 query result | Found: False | LinkId: 
[AF-18:Debug] V1 fallback query starting | ReferralCode: ZEZO906409 | CurrentTenant: 8a9f672b... | Looking for Status: 1 (Active=1)
[AF-18:Debug] ANY account with ref code | Found: True | Id: 019c8fb7-0756-7ab3-9187-086089e86e06 | Status: Active | IsDeleted: False | Tenant: 8a9f672b-43d5-4a2c-9762-5df63410fb73
[AF-18:Debug] V1 fallback query result | Found: True | AffiliateId: 019c8fb7-0756-7ab3-9187-086089e86e06
[Affiliate:Mode] Activated (v1 fallback) | AffiliateId: 019c8fb7-0756-7ab3-9187-086089e86e06 | Ref: ZEZO906409 | AffiliateTenant: 8a9f672b-43d5-4a2c-9762-5df63410fb73 | OrderTenant: 8a9f672b-43d5-4a2c-9762-5df63410fb73 | CrossTenant: False
```

## Troubleshooting

### If "ANY account" query returns NULL
- ZEZO account doesn't exist or ReferralCode is wrong
- Run: `SELECT * FROM "AffiliateAccounts" WHERE "ReferralCode" ILIKE '%ZEZO%';`

### If "ANY account" found but "V1 fallback" returns NULL
- Status is not Active (1)
- IsDeleted is True
- Check the Status value in the "ANY account" log

### If middleware never logs anything
- Frontend is not sending X-Affiliate-Ref header
- Check browser console and network tab
- Verify `initAffiliateRefTracker()` is being called

### If checkout doesn't include affiliate
- Check checkout API logs for `[AF-18]` entries
- Verify X-Affiliate-Ref header is sent with checkout POST request
- Check if AffiliateReferralCode is in the request body

## Cross-Tenant Testing (Optional)

To test cross-tenant affiliate selling:

1. Visit Anatico store with ZEZO ref:
   ```
   http://anatico.localhost:5174/products/some-product?ref=ZEZO906409
   ```

2. Complete checkout in Anatico

3. Check logs for:
   ```
   [AF-18:CrossTenant] Affiliate from different tenant | AffiliateId: 019c8fb7... | AffiliateTenant: 8a9f672b... | OrderTenant: fc1b4f0e... | Ref: ZEZO906409
   ```

4. Verify order has:
   - AffiliateAccountId: `019c8fb7...` (ZEZO from Mena)
   - TenantId: `fc1b4f0e...` (Anatico)
   - This proves cross-tenant affiliate selling works!
