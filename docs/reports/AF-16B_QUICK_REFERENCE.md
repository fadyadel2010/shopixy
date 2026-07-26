# AF-16B Quick Reference Guide

## 🚀 Quick Start

### For Developers

**Using PayoutWallet in Code:**
```csharp
// Create and validate wallet
var wallet = PayoutWallet.Create("+201012345678", PayoutWalletTypes.VodafoneCash);
// Result: wallet.Number = "01012345678" (normalized)

// Mask for display
var masked = wallet.GetMasked();
// Result: "010*****678"

// Update affiliate wallet
affiliate.UpdatePayoutWallet("+201012345678", PayoutWalletTypes.VodafoneCash);
// Automatically normalized and validated
```

### For QA/Testers

**Test Cases:**
```
✅ Valid: +201012345678 (VodafoneCash) → 01012345678
✅ Valid: 010-1234-5678 (VodafoneCash) → 01012345678
✅ Valid: IBAN123456 (Bank) → IBAN123456
❌ Invalid: 0101234567 (VodafoneCash) → "must be 11 digits"
❌ Invalid: ABC-123 (Bank) → "only letters and digits"
```

### For DBAs

**Run Normalization (ONCE in staging, then production):**
```sql
-- Backup first!
CREATE TABLE "AffiliateAccounts_Backup_AF16B" AS 
SELECT * FROM "AffiliateAccounts" WHERE "PayoutWalletNumber" IS NOT NULL;

-- Run normalization
\i normalize_existing_payout_wallets.sql

-- Verify results
SELECT "PayoutWalletType", COUNT(*) 
FROM "AffiliateAccounts" 
WHERE "PayoutWalletNumber" IS NOT NULL 
GROUP BY "PayoutWalletType";
```

---

## 📋 Wallet Type Rules

| Type | Format | Length | Example |
|------|--------|--------|---------|
| VodafoneCash | Digits | 11 | 01012345678 |
| OrangeMoney | Digits | 11 | 01012345678 |
| EtisalatCash | Digits | 11 | 01012345678 |
| InstaPay | Digits | 11 | 01012345678 |
| Bank | Alphanumeric | 6-32 | IBAN1234567890 |
| Other | Alphanumeric | 6-32 | WALLET123 |

---

## 🔧 Common Tasks

### Add New Wallet Type
```csharp
// 1. Add constant to PayoutWalletTypes.cs
public const string NewWallet = "NewWallet";

// 2. If mobile wallet, add to MobileWalletTypes set
MobileWalletTypes.Add(NewWallet);

// 3. Update frontend dropdown (if needed)
```

### Change Validation Rules
```csharp
// Edit PayoutWallet.cs → ValidateWalletNumber()
// All validation is centralized here
```

### Add Encryption (AF-17)
```csharp
// 1. Implement IPayoutWalletProtector
public class AesPayoutWalletProtector : IPayoutWalletProtector
{
    public string Protect(string plainText) { /* encrypt */ }
    public string Unprotect(string cipherText) { /* decrypt */ }
}

// 2. Register in DI
services.AddSingleton<IPayoutWalletProtector, AesPayoutWalletProtector>();

// 3. Use in repository/service before save/after load
```

---

## 🐛 Troubleshooting

### "Mobile wallet must be 11 digits"
**Cause:** Input doesn't normalize to 11 digits  
**Fix:** Check input format, ensure it's a valid Egyptian mobile number

### "Wallet number must contain only letters and digits"
**Cause:** Special characters in bank account  
**Fix:** Remove dashes, spaces, special characters

### "Wallet number must be 6-32 characters"
**Cause:** Bank account too short or too long  
**Fix:** Ensure length is between 6 and 32 characters

---

## 📞 Support

**Documentation:** `AF-16B_PAYOUT_WALLET_HARDENING_COMPLETE.md`  
**Tests:** `PointOfSale.Tests/Core/Entities/Affiliate/PayoutWalletTests.cs`  
**Migration:** `normalize_existing_payout_wallets.sql`
