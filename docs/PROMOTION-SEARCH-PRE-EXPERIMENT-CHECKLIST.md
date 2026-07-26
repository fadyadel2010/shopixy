# Promotion-Aware Search – Pre-Experiment Checklist

## Overview

> ❌ **Do NOT start testing before completing all steps below.**  
> Any skipped step may produce misleading or invalid results.

---

## 🔁 STEP 1 — Elasticsearch Reindex (CRITICAL)

### Why This Is Mandatory

New promotion-aware ranking relies on recently added Elasticsearch fields:

- `HasActivePromotion` (boolean)
- `PromotionDiscountValue` (double)
- `PromotionDiscountType` (keyword)
- `PromotionStartDate` (date - time decay)
- `PromotionEndDate` (date - countdown)
- `SearchBoostOverride` (double - merchandising)

⚠️ **Existing indices DO NOT contain these fields** and will ignore promotion boosts unless reindexed.

### ✅ Option A — Full Reindex via API (Recommended)

#### Step 1.1: Check Current Status

```http
GET /api/admin/search/status
Headers:
  X-Tenant-Id: <TENANT_ID>
  Authorization: Bearer <JWT>
```

#### Step 1.2: Recreate Index with Fresh Mappings

```http
POST /api/admin/search/recreate-index
Headers:
  X-Tenant-Id: <TENANT_ID>
  Authorization: Bearer <JWT>
```

This will:
- Delete the existing products index
- Create a new index with correct field mappings
- Return list of new fields added

#### Step 1.3: Reindex All Products from MongoDB

```http
POST /api/admin/search/reindex
Headers:
  X-Tenant-Id: <TENANT_ID>
  Authorization: Bearer <JWT>
```

Response includes:
- `indexedProducts` - Total products indexed
- `productsWithPromotion` - Products with active promotions
- `errors` - Any indexing errors

### ✅ Option B — Manual Elasticsearch Commands (Dev/Debug)

```bash
# 1️⃣ Delete the existing product index
curl -X DELETE "http://localhost:9200/products_index"

# 2️⃣ Restart the API application
# This ensures new mappings are applied automatically

# 3️⃣ Verify mappings
curl "http://localhost:9200/products_index/_mapping" | jq
```

---

## 🧹 STEP 2 — Redis Cache Flush (MANDATORY)

### Why This Is Required

Promotion ranking uses cached configuration and product lists.
Old cache values will override correct search behavior.

### Option A — Via API (Recommended)

```http
POST /api/admin/search/clear-cache
Headers:
  X-Tenant-Id: <TENANT_ID>
  Authorization: Bearer <JWT>
```

### Option B — Direct Redis Commands

```bash
redis-cli

# Remove store config cache (includes PromotionRankingConfig)
KEYS storeconfig:*
DEL storeconfig:<tenant_id>

# Remove promotion ranking cache
KEYS promotion:ranking:*
DEL <matched_keys>
```

---

## 🔍 STEP 3 — Promotion Sync Verification

### Verify MongoDB Products Have Promotion Fields

```javascript
db.products.find(
  { hasActivePromotion: true },
  {
    productId: 1,
    hasActivePromotion: 1,
    promotionBadge: 1,
    promotionDiscountValue: 1,
    promotionStartDate: 1,
    promotionEndDate: 1,
    searchBoostOverride: 1
  }
).limit(5)
```

**Expected:** Products with promotions should have all fields populated.

### Verify Elasticsearch Mapping

```bash
curl "http://localhost:9200/products_index/_mapping" | jq '.products_index.mappings.properties | keys'
```

**Expected fields:**
- `hasActivePromotion` → boolean
- `promotionDiscountValue` → double
- `promotionStartDate` → date
- `promotionEndDate` → date
- `searchBoostOverride` → double

### Verify Elasticsearch Contains Promotion Data

```bash
curl -X POST "http://localhost:9200/products_index/_search" \
  -H "Content-Type: application/json" \
  -d '{
    "query": {
      "bool": {
        "filter": [
          { "term": { "tenantId": "<TENANT_ID>" } },
          { "term": { "hasActivePromotion": true } }
        ]
      }
    },
    "_source": ["productId", "name", "hasActivePromotion", "promotionDiscountValue", "promotionStartDate", "searchBoostOverride"],
    "size": 5
  }'
```

---

## ✅ READY-TO-TEST CHECKPOINT

**Only proceed to testing AFTER ALL CONDITIONS ARE TRUE:**

| Check | Status |
|-------|--------|
| Elasticsearch reindexed with new mappings | ⬜ |
| Redis promotion/search caches cleared | ⬜ |
| MongoDB product documents contain promotion fields | ⬜ |
| Elasticsearch index contains promotion fields | ⬜ |
| API restarted (if Option A used) | ⬜ |

---

## 🚀 Quick Start (All-in-One)

Run these in sequence:

```bash
# 1. Recreate index
curl -X POST "http://localhost:5000/api/admin/search/recreate-index" \
  -H "X-Tenant-Id: <TENANT_ID>" \
  -H "Authorization: Bearer <JWT>"

# 2. Reindex products
curl -X POST "http://localhost:5000/api/admin/search/reindex" \
  -H "X-Tenant-Id: <TENANT_ID>" \
  -H "Authorization: Bearer <JWT>"

# 3. Clear cache
curl -X POST "http://localhost:5000/api/admin/search/clear-cache" \
  -H "X-Tenant-Id: <TENANT_ID>" \
  -H "Authorization: Bearer <JWT>"

# 4. Verify status
curl "http://localhost:5000/api/admin/search/status" \
  -H "X-Tenant-Id: <TENANT_ID>" \
  -H "Authorization: Bearer <JWT>"
```

---

## 🧪 Testing Scope

After completing the checklist, test these scenarios:

1. **Base Promotion Boost**
   - Products with promotions rank higher than non-promoted products
   - Text relevance is still preserved (search for specific product name)

2. **High Discount Boost**
   - Products with ≥20% discount rank higher than 10% discount

3. **Time Decay**
   - Newer promotions rank higher than older ones
   - Week-old promotions still visible but lower ranked

4. **Manual Boost Override**
   - Set `SearchBoostOverride = 25` on a promotion
   - Verify product jumps to top of results

5. **Countdown Timer**
   - Verify `promotionEndDate` is returned in API
   - Frontend countdown displays correctly

---

## ⚠️ Reminder

Search ranking behavior is **ONLY correct after reindex + cache reset**.
Any test before that is **invalid by definition**.
