# AF-12 Diagnostic Script - Check Affiliate Filtering

Run these commands to diagnose why "Anker 999" is still showing in affiliate mode:

## Step 1: Check MongoDB Value

```javascript
// Connect to MongoDB
use PointOfSale

// Find the product "Anker 999"
db.products.find({ 
  Name: "Anker 999" 
}, {
  ProductId: 1,
  Name: 1,
  IsAffiliateEnabled: 1,
  AffiliateEnabledAtUtc: 1,
  TenantId: 1
}).pretty()
```

**Expected Result:**
```json
{
  "ProductId": "some-guid",
  "Name": "Anker 999",
  "IsAffiliateEnabled": false,  // Should be FALSE
  "AffiliateEnabledAtUtc": null,
  "TenantId": "tenant-guid"
}
```

**If `IsAffiliateEnabled: true`** → MongoDB is not updated. Check the POS Admin update logic.

---

## Step 2: Check Elasticsearch Value

```bash
# Replace with your Elasticsearch URL and index name
curl -X GET "http://localhost:9200/products/_search?pretty" \
  -H 'Content-Type: application/json' \
  -d '{
    "query": {
      "bool": {
        "must": [
          { "match": { "Name": "Anker 999" } }
        ]
      }
    },
    "_source": ["ProductId", "Name", "IsAffiliateEnabled", "TenantId"]
  }'
```

**Expected Result:**
```json
{
  "hits": {
    "hits": [{
      "_source": {
        "ProductId": "some-guid",
        "Name": "Anker 999",
        "IsAffiliateEnabled": false,  // Should be FALSE
        "TenantId": "tenant-guid"
      }
    }]
  }
}
```

**If `IsAffiliateEnabled: true`** → Elasticsearch is stale. Need to reindex.

---

## Step 3: Check Affiliate Referral Code

```sql
-- Connect to PostgreSQL
SELECT 
    Id,
    ReferralCode,
    Status,
    TenantId
FROM "AffiliateAccounts"
WHERE "ReferralCode" = 'SOSO2248';
```

**Expected Result:**
```
Id                                   | ReferralCode | Status | TenantId
-------------------------------------|--------------|--------|----------
some-guid                            | SOSO2248     | Active | tenant-guid
```

**If no results** → Referral code doesn't exist. Middleware won't activate affiliate mode.

---

## Step 4: Test API Directly

```bash
# Test without ref parameter (should show all products)
curl "http://localhost:5173/api/storefront/products/discover?page=1&pageSize=20" \
  -H "X-Tenant-Id: YOUR_TENANT_ID" \
  -H "Accept: application/json" | jq '.items[] | {name: .name, isAffiliateEnabled: .isAffiliateEnabled}'

# Test with ref parameter (should show only affiliate products)
curl "http://localhost:5173/api/storefront/products/discover?page=1&pageSize=20&ref=SOSO2248" \
  -H "X-Tenant-Id: YOUR_TENANT_ID" \
  -H "Accept: application/json" | jq '.items[] | {name: .name, isAffiliateEnabled: .isAffiliateEnabled}'
```

**Expected:**
- Without `ref`: All products (including Anker 999)
- With `ref`: Only products where `isAffiliateEnabled: true` (Anker 999 should NOT appear)

---

## Step 5: Check Backend Logs

Look for these log entries when you visit the storefront with `?ref=SOSO2248`:

```
[Affiliate:Referral] Click tracked | AffiliateId: {guid} | Ref: SOSO2248
[Affiliate:Mode] Activated | AffiliateId: {guid} | Ref: SOSO2248
DiscoverProducts request | ... | AffiliateMode: True
```

**If you see `AffiliateMode: False`** → Middleware is not activating affiliate mode.

---

## Step 6: Check Response Headers

```bash
curl -I "http://localhost:5173/api/storefront/products/discover?ref=SOSO2248" \
  -H "X-Tenant-Id: YOUR_TENANT_ID"
```

**Look for:**
```
X-Affiliate-Mode: true
X-Affiliate-Account-Id: some-guid
```

**If missing** → Middleware is not setting headers.

---

## Common Issues & Fixes

### Issue 1: MongoDB Not Updated
**Symptom:** MongoDB shows `IsAffiliateEnabled: true` but you toggled it off in POS Admin

**Fix:** Check the POS Admin product update logic. Ensure it's updating the `IsAffiliateEnabled` field.

---

### Issue 2: Elasticsearch Stale
**Symptom:** MongoDB shows `IsAffiliateEnabled: false` but Elasticsearch shows `true`

**Fix Option A - Trigger Reindex:**
```bash
# Trigger a product update event to reindex
# Update any field on the product in POS Admin (e.g., change description)
```

**Fix Option B - Manual Reindex:**
```bash
# Run the reindex command (if you have one)
dotnet run --project PointOfSale.API -- reindex-products
```

**Fix Option C - Delete and Recreate Index:**
```bash
# Delete the Elasticsearch index
curl -X DELETE "http://localhost:9200/products"

# Recreate and reindex all products
# (Requires a full reindex script)
```

---

### Issue 3: Cache Poisoning (Already Fixed)
**Symptom:** API returns correct data but frontend shows wrong data

**Fix:** Clear Redis cache
```bash
redis-cli FLUSHDB
```

---

### Issue 4: Frontend Not Passing ref Parameter
**Symptom:** URL shows `?ref=SOSO2248` but API logs show `AffiliateMode: False`

**Fix:** Check frontend code - ensure it's passing the `ref` parameter to API calls.

---

## Quick Fix: Force Elasticsearch Reindex

If Elasticsearch is stale, the quickest fix is to update the product in POS Admin:

1. Open "Anker 999" in POS Admin
2. Make a small change (e.g., add a space to description)
3. Click "Update Product"
4. This triggers `ProductUpdatedEvent` → Elasticsearch reindex
5. Check Elasticsearch again (Step 2)

---

## Verification After Fix

1. ✅ MongoDB: `IsAffiliateEnabled: false`
2. ✅ Elasticsearch: `IsAffiliateEnabled: false`
3. ✅ API without ref: Returns "Anker 999"
4. ✅ API with ref: Does NOT return "Anker 999"
5. ✅ Frontend without ref: Shows "Anker 999"
6. ✅ Frontend with ref: Does NOT show "Anker 999"
