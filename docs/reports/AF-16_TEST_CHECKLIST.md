# AF-16: Affiliate Payout Wallet — Test Checklist

## 🔧 Pre-Deployment

- [ ] Run database migration: `.\add-affiliate-payout-wallet.ps1`
- [ ] Verify columns added: `SELECT * FROM "AffiliateAccounts" LIMIT 1;`
- [ ] Verify index created: `\d "AffiliateAccounts"`
- [ ] Build backend: `dotnet build`
- [ ] Build frontend: `npm run build`
- [ ] Check for compilation errors

---

## 🧪 Backend API Tests

### Affiliate Portal Endpoint

**PUT /api/affiliate/profile/payout-wallet**

- [ ] **Valid Request**
  ```bash
  curl -X PUT http://localhost:5000/api/affiliate/profile/payout-wallet \
    -H "Authorization: Bearer {affiliate_jwt}" \
    -H "Content-Type: application/json" \
    -d '{"walletNumber":"01012345678","walletType":"VodafoneCash"}'
  ```
  Expected: `200 OK`, `{ "message": "Payout wallet updated successfully" }`

- [ ] **Missing Wallet Number**
  ```json
  {"walletNumber":"","walletType":"VodafoneCash"}
  ```
  Expected: `400 Bad Request`, error message

- [ ] **Too Short (5 chars)**
  ```json
  {"walletNumber":"12345","walletType":"VodafoneCash"}
  ```
  Expected: `400 Bad Request`, "must be 6-32 characters"

- [ ] **Too Long (33 chars)**
  ```json
  {"walletNumber":"123456789012345678901234567890123","walletType":"VodafoneCash"}
  ```
  Expected: `400 Bad Request`, "must be 6-32 characters"

- [ ] **Special Characters**
  ```json
  {"walletNumber":"0101234-5678","walletType":"VodafoneCash"}
  ```
  Expected: `400 Bad Request`, "only letters and digits"

- [ ] **Unauthorized (No JWT)**
  Expected: `401 Unauthorized`

- [ ] **Wrong Affiliate (JWT for Affiliate B, trying to update Affiliate A)**
  Expected: `401 Unauthorized` or `404 Not Found`

### Admin Endpoint

**GET /api/admin/affiliates/{id}**

- [ ] **Admin Can Read Wallet**
  ```bash
  curl http://localhost:5000/api/admin/affiliates/{id} \
    -H "Authorization: Bearer {admin_jwt}"
  ```
  Expected: Response includes `payoutWalletNumber`, `payoutWalletType`, `payoutWalletUpdatedAtUtc`

- [ ] **Wallet Number Unmasked**
  Expected: Full wallet number visible (e.g., `01012345678`, not `010*****678`)

- [ ] **No Wallet Configured**
  Expected: `payoutWalletNumber: null`, `payoutWalletType: null`

---

## 🎨 Frontend Tests

### Affiliate Portal - Profile Tab

#### Initial State (No Wallet)
- [ ] Navigate to Profile tab
- [ ] Empty state shown: "No payout wallet configured"
- [ ] "Add Wallet" button visible

#### Add Wallet Flow
- [ ] Click "Add Wallet" or edit button
- [ ] Form appears with:
  - [ ] Wallet Type dropdown (default: Vodafone Cash)
  - [ ] Wallet Number input (placeholder visible)
  - [ ] Save/Cancel buttons
  - [ ] Security notice
- [ ] Select wallet type: "Orange Money"
- [ ] Enter wallet number: "01012345678"
- [ ] Click "Save Wallet"
- [ ] Success message appears (green)
- [ ] Form closes
- [ ] Wallet displayed (masked): `010*****678`
- [ ] Wallet type shown: "Orange Money"
- [ ] Last updated timestamp shown

#### Edit Wallet Flow
- [ ] Click "Edit Wallet" button
- [ ] Form pre-filled with current values
- [ ] Change wallet type to "Bank"
- [ ] Change wallet number to "11122233344"
- [ ] Click "Save Wallet"
- [ ] Success message appears
- [ ] Updated wallet displayed (masked): `111*****344`

#### Cancel Edit
- [ ] Click "Edit Wallet"
- [ ] Change values
- [ ] Click "Cancel"
- [ ] Form closes
- [ ] Original values restored (no changes saved)

#### Validation Errors
- [ ] Click "Edit Wallet"
- [ ] Clear wallet number
- [ ] Click "Save Wallet"
- [ ] Error message: "Wallet number is required"
- [ ] Enter "12345" (5 chars)
- [ ] Click "Save Wallet"
- [ ] Error message: "must be 6-32 characters"

### Admin Portal - Affiliate Detail

#### Affiliate With Wallet
- [ ] Navigate to Admin → Affiliates
- [ ] Click on affiliate with wallet
- [ ] Payout wallet card visible
- [ ] Wallet type displayed
- [ ] Wallet number UNMASKED (full number visible)
- [ ] Last updated timestamp shown
- [ ] Card has gradient styling (indigo/purple)

#### Affiliate Without Wallet
- [ ] Navigate to affiliate without wallet
- [ ] Payout wallet card NOT shown (or shows "Not configured")

---

## 🔐 Security Tests

### Authorization
- [ ] Affiliate A cannot update Affiliate B's wallet
- [ ] Unauthenticated request returns 401
- [ ] Admin can READ wallet but has no UPDATE endpoint
- [ ] Wallet never appears in JWT claims (decode JWT and verify)

### Audit Trail
- [ ] Update wallet via API
- [ ] Query `AuditLogs` table:
  ```sql
  SELECT * FROM "AuditLogs" 
  WHERE "ActionType" = 'AffiliateWalletUpdated' 
  ORDER BY "TimestampUtc" DESC LIMIT 1;
  ```
- [ ] Verify entry exists
- [ ] Verify `BeforeDataJson` is masked (e.g., `010*****678`)
- [ ] Verify `AfterDataJson` is masked
- [ ] Verify `UserId` is affiliate account ID
- [ ] Verify `EntityId` is affiliate account ID
- [ ] Verify `SuccessFlag` is true

### Data Privacy
- [ ] Check application logs (not database)
- [ ] Verify wallet numbers are masked in logs
- [ ] Verify no plain-text wallet numbers in logs
- [ ] Check browser DevTools → Network tab
- [ ] Verify wallet number sent in request body (expected)
- [ ] Verify wallet number in response is masked (affiliate view)

---

## 🗄️ Database Tests

### Schema Verification
```sql
-- Verify columns exist
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'AffiliateAccounts' 
AND column_name IN ('PayoutWalletNumber', 'PayoutWalletType', 'PayoutWalletUpdatedAtUtc');
```
- [ ] All 3 columns exist
- [ ] All are nullable
- [ ] Correct data types (TEXT, TEXT, TIMESTAMPTZ)

### Index Verification
```sql
-- Verify index exists
SELECT indexname, indexdef 
FROM pg_indexes 
WHERE tablename = 'AffiliateAccounts' 
AND indexname = 'IX_AffiliateAccounts_PayoutWallet';
```
- [ ] Index exists
- [ ] Includes TenantId and PayoutWalletNumber

### Data Integrity
```sql
-- Insert test data
UPDATE "AffiliateAccounts" 
SET "PayoutWalletNumber" = '01012345678',
    "PayoutWalletType" = 'VodafoneCash',
    "PayoutWalletUpdatedAtUtc" = NOW()
WHERE "Id" = '{test_affiliate_id}';

-- Verify update
SELECT "PayoutWalletNumber", "PayoutWalletType", "PayoutWalletUpdatedAtUtc"
FROM "AffiliateAccounts"
WHERE "Id" = '{test_affiliate_id}';
```
- [ ] Data persists correctly
- [ ] Timestamp is UTC
- [ ] No data corruption

---

## 🔄 Integration Tests

### Existing Functionality (No Breaking Changes)
- [ ] Affiliate registration still works
- [ ] Affiliate login still works
- [ ] Dashboard loads correctly
- [ ] Orders tab loads correctly
- [ ] Commission recording still works
- [ ] Admin affiliate list still works
- [ ] Admin affiliate detail still works (without wallet)
- [ ] Approve/Pay actions still work

### New Functionality Integration
- [ ] Profile endpoint returns wallet fields
- [ ] Admin detail endpoint returns wallet fields
- [ ] Wallet update doesn't affect commission ledger
- [ ] Wallet update doesn't affect accounting bridge
- [ ] Wallet update doesn't affect attribution

---

## 📱 UI/UX Tests

### Responsive Design
- [ ] Desktop (1920x1080): Layout correct
- [ ] Tablet (768x1024): Layout correct
- [ ] Mobile (375x667): Layout correct
- [ ] Form inputs accessible on mobile

### Accessibility
- [ ] Form labels present
- [ ] Error messages announced
- [ ] Keyboard navigation works
- [ ] Focus indicators visible
- [ ] Color contrast sufficient

### User Experience
- [ ] Loading states shown during save
- [ ] Success feedback clear
- [ ] Error messages helpful
- [ ] Cancel action works as expected
- [ ] No data loss on cancel

---

## 🚀 Performance Tests

### API Response Times
- [ ] GET /api/affiliate/profile < 200ms
- [ ] PUT /api/affiliate/profile/payout-wallet < 500ms
- [ ] GET /api/admin/affiliates/{id} < 300ms

### Database Queries
- [ ] Index used for wallet queries (check EXPLAIN)
- [ ] No N+1 queries
- [ ] Audit log insert doesn't block response

---

## ✅ Final Verification

- [ ] All backend tests pass
- [ ] All frontend tests pass
- [ ] All security tests pass
- [ ] All database tests pass
- [ ] All integration tests pass
- [ ] No console errors in browser
- [ ] No exceptions in server logs
- [ ] Documentation updated
- [ ] Code reviewed
- [ ] Ready for production deployment

---

## 🐛 Known Issues / Edge Cases

- [ ] What happens if affiliate updates wallet during payout processing?
  - Expected: Last write wins, no data corruption
- [ ] What happens if admin views affiliate detail while wallet is being updated?
  - Expected: Eventual consistency, no errors
- [ ] What happens if wallet number contains Unicode characters?
  - Expected: Validation rejects (alphanumeric only)
- [ ] What happens if two affiliates share the same wallet number?
  - Expected: Allowed (no uniqueness constraint)

---

**Test Status:** ⏳ PENDING  
**Tested By:** _____________  
**Date:** _____________  
**Sign-off:** _____________
