# SOA Module Testing Guide

## Quick Test Scenarios

### Prerequisites
1. Ensure the API is running
2. Have a valid JWT token
3. Have at least one customer and one supplier created
4. Have at least one warehouse created

### Test Scenario 1: Customer Account Flow

#### Step 1: Create Opening Balance
```http
POST /api/StatementOfAccount/opening-balance
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "partyType": "Customer",
  "partyId": "YOUR_CUSTOMER_GUID",
  "amount": 1000.00,
  "isDebit": true,
  "effectiveDate": "2024-01-01T00:00:00Z",
  "notes": "Opening balance"
}
```

**Expected Result**: Customer balance should be 1000.00 (Debit)

#### Step 2: Create a Sale on Credit
```http
POST /api/Sales
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "customerId": "YOUR_CUSTOMER_GUID",
  "warehouseId": "YOUR_WAREHOUSE_GUID",
  "items": [
    {
      "productId": "YOUR_PRODUCT_GUID",
      "quantity": 2,
      "unitPrice": 500.00,
      "discountAmount": 0,
      "taxRate": 0
    }
  ],
  "discountPercentage": 0,
  "discountAmount": 0,
  "paymentMethod": "Cash",
  "paidAmount": 0
}
```

**Expected Result**: 
- Customer balance increases to 2000.00 (1000 + 1000)
- Ledger shows Sale transaction with 1000 debit

#### Step 3: Receive Payment
```http
POST /api/Payments/receive
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "customerId": "YOUR_CUSTOMER_GUID",
  "amount": 500.00,
  "paymentDate": "2024-01-15T10:00:00Z",
  "paymentMethod": "Cash",
  "notes": "Partial payment"
}
```

**Expected Result**: 
- Customer balance reduces to 1500.00
- Ledger shows PaymentReceived with 500 credit

#### Step 4: Issue Credit Note
```http
POST /api/StatementOfAccount/adjustment
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "partyType": "Customer",
  "partyId": "YOUR_CUSTOMER_GUID",
  "adjustmentType": "CreditNote",
  "amount": 100.00,
  "notes": "Discount for bulk purchase"
}
```

**Expected Result**: 
- Customer balance reduces to 1400.00
- Ledger shows CreditNote with 100 credit

#### Step 5: View Statement
```http
POST /api/StatementOfAccount/query
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "partyType": "Customer",
  "partyId": "YOUR_CUSTOMER_GUID",
  "page": 1,
  "pageSize": 50
}
```

**Expected Result**: 
- Summary shows:
  - Opening Balance: 1000.00
  - Total Debit: 1000.00 (from sale)
  - Total Credit: 600.00 (500 payment + 100 credit note)
  - Closing Balance: 1400.00 (Debit)
- Transactions list shows all 4 entries

### Test Scenario 2: Supplier Account Flow

#### Step 1: Create Opening Balance
```http
POST /api/StatementOfAccount/opening-balance
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "partyType": "Supplier",
  "partyId": "YOUR_SUPPLIER_GUID",
  "amount": 2000.00,
  "isDebit": false,
  "effectiveDate": "2024-01-01T00:00:00Z",
  "notes": "Opening balance - we owe supplier"
}
```

**Expected Result**: Supplier balance should be 2000.00 (Credit - we owe them)

#### Step 2: Create Purchase on Credit
```http
POST /api/Purchases
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "supplierId": "YOUR_SUPPLIER_GUID",
  "warehouseId": "YOUR_WAREHOUSE_GUID",
  "invoiceDate": "2024-01-10T00:00:00Z",
  "items": [
    {
      "productId": "YOUR_PRODUCT_GUID",
      "quantity": 10,
      "unitCost": 100.00,
      "taxRate": 0
    }
  ],
  "paymentMethod": "Cash",
  "paidAmount": 0
}
```

**Expected Result**: 
- Supplier balance increases to 3000.00 (we owe more)
- Ledger shows Purchase with 1000 credit

#### Step 3: Make Payment
```http
POST /api/Payments/make
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "supplierId": "YOUR_SUPPLIER_GUID",
  "amount": 1500.00,
  "paymentDate": "2024-01-15T10:00:00Z",
  "paymentMethod": "BankTransfer",
  "notes": "Partial payment"
}
```

**Expected Result**: 
- Supplier balance reduces to 1500.00
- Ledger shows PaymentMade with 1500 debit

#### Step 4: View Statement
```http
POST /api/StatementOfAccount/query
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "partyType": "Supplier",
  "partyId": "YOUR_SUPPLIER_GUID",
  "page": 1,
  "pageSize": 50
}
```

**Expected Result**: 
- Summary shows:
  - Opening Balance: 2000.00
  - Total Debit: 1500.00 (payment made)
  - Total Credit: 1000.00 (purchase)
  - Closing Balance: 1500.00 (Credit - we still owe)

### Test Scenario 3: Outstanding Balances

#### Get All Outstanding Customer Balances
```http
GET /api/StatementOfAccount/outstanding/Customer
Authorization: Bearer YOUR_TOKEN
```

**Expected Result**: List of all customers with non-zero balances

#### Get All Outstanding Supplier Balances
```http
GET /api/StatementOfAccount/outstanding/Supplier
Authorization: Bearer YOUR_TOKEN
```

**Expected Result**: List of all suppliers with non-zero balances

### Test Scenario 4: Date Range Filtering

```http
POST /api/StatementOfAccount/query
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "partyType": "Customer",
  "partyId": "YOUR_CUSTOMER_GUID",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-01-31T23:59:59Z",
  "page": 1,
  "pageSize": 50
}
```

**Expected Result**: Only transactions within January 2024

### Test Scenario 5: Sales Return Impact

#### Create a Sale
```http
POST /api/Sales
(Same as Test Scenario 1, Step 2)
```

#### Process Return
```http
POST /api/Sales/{invoiceId}/return
Content-Type: application/json
Authorization: Bearer YOUR_TOKEN

{
  "items": [
    {
      "productId": "YOUR_PRODUCT_GUID",
      "quantity": 1,
      "unitPrice": 500.00
    }
  ],
  "reason": "Defective product"
}
```

**Expected Result**: 
- Customer balance reduces by 500
- Ledger shows SalesReturn with 500 credit

## Validation Tests

### Test 1: Duplicate Opening Balance
Try creating opening balance twice for the same party.

**Expected Result**: Error message "Opening balance already exists for this party"

### Test 2: Invalid Party
Try creating transaction with non-existent customer/supplier ID.

**Expected Result**: Error message "Customer not found" or "Supplier not found"

### Test 3: Invalid Adjustment Type
Try creating adjustment with type "Sale" instead of valid adjustment types.

**Expected Result**: Error message "Invalid adjustment type"

## Balance Verification

After each transaction, verify:

1. **Party Balance** (Customer/Supplier entity)
2. **Last Ledger Entry Running Balance**
3. **Both should match**

Query to verify:
```http
GET /api/StatementOfAccount/balance/{partyType}/{partyId}
```

## Common Issues and Solutions

### Issue 1: Balance Not Updating
**Cause**: Transaction not committed
**Solution**: Check transaction logs, ensure SaveChangesAsync is called

### Issue 2: Incorrect Balance Type
**Cause**: Wrong debit/credit logic
**Solution**: Review party type and transaction type combination

### Issue 3: Missing Ledger Entries
**Cause**: LedgerService not called in transaction
**Solution**: Verify all financial controllers use LedgerService

## Performance Testing

### Test Large Dataset
1. Create 1000+ transactions for a single customer
2. Query statement with pagination
3. Verify response time < 2 seconds

### Test Multiple Parties
1. Create 100+ customers with transactions
2. Query outstanding balances
3. Verify response time < 3 seconds

## Integration Testing Checklist

- [ ] Sale creates ledger entry
- [ ] Sale return creates ledger entry
- [ ] Purchase creates ledger entry
- [ ] Payment received creates ledger entry
- [ ] Payment made creates ledger entry
- [ ] Opening balance creates ledger entry
- [ ] Adjustments create ledger entries
- [ ] Cashbox updates on cash payments
- [ ] Balance calculations are correct
- [ ] Date filtering works correctly
- [ ] Pagination works correctly
- [ ] Outstanding balances list is accurate

## Automated Test Script (PowerShell)

```powershell
# Set your API base URL and token
$baseUrl = "https://localhost:7001"
$token = "YOUR_JWT_TOKEN"
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# Test 1: Get outstanding customers
Write-Host "Testing outstanding customers..."
$response = Invoke-RestMethod -Uri "$baseUrl/api/StatementOfAccount/outstanding/Customer" -Headers $headers -Method Get
Write-Host "Found $($response.Count) customers with outstanding balances"

# Test 2: Get customer balance
$customerId = "YOUR_CUSTOMER_GUID"
Write-Host "Testing customer balance..."
$response = Invoke-RestMethod -Uri "$baseUrl/api/StatementOfAccount/balance/Customer/$customerId" -Headers $headers -Method Get
Write-Host "Customer balance: $($response.balance) ($($response.balanceType))"

# Test 3: Get statement
Write-Host "Testing statement query..."
$body = @{
    partyType = "Customer"
    partyId = $customerId
    page = 1
    pageSize = 50
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "$baseUrl/api/StatementOfAccount/query" -Headers $headers -Method Post -Body $body
Write-Host "Found $($response.totalCount) transactions"
Write-Host "Closing balance: $($response.summary.closingBalance) ($($response.summary.balanceType))"
```

## Success Criteria

All tests pass when:
1. ✅ Balances calculate correctly
2. ✅ All transaction types create ledger entries
3. ✅ Running balances are sequential and accurate
4. ✅ Date filtering works as expected
5. ✅ Pagination returns correct results
6. ✅ Balance types (Debit/Credit) display correctly
7. ✅ Cashbox integrates properly
8. ✅ No duplicate opening balances allowed
9. ✅ All validations work correctly
10. ✅ Performance meets requirements
