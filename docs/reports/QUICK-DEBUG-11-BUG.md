# Quick Debug Steps for 11-Piece Bug

## 🔍 Immediate Debugging Steps

### Step 1: Check Browser Network Request
1. Open browser DevTools (F12)
2. Go to Network tab
3. Create a Purchase Return for 1 Carton
4. Find the POST request to `/api/purchasereturns`
5. Click on it and check the **Payload** tab

**Look for:**
```json
{
    "items": [{
        "quantity": ???  // ← What number is here? Should be 1
    }]
}
```

**If it shows `"quantity": 11`** → Frontend bug  
**If it shows `"quantity": 1`** → Backend bug

---

### Step 2: Check Console Logs
Add this to `PurchaseReturns.jsx` in the `handleSubmit` function, right before the API call:

```javascript
console.log('=== PURCHASE RETURN DEBUG ===');
console.log('Return Items:', returnItems);
console.log('Payload:', payload);
console.log('============================');
```

This will show exactly what's being sent.

---

### Step 3: Check Backend Logs
Look at the API console output when you create the return. It should show the received data.

---

## 🎯 Most Likely Causes

### Cause 1: Conversion Factor is 11 (not 10)
**Check:** Go to Products → Edit your product → Check the Carton unit
- Is the conversion factor set to 11 instead of 10?

### Cause 2: Quantity Input Shows 11
**Check:** When you create the return, does the quantity field show 1 or 11?

### Cause 3: Double Deduction
**Check:** Are there TWO stock movements created for the same return?

---

## 🔧 Quick Fix Test

Try this:
1. Create a NEW product with a fresh Carton unit (conversion: 10)
2. Set initial stock to 0
3. Purchase 1 Carton → Should become 10 pieces
4. Return 1 Carton → Should become 0 pieces

If this works correctly, the issue might be with your specific product's unit configuration.

---

## 📊 What to Share

Please share:
1. **Network request payload** (from DevTools)
2. **Product unit configuration** (conversion factor for Carton)
3. **Stock movement history** (from database or UI)

This will help me pinpoint the exact issue! 🎯
