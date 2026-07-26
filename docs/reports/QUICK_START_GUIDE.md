# Quick Start Guide - AF-22 + FE-1

## Problem: "No Products Found"

You're seeing the new UI (✅ FE-1 works!) but no products because:

1. ❌ **AF-22 Consumer was NOT registered** (FIXED NOW!)
2. ❌ MongoDB collection is empty

## Critical Fix Applied ✅

The `AffiliateProductIntelligenceProjectionConsumer` was missing from MassTransit registration.

**This has been FIXED in `PointOfSale.API/Program.cs`**

## Solution: Restart Backend + Create Product

### Option 1: Quick Test Data (Recommended for Testing)

**Step 1:** Open MongoDB shell or MongoDB Compass

**Step 2:** Run the test data script:

```bash
# Using mongosh
mongosh PosEcommerceDb < AF-22-Insert-Test-Data.js

# OR copy-paste the contents of AF-22-Insert-Test-Data.js into MongoDB Compass shell
```

**Step 3:** Verify data was inserted:

```javascript
use PosEcommerceDb
db.affiliate_product_intelligence.countDocuments()
// Should return: 5
```

**Step 4:** Refresh the page:
```
http://localhost:5173/affiliate/portal?tab=discover
```

You should now see 5 test products with:
- ✅ Commission badges (gold/blue/gray)
- ✅ Stock badges (green/orange/gray)
- ✅ Product images
- ✅ Clickable cards that open drawer

---

### Option 2: Trigger Real Events (Production Way)

**Step 1:** Go to your POS admin panel

**Step 2:** Edit any existing product:
- Change the name, price, or description
- Save the product

**Step 3:** This publishes `ProductUpdatedEvent`

**Step 4:** The `AffiliateProductIntelligenceProjectionConsumer` processes it

**Step 5:** MongoDB document is created automatically

**Step 6:** Refresh discover page

---

### Option 3: Run Diagnostic First

```powershell
.\AF-22-Diagnostic.ps1
```

This will:
- ✅ Check if backend is running
- ✅ Check if API endpoint works
- ✅ Show product count
- ✅ Suggest next steps

---

## What You Should See After Adding Data

### Discover Tab (`/affiliate/portal?tab=discover`)

**Product Cards:**
- Product image with hover effect
- Product name (2 lines max)
- Store name
- Price (or "From X" if variants)
- Commission badge (top right):
  - 🟡 Gold: > 25% commission
  - 🔵 Blue: 10-25% commission
  - ⚪ Gray: < 10% commission
- Stock badge (bottom left):
  - 🟢 Green: In stock
  - 🟠 Orange: Low stock
  - ⚪ Gray: Out of stock
- "You earn X" highlight
- "View Details" button (disabled if out of stock)

**Filters:**
- Search bar (debounced 400ms)
- "In Stock Only" toggle
- Sorting hint: "Sorted by: In-stock • Highest commission • Newest"

**Product Drawer (click any card):**
- Slides from right
- Image carousel (if multiple images)
- Price & commission breakdown
- Inventory status with stock count
- Variant list (if product has variants)
- Store info with logo
- Copy affiliate link button
- Share buttons (WhatsApp, Facebook)
- Visit Storefront button
- Press ESC to close

---

## Test the FE-1A Hardening

After adding data, test these features:

1. **ESC Key:** Open drawer → Press ESC → Drawer closes ✅
2. **Copy Link:** Click copy button → Checkmark appears ✅
3. **Broken Image:** Edit test data with bad image URL → Placeholder shows ✅
4. **Image Carousel:** Product with multiple images → Navigate with arrows ✅
5. **Search:** Type in search bar → Results filter after 400ms ✅
6. **Stock Filter:** Toggle "In Stock Only" → Out of stock products hidden ✅
7. **Pagination:** If > 20 products → Pagination buttons work ✅

---

## Troubleshooting

### Issue: Still seeing "No Products Found"

**Check 1:** MongoDB has data
```javascript
use PosEcommerceDb
db.affiliate_product_intelligence.countDocuments()
```

**Check 2:** API returns data
```bash
curl http://localhost:5000/api/affiliate/discover/products?pageSize=1
```

**Check 3:** Browser console for errors
- Open DevTools (F12)
- Check Console tab
- Look for API errors

**Check 4:** Network tab
- Open DevTools → Network tab
- Refresh page
- Look for `/api/affiliate/discover/products` request
- Check response

### Issue: API returns 401 Unauthorized

You need to be logged in as an affiliate:
1. Go to `http://localhost:5173/affiliate/login`
2. Login with affiliate credentials
3. Navigate to discover tab

### Issue: API returns 500 Internal Server Error

Check backend logs for:
- MongoDB connection errors
- Missing configuration
- Consumer registration errors

### Issue: Images not loading

The test data uses Unsplash placeholder images. If they don't load:
- Check internet connection
- Images will fallback to placeholder.png
- Replace with your own image URLs

---

## Next Steps

After verifying FE-1 works with test data:

1. **Implement Backfill:** Create .NET console app to populate all products
2. **Test Edge Cases:** Missing fields, broken images, variants
3. **Mobile Testing:** Test on mobile devices
4. **Performance:** Test with 100+ products
5. **Production Deploy:** Deploy to staging → test → production

---

## Files Reference

- `AF-22-Insert-Test-Data.js` - MongoDB test data script
- `AF-22-Diagnostic.ps1` - Diagnostic tool
- `AF-22-Backfill-ProductIntelligence.ps1` - Backfill template
- `FE-1_INTEGRATION_CHECKLIST.md` - Complete integration guide
- `FE-1A_HARDENING_SUMMARY.md` - Hardening details
- `AF-22_IMPLEMENTATION_SUMMARY.md` - Backend implementation

---

## Success Criteria ✅

- [ ] MongoDB has product documents
- [ ] API returns products
- [ ] Discover tab shows product grid
- [ ] Commission badges colored correctly
- [ ] Stock badges show correct status
- [ ] Search works
- [ ] Filters work
- [ ] Drawer opens on click
- [ ] ESC closes drawer
- [ ] Copy link works
- [ ] No console errors
- [ ] Mobile responsive

---

## Quick Commands

```bash
# Check MongoDB data
mongosh PosEcommerceDb --eval "db.affiliate_product_intelligence.countDocuments()"

# Insert test data
mongosh PosEcommerceDb < AF-22-Insert-Test-Data.js

# Run diagnostic
.\AF-22-Diagnostic.ps1

# Test API
curl http://localhost:5000/api/affiliate/discover/products?pageSize=5

# Restart dev server
cd PointOfSale.Web
npm run dev
```

---

## Status

- ✅ AF-22 Backend: COMPLETE
- ✅ FE-1 Frontend: COMPLETE
- ✅ FE-1A Hardening: COMPLETE
- ✅ Integration: COMPLETE
- ⏳ Data Population: PENDING (run test data script)
- ⏳ Testing: PENDING (after data population)

**Action Required:** Run `AF-22-Insert-Test-Data.js` to populate MongoDB!
