# Statement of Account (SOA) Module - Complete Guide

## Overview

The Statement of Account (SOA) module provides comprehensive financial tracking for both **Customers** and **Suppliers**. It automatically records all financial movements and maintains a complete ledger with running balances.

## Features

### 1. Unified Ledger System
- **AccountLedger** entity tracks all financial transactions
- Supports both Customer and Supplier accounts
- Maintains running balance for each transaction
- Automatic integration with all financial modules

### 2. Transaction Types Supported

#### Customer Transactions (Receivables)
- **Sale**: When a sale is made on credit
- **SalesReturn**: When goods are returned
- **PaymentReceived**: When customer makes a payment
- **CreditNote**: Credit adjustment in favor of customer
- **DebitNote**: Debit adjustment against customer
- **OpeningBalance**: Initial balance setup

#### Supplier Transactions (Payables)
- **Purchase**: When goods are purchased on credit
- **PurchaseReturn**: When goods are returned to supplier
- **PaymentMade**: When payment is made to supplier
- **CreditNote**: Credit adjustment from supplier
- **DebitNote**: Debit adjustment to supplier
- **OpeningBalance**: Initial balance setup

### 3. Balance Calculation Logic

#### For Customers (Accounts Receivable)
- **Debit** = Increases what customer owes us (Sales, Debit Notes)
- **Credit** = Decreases what customer owes us (Payments, Returns, Credit Notes)
- **Positive Balance** = Customer owes us (Debit Balance)
- **Negative Balance** = We owe customer (Credit Balance)

#### For Suppliers (Accounts Payable)
- **Debit** = Decreases what we owe supplier (Payments, Debit Notes)
- **Credit** = Increases what we owe supplier (Purchases, Credit Notes)
- **Positive Balance** = We owe supplier (Credit Balance)
- **Negative Balance** = Supplier owes us (Debit Balance)

## API Endpoints

### 1. Get Statement of Account
**POST** `/api/StatementOfAccount/query`

Retrieve filtered statement of account with summary.

**Request Body:**
```json
{
  "partyType": "Customer",  // or "Supplier"
  "partyId": "guid-here",   // Optional: filter by specific party
  "startDate": "2024-01-01T00:00:00Z",  // Optional
  "endDate": "2024-12-31T23:59:59Z",    // Optional
  "page": 1,
  "pageSize": 50
}
```

**Response:**
```json
{
  "partyType": "Customer",
  "partyId": "guid-here",
  "partyName": "John Doe",
  "summary": {
    "openingBalance": 1000.00,
    "totalDebit": 5000.00,
    "totalCredit": 3000.00,
    "closingBalance": 3000.00,
    "balanceType": "Debit"  // or "Credit"
  },
  "transactions": [
    {
      "id": "guid",
      "transactionDate": "2024-01-15T10:30:00Z",
      "transactionType": "Sale",
      "documentNumber": "INV-20240115-0001",
      "debitAmount": 1500.00,
      "creditAmount": 0,
      "runningBalance": 2500.00,
      "notes": "Sale Invoice INV-20240115-0001",
      "userName": "admin"
    }
  ],
  "totalCount": 25,
  "page": 1,
  "pageSize": 50,
  "totalPages": 1
}
```

### 2. Get Party Balance
**GET** `/api/StatementOfAccount/balance/{partyType}/{partyId}`

Get current balance for a specific customer or supplier.

**Example:** `/api/StatementOfAccount/balance/Customer/guid-here`

**Response:**
```json
{
  "partyId": "guid-here",
  "partyName": "John Doe",
  "partyType": "Customer",
  "balance": 3000.00,
  "balanceType": "Debit"
}
```

### 3. Create Opening Balance
**POST** `/api/StatementOfAccount/opening-balance`

Set initial balance for a customer or supplier.

**Request Body:**
```json
{
  "partyType": "Customer",
  "partyId": "guid-here",
  "amount": 5000.00,
  "isDebit": true,  // true = they owe us, false = we owe them
  "effectiveDate": "2024-01-01T00:00:00Z",
  "notes": "Opening balance as of Jan 1, 2024"
}
```

### 4. Create Adjustment
**POST** `/api/StatementOfAccount/adjustment`

Create credit/debit notes or adjustments.

**Request Body:**
```json
{
  "partyType": "Customer",
  "partyId": "guid-here",
  "adjustmentType": "CreditNote",  // CreditNote, DebitNote, CreditAdjustment, DebitAdjustment
  "amount": 500.00,
  "notes": "Discount for bulk purchase"
}
```

### 5. Get Outstanding Balances
**GET** `/api/StatementOfAccount/outstanding/{partyType}`

Get all customers or suppliers with non-zero balances.

**Example:** `/api/StatementOfAccount/outstanding/Customer`

**Response:**
```json
[
  {
    "partyId": "guid-1",
    "partyName": "John Doe",
    "partyType": "Customer",
    "balance": 3000.00,
    "balanceType": "Debit"
  },
  {
    "partyId": "guid-2",
    "partyName": "Jane Smith",
    "partyType": "Customer",
    "balance": 1500.00,
    "balanceType": "Credit"
  }
]
```

### 6. Receive Payment from Customer
**POST** `/api/Payments/receive`

Record payment received from a customer.

**Request Body:**
```json
{
  "customerId": "guid-here",
  "amount": 1000.00,
  "paymentDate": "2024-01-20T14:30:00Z",
  "paymentMethod": "Cash",  // Cash, Card, BankTransfer, Mixed
  "cashAmount": 1000.00,    // Required if Mixed
  "notes": "Payment for Invoice INV-20240115-0001"
}
```

### 7. Make Payment to Supplier
**POST** `/api/Payments/make`

Record payment made to a supplier.

**Request Body:**
```json
{
  "supplierId": "guid-here",
  "amount": 2000.00,
  "paymentDate": "2024-01-20T14:30:00Z",
  "paymentMethod": "BankTransfer",
  "notes": "Payment for Purchase PUR-20240110-0001"
}
```

### 8. Get Customer Payment History
**GET** `/api/Payments/customer/{customerId}`

### 9. Get Supplier Payment History
**GET** `/api/Payments/supplier/{supplierId}`

## Automatic Integration

The SOA module automatically integrates with:

### Sales Module
- **On Sale Creation**: Creates debit entry if sale is on credit
- **On Sale Return**: Creates credit entry to reduce customer balance
- **Document Number**: Uses invoice number (e.g., INV-20240115-0001)

### Purchase Module
- **On Purchase Creation**: Creates credit entry if purchase is on credit
- **On Purchase Return**: Creates debit entry to reduce supplier balance
- **Document Number**: Uses purchase invoice number (e.g., PUR-20240115-0001)

### Cashbox Module
- **Payment Received**: Updates cashbox when customer pays in cash
- **Payment Made**: Updates cashbox when supplier is paid in cash
- **Mixed Payments**: Handles both cash and card portions

## Database Schema

### AccountLedger Table
```sql
- Id (Guid, PK)
- PartyType (enum: Customer, Supplier)
- PartyId (Guid)
- TransactionDate (DateTime)
- TransactionType (enum)
- DocumentNumber (string)
- DebitAmount (decimal)
- CreditAmount (decimal)
- RunningBalance (decimal)
- ReferenceId (Guid, nullable)
- Notes (string, nullable)
- UserId (Guid, nullable)
- CreatedAt (DateTime)
- UpdatedAt (DateTime, nullable)
- IsDeleted (bool)
```

## Usage Examples

### Example 1: View Customer Statement
```bash
curl -X POST "https://api.example.com/api/StatementOfAccount/query" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "partyType": "Customer",
    "partyId": "customer-guid",
    "startDate": "2024-01-01T00:00:00Z",
    "endDate": "2024-12-31T23:59:59Z",
    "page": 1,
    "pageSize": 50
  }'
```

### Example 2: Record Customer Payment
```bash
curl -X POST "https://api.example.com/api/Payments/receive" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "customer-guid",
    "amount": 1500.00,
    "paymentDate": "2024-01-20T10:00:00Z",
    "paymentMethod": "Cash",
    "notes": "Payment received"
  }'
```

### Example 3: Create Credit Note
```bash
curl -X POST "https://api.example.com/api/StatementOfAccount/adjustment" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "partyType": "Customer",
    "partyId": "customer-guid",
    "adjustmentType": "CreditNote",
    "amount": 200.00,
    "notes": "Discount for damaged goods"
  }'
```

## Business Rules

1. **Opening Balance**: Can only be created once per party
2. **Running Balance**: Automatically calculated based on previous balance
3. **Party Balance**: Always synchronized with ledger entries
4. **Document Numbers**: Auto-generated with prefixes:
   - `INV-` for sales invoices
   - `PUR-` for purchase invoices
   - `RCP-` for payment receipts
   - `PV-` for payment vouchers
   - `CN-` for credit notes
   - `DN-` for debit notes
   - `OB-` for opening balances

5. **Balance Types**:
   - Customer with positive balance = Debit (they owe us)
   - Customer with negative balance = Credit (we owe them)
   - Supplier with positive balance = Credit (we owe them)
   - Supplier with negative balance = Debit (they owe us)

## Reports You Can Generate

1. **Customer Aging Report**: Filter by date ranges to see overdue amounts
2. **Supplier Payables Report**: View all outstanding supplier balances
3. **Transaction History**: Complete audit trail of all financial movements
4. **Payment History**: Track all payments received/made
5. **Balance Summary**: Quick overview of all outstanding balances

## Best Practices

1. **Set Opening Balances**: Always set opening balances when migrating from another system
2. **Regular Reconciliation**: Periodically verify ledger balances match party balances
3. **Document References**: Always include notes for manual adjustments
4. **Date Accuracy**: Use correct transaction dates for accurate reporting
5. **Payment Tracking**: Record payments promptly to maintain accurate balances

## Troubleshooting

### Balance Mismatch
If party balance doesn't match ledger:
1. Check the last ledger entry for that party
2. Verify all transactions are recorded
3. Look for any manual balance modifications

### Missing Transactions
If transactions aren't appearing:
1. Verify the date range filter
2. Check if party filter is correctly applied
3. Ensure transactions were committed successfully

### Incorrect Balance Type
If balance type shows incorrectly:
1. Verify the party type (Customer vs Supplier)
2. Check the sign of the balance
3. Review the debit/credit logic for that party type

## Future Enhancements

- PDF export for statements
- Email statements to customers/suppliers
- Automated payment reminders
- Multi-currency support
- Payment terms tracking
- Interest calculation on overdue amounts
