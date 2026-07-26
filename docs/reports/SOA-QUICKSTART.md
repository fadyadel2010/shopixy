# SOA Module - Quick Start Guide

## 🚀 5-Minute Setup

### Prerequisites
- ✅ Backend API running (PointOfSale.API)
- ✅ Frontend running (PointOfSale.Web)
- ✅ Database migration applied
- ✅ At least one customer and supplier created

---

## Step 1: Access the SOA Module (30 seconds)

1. Open your browser to the frontend (usually http://localhost:5173)
2. Login with your credentials
3. Look for **"Accounts"** in the top navigation menu
4. You'll see 4 options:
   - 📄 Statement of Account
   - 💵 Payments
   - ⚠️ Outstanding Balances
   - ⚙️ Adjustments

---

## Step 2: View Outstanding Balances (1 minute)

1. Click **"Accounts"** → **"Outstanding Balances"**
2. You'll see two tabs:
   - **Customers** (money owed to you)
   - **Suppliers** (money you owe)
3. Each card shows:
   - Party name
   - Current balance
   - Balance type (Debit/Credit)
4. Click any card to view full statement
5. Click "Receive/Make Payment" for quick payment

**What you'll see:**
- Red cards = Debit (they owe you)
- Green cards = Credit (you owe them)
- Summary at top shows totals

---

## Step 3: Record a Payment (2 minutes)

### Receive Payment from Customer

1. Click **"Accounts"** → **"Payments"**
2. Ensure **"Receive Payment"** tab is active
3. Select a customer from dropdown
4. View their current balance
5. Click **"Receive Payment"** button
6. Fill in the form:
   - **Amount**: Enter payment amount
   - **Payment Date**: Select date (defaults to today)
   - **Payment Method**: Choose Cash, Card, Bank Transfer, or Mixed
   - **Notes**: Optional description
7. Click **"Receive Payment"**
8. ✅ Success! Balance updates immediately

### Make Payment to Supplier

1. Click **"Make Payment"** tab
2. Select a supplier
3. View their balance
4. Click **"Make Payment"** button
5. Fill in the form (same fields as above)
6. Click **"Make Payment"**
7. ✅ Done! Balance updates

---

## Step 4: View Statement of Account (1 minute)

1. Click **"Accounts"** → **"Statement of Account"**
2. Select **Party Type** (Customer or Supplier)
3. Select specific party from dropdown
4. Optionally set date range
5. Click **"Load Statement"**

**What you'll see:**
- **Summary Cards:**
  - Opening Balance
  - Total Debit (increases balance)
  - Total Credit (decreases balance)
  - Closing Balance with type
- **Transaction List:**
  - Date and time
  - Transaction type (Sale, Payment, etc.)
  - Document number
  - Debit/Credit amounts
  - Running balance
  - Notes

---

## Step 5: Create an Adjustment (1 minute)

1. Click **"Accounts"** → **"Adjustments"**
2. Select **Party Type** (Customer or Supplier)
3. Select party from dropdown
4. View current balance
5. Click **"Create Adjustment"**
6. Select adjustment type:
   - **Credit Note**: Reduces amount owed (e.g., discount)
   - **Debit Note**: Increases amount owed (e.g., extra charge)
7. Enter amount
8. **Add notes** (required - explain why)
9. Click **"Create Adjustment"**
10. ✅ Balance updates immediately

---

## Common Scenarios

### Scenario 1: Customer Owes You Money
**Situation:** Customer bought goods on credit

**What to do:**
1. Sale is automatically recorded when you create invoice in POS
2. Check balance in Outstanding Balances
3. When customer pays, record payment in Payments page
4. Balance reduces automatically

**Balance Display:**
- Shows as **Red (Debit)** = Customer owes you

### Scenario 2: You Owe Supplier Money
**Situation:** You purchased goods on credit

**What to do:**
1. Purchase is automatically recorded when you create purchase invoice
2. Check balance in Outstanding Balances
3. When you pay supplier, record payment in Payments page
4. Balance reduces automatically

**Balance Display:**
- Shows as **Green (Credit)** = You owe supplier

### Scenario 3: Give Customer a Discount
**Situation:** Customer deserves discount for damaged goods

**What to do:**
1. Go to Adjustments page
2. Select Customer and the specific customer
3. Create **Credit Note**
4. Enter discount amount
5. Add notes: "Discount for damaged goods"
6. Submit

**Result:**
- Customer's balance reduces by discount amount

### Scenario 4: Charge Customer Extra
**Situation:** Customer needs to pay additional delivery fee

**What to do:**
1. Go to Adjustments page
2. Select Customer and the specific customer
3. Create **Debit Note**
4. Enter additional amount
5. Add notes: "Additional delivery charges"
6. Submit

**Result:**
- Customer's balance increases by additional amount

---

## Understanding Balance Types

### For Customers (Accounts Receivable)
| Balance Type | Meaning | Color |
|--------------|---------|-------|
| **Debit** | Customer owes you money | 🔴 Red |
| **Credit** | You owe customer money (rare) | 🟢 Green |

### For Suppliers (Accounts Payable)
| Balance Type | Meaning | Color |
|--------------|---------|-------|
| **Credit** | You owe supplier money | 🟢 Green |
| **Debit** | Supplier owes you money (rare) | 🔴 Red |

---

## Quick Reference

### Transaction Types You'll See

| Type | What It Means | Debit/Credit |
|------|---------------|--------------|
| **Sale** | Customer bought on credit | Debit (increases what they owe) |
| **Sales Return** | Customer returned goods | Credit (reduces what they owe) |
| **Purchase** | You bought on credit | Credit (increases what you owe) |
| **Payment Received** | Customer paid you | Credit (reduces what they owe) |
| **Payment Made** | You paid supplier | Debit (reduces what you owe) |
| **Credit Note** | Discount/adjustment in favor of party | Credit |
| **Debit Note** | Additional charge to party | Debit |

---

## Tips for Success

### ✅ Do's
- ✅ Always add notes when creating adjustments
- ✅ Check balance before recording payment
- ✅ Use date filters to view specific periods
- ✅ Regularly review outstanding balances
- ✅ Record payments promptly

### ❌ Don'ts
- ❌ Don't create adjustments without notes
- ❌ Don't forget to verify payment amounts
- ❌ Don't ignore outstanding balances
- ❌ Don't manually edit balances (use adjustments)

---

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Navigate to Accounts | Click "Accounts" in menu |
| Search parties | Type in search box |
| Submit form | Enter key |
| Close modal | Escape key |

---

## Mobile Usage

All pages are mobile-friendly:
- ✅ Responsive layouts
- ✅ Touch-friendly buttons
- ✅ Horizontal scrolling tables
- ✅ Easy navigation

---

## Troubleshooting

### Balance Not Showing
**Solution:** Refresh the page or click "Load Statement" again

### Payment Not Recording
**Solution:** 
1. Check all required fields are filled
2. Verify amount is positive number
3. Ensure payment method is selected

### Statement Empty
**Solution:**
1. Verify party is selected
2. Check date range isn't too narrow
3. Ensure party has transactions

### Can't Find Party
**Solution:**
1. Use search box
2. Check correct party type is selected
3. Verify party exists in system

---

## Next Steps

### After Quick Start
1. ✅ Set opening balances for existing parties (if migrating)
2. ✅ Train team on payment recording
3. ✅ Set up regular balance review schedule
4. ✅ Create process for handling adjustments
5. ✅ Monitor outstanding balances weekly

### Learn More
- **Full Backend Guide:** `SOA-MODULE-GUIDE.md`
- **Frontend Guide:** `SOA-FRONTEND-GUIDE.md`
- **Testing Guide:** `SOA-TESTING-GUIDE.md`
- **Complete Implementation:** `SOA-COMPLETE-IMPLEMENTATION.md`

---

## Support

### Need Help?
1. Check the documentation files
2. Review the testing guide for examples
3. Check browser console for errors
4. Verify API is running

### Common Questions

**Q: How do I set opening balances?**
A: Use the API endpoint `/api/StatementOfAccount/opening-balance` (UI coming soon)

**Q: Can I delete a transaction?**
A: No, for audit purposes. Create an adjustment to correct it.

**Q: How do I export statements?**
A: PDF export feature coming soon. Currently view on screen.

**Q: Can I record partial payments?**
A: Yes! Just enter the partial amount when recording payment.

**Q: What if I make a mistake?**
A: Create a reversing adjustment with notes explaining the correction.

---

## Success! 🎉

You're now ready to use the SOA module! 

**Remember:**
- Red = Money owed to you
- Green = Money you owe
- Always add notes to adjustments
- Check outstanding balances regularly

**Happy accounting!** 📊💰
