# AF-12: Affiliate Mode Navigation - Testing Guide

## 🎯 Purpose
Quick reference guide for testing the affiliate mode navigation fix.

## ⚡ Quick Start

### Prerequisites
1. Backend running with affiliate accounts configured
2. Frontend running (`npm run dev`)
3. At least one affiliate account with referral code (e.g., `AFFLIA9689`)
4. At least one product with `IsAffiliateEnabled = true`

### Test Affiliate Account
- **Referral Code**: `AFFLIA9689` (or your configured code)
- **Test URL**: `http://localhost:5173/products?ref=AFFLIA9689`

---

## 🧪 Test Scenarios

### ✅ Test 1: Initial Entry (2 min)
**Objective**: Verify affiliate mode activates on entry

**Steps**:
1. Open browser (incognito mode recommended)
2. Navigate to: `http://localhost:5173/products?ref=AFFLIA9689`
3. Wait for page load

**Expected Results**:
- ✅ Affiliate badge appears in header ("🎯 Affiliate Store")
- ✅ Only affiliate-enabled products visible
- ✅ URL contains `?ref=AFFLIA9689`
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 2: Header Navigation (3 min)
**Objective**: Verify ref preserved in header navigation

**Steps**:
1. Start at: `/products?ref=AFFLIA9689`
2. Click "Home" logo
3. Check URL
4. Click "Products" link
5. Check URL
6. Click "Cart" icon
7. Check URL

**Expected Results**:
- ✅ Home URL: `/?ref=AFFLIA9689`
- ✅ Products URL: `/products?ref=AFFLIA9689`
- ✅ Cart URL: `/cart?ref=AFFLIA9689`
- ✅ Affiliate badge visible on all pages
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 3: Product Card Navigation (2 min)
**Objective**: Verify ref preserved when clicking products

**Steps**:
1. Start at: `/products?ref=AFFLIA9689`
2. Click any product card
3. Check URL
4. Click browser back button
5. Click different product
6. Check URL

**Expected Results**:
- ✅ Product detail URL: `/products/{slug}?ref=AFFLIA9689`
- ✅ Affiliate badge visible on product page
- ✅ Back navigation preserves ref
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 4: Search Navigation (3 min)
**Objective**: Verify ref preserved in search

**Steps**:
1. Start at: `/products?ref=AFFLIA9689`
2. Open search modal (Cmd/Ctrl + K or click search icon)
3. Type: "anker"
4. Click a search result
5. Check URL

**Expected Results**:
- ✅ Search result URL includes `?ref=AFFLIA9689`
- ✅ Affiliate badge visible
- ✅ Search modal closes
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 5: Filter Navigation (3 min)
**Objective**: Verify ref preserved with filters

**Steps**:
1. Start at: `/products?ref=AFFLIA9689`
2. Apply category filter
3. Check URL
4. Apply price filter
5. Check URL
6. Apply brand filter
7. Check URL

**Expected Results**:
- ✅ Category URL: `/products?category=123&ref=AFFLIA9689`
- ✅ Price URL: `/products?category=123&minPrice=100&ref=AFFLIA9689`
- ✅ Brand URL: `/products?category=123&minPrice=100&brand=anker&ref=AFFLIA9689`
- ✅ Affiliate badge visible
- ✅ Only affiliate products shown
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 6: Mobile Navigation (3 min)
**Objective**: Verify ref preserved on mobile

**Steps**:
1. Resize browser to mobile width (< 768px)
2. Start at: `/products?ref=AFFLIA9689`
3. Open mobile menu (hamburger icon)
4. Click "Home"
5. Check URL
6. Open mobile menu again
7. Click "Products"
8. Check URL

**Expected Results**:
- ✅ Mobile menu opens correctly
- ✅ Home URL: `/?ref=AFFLIA9689`
- ✅ Products URL: `/products?ref=AFFLIA9689`
- ✅ Affiliate badge visible (mobile size)
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 7: Breadcrumb Navigation (2 min)
**Objective**: Verify ref preserved in breadcrumbs

**Steps**:
1. Start at: `/products?ref=AFFLIA9689`
2. Apply category filter
3. Click "Home" in breadcrumb
4. Check URL
5. Navigate back to products
6. Click "Products" in breadcrumb
7. Check URL

**Expected Results**:
- ✅ Home breadcrumb URL: `/?ref=AFFLIA9689`
- ✅ Products breadcrumb URL: `/products?ref=AFFLIA9689`
- ✅ Affiliate badge visible
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 8: Cart/Checkout Flow (4 min)
**Objective**: Verify ref preserved through checkout

**Steps**:
1. Start at: `/products?ref=AFFLIA9689`
2. Add product to cart
3. Click cart icon
4. Check URL
5. Click "Checkout"
6. Check URL
7. Click "Back to Cart" breadcrumb
8. Check URL

**Expected Results**:
- ✅ Cart URL: `/cart?ref=AFFLIA9689`
- ✅ Checkout URL: `/checkout?ref=AFFLIA9689`
- ✅ Back to cart URL: `/cart?ref=AFFLIA9689`
- ✅ Affiliate badge visible on all pages
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 9: Non-Affiliate Entry (2 min)
**Objective**: Verify normal behavior without ref

**Steps**:
1. Open new incognito window
2. Navigate to: `http://localhost:5173/products` (NO ref parameter)
3. Check page
4. Navigate to home
5. Navigate to cart
6. Check URLs

**Expected Results**:
- ✅ NO affiliate badge visible
- ✅ ALL products visible (not just affiliate)
- ✅ URLs do NOT contain `?ref=`
- ✅ Navigation works normally
- ✅ No console errors

**Pass/Fail**: ___________

---

### ✅ Test 10: External Links (1 min)
**Objective**: Verify external links don't get ref

**Steps**:
1. Start at: `/products?ref=AFFLIA9689`
2. Scroll to footer
3. Inspect social media links (if any)
4. Check href attributes

**Expected Results**:
- ✅ External links do NOT contain `?ref=`
- ✅ External links open in new tab (if configured)
- ✅ No console errors

**Pass/Fail**: ___________

---

## 🔍 Browser Testing Matrix

Test all scenarios in each browser:

| Browser | Version | Test 1 | Test 2 | Test 3 | Test 4 | Test 5 | Test 6 | Test 7 | Test 8 | Test 9 | Test 10 |
|---------|---------|--------|--------|--------|--------|--------|--------|--------|--------|--------|---------|
| Chrome  | Latest  | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ |
| Firefox | Latest  | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ |
| Safari  | Latest  | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ |
| Edge    | Latest  | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ |

---

## 📱 Mobile Testing Matrix

Test key scenarios on mobile devices:

| Device | OS | Test 1 | Test 2 | Test 3 | Test 6 | Test 8 |
|--------|-----|--------|--------|--------|--------|--------|
| iPhone | iOS | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ |
| Android | Android | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ |

---

## 🐛 Bug Report Template

If you find an issue, use this template:

```
**Test**: Test #X - [Test Name]
**Browser**: [Chrome/Firefox/Safari/Edge] [Version]
**Device**: [Desktop/Mobile] [OS]

**Steps to Reproduce**:
1. 
2. 
3. 

**Expected Result**:
- 

**Actual Result**:
- 

**URL When Issue Occurred**:
- 

**Console Errors** (if any):
```
[Paste console errors here]
```

**Screenshots** (if applicable):
[Attach screenshots]

**Severity**: [Critical/High/Medium/Low]
```

---

## ✅ Sign-Off Checklist

Before marking testing complete:

- [ ] All 10 test scenarios passed
- [ ] Tested in Chrome
- [ ] Tested in Firefox
- [ ] Tested in Safari
- [ ] Tested in Edge
- [ ] Tested on mobile (iOS)
- [ ] Tested on mobile (Android)
- [ ] No console errors found
- [ ] No visual regressions
- [ ] Performance acceptable (< 10ms overhead)
- [ ] Affiliate badge displays correctly
- [ ] Non-affiliate mode works correctly
- [ ] External links work correctly

---

## 📊 Test Results Summary

**Date**: ___________  
**Tester**: ___________  
**Environment**: ___________

**Overall Result**: PASS / FAIL

**Tests Passed**: ___ / 10  
**Browsers Tested**: ___ / 4  
**Mobile Devices Tested**: ___ / 2

**Critical Issues Found**: ___  
**High Priority Issues**: ___  
**Medium Priority Issues**: ___  
**Low Priority Issues**: ___

**Ready for Production**: YES / NO

**Notes**:
```
[Add any additional notes or observations here]
```

---

## 🚀 Next Steps After Testing

### If All Tests Pass
1. Update AF-12_COMPLETION_SUMMARY.md with test results
2. Create deployment ticket
3. Schedule production deployment
4. Prepare rollback plan
5. Monitor post-deployment

### If Tests Fail
1. Document all issues using bug report template
2. Prioritize issues (Critical → High → Medium → Low)
3. Fix critical and high priority issues
4. Re-test affected scenarios
5. Repeat until all tests pass

---

**Testing Guide Version**: 1.0  
**Last Updated**: 2026-02-22  
**Estimated Testing Time**: 30-45 minutes (all scenarios)
