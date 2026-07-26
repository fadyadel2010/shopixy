# Visa Account (حساب الفيزا) Implementation Summary

## Overview
This document describes the implementation of the Visa Account system, which automatically creates and manages a Main & Sub Account called "حساب الفيزا" (Visa Account) that is connected with:
- Visa Account Statement report
- Payment method "Card" at Point of Sale
- Payment method "Card" in Purchase Invoices

## Implementation Details

### 1. Financial Account Entity
**File**: `PointOfSale.Core\Entities\FinancialAccount.cs`

The `FinancialAccount` entity supports:
- Hierarchical account structure (Main & Sub accounts via `ParentAccountId`)
- System accounts that cannot be deleted or modified (`IsSystem` flag)
- Balance tracking
- Account numbers

### 2. Database Seeding
**File**: `PointOfSale.Infrastructure\Data\ApplicationDbContext.cs`

The Visa Account is automatically seeded in the database with:
- **ID**: `11111111-1111-1111-1111-111111111111` (fixed GUID for easy reference)
- **Name**: `حساب الفيزا` (Arabic name as requested)
- **Account Number**: `VISA-001`
- **IsSystem**: `true` (prevents deletion/modification)
- **Initial Balance**: `0`

### 3. Financial Account Service
**File**: `PointOfSale.API\Services\FinancialAccountService.cs`

Created a new service that:
- Ensures the Visa Account exists in the database
- Restores the account if it was soft-deleted
- Provides a centralized way to access the Visa Account ID

**Service Registration**: Added to `Program.cs` as a scoped service.

### 4. Database Migration
**Migration**: `20251209172937_UpdateVisaAccountName`

Updates the existing Visa Account to:
- Change name from "Visa Account" to "حساب الفيزا"
- Add account number "VISA-001"

**Status**: ✅ Migration applied successfully

### 5. Integration with Sales (POS)
**File**: `PointOfSale.API\Controllers\SalesController.cs`

**Lines 351-387**: Card payment handling
- When `PaymentMethod` is `Card` or `Mixed`:
  - Calculates the card amount
  - Updates the Visa Account balance (increases)
  - Creates a `VisaTransaction` record with:
    - Transaction Type: 1 (Sale by Visa)
    - Amount: Card payment amount
    - IsDebit: true (increases balance)
    - Reference: Invoice number
    - Source: SalesInvoice entity

**Lines 666-696**: Same logic applied in the Update Sale method

**Lines 785-800**: Revert logic in Delete Sale method to properly reverse Visa transactions

### 6. Integration with Purchases
**File**: `PointOfSale.API\Controllers\PurchasesController.cs`

**Lines 362-389**: Card payment handling
- When `PaymentMethod` is `Card` or `Mixed`:
  - Calculates the card amount
  - Updates the Visa Account balance (decreases)
  - Creates a `VisaTransaction` record with:
    - Transaction Type: 10 (Purchase)
    - Amount: Card payment amount
    - IsDebit: false (decreases balance)
    - Reference: Invoice number
    - Source: PurchaseInvoice entity

**Lines 719-745**: Same logic applied in the Update Purchase method

### 7. Visa Account Statement Report
**File**: `PointOfSale.API\Controllers\VisaStatementController.cs`

Provides comprehensive reporting with:
- **GET /api/visastatement/statement**: Filtered statement with date range, transaction type, search
- **GET /api/visastatement/balance**: Current Visa account balance
- **GET /api/visastatement/transaction/{id}**: Transaction details with drill-down to source entities
- **POST /api/visastatement/transaction**: Manual transaction recording
- **POST /api/visastatement/opening-balance**: Set opening balance

**Transaction Types Supported**:
1. Sale by Visa (from POS)
2. Cash Transfer to Visa
3. Cash Transfer from Visa
4. Bank Transfer to Visa
5. Bank Transfer from Visa
6. Return (Refund)
7. Expense
8. Opening Balance
9. Adjustment
10. Purchase (from Purchase Invoices)

### 8. Visa Transaction Entity
**File**: `PointOfSale.Core\Entities\VisaTransaction.cs`

Tracks all Visa account movements with:
- Transaction date and type
- Amount and direction (IsDebit)
- Running balance (BalanceAfter)
- Reference number and description
- Source entity tracking (SalesInvoice, PurchaseInvoice, etc.)
- User tracking (CreatedByUserId)

## Data Flow

### Sale with Card Payment
```
1. User creates sale with PaymentMethod = Card
2. SalesController processes the sale
3. Stock is deducted
4. Customer ledger is updated (if customer selected)
5. Visa Account balance is INCREASED by card amount
6. VisaTransaction record is created (Type 1: Sale by Visa)
7. Transaction is committed
```

### Purchase with Card Payment
```
1. User creates purchase with PaymentMethod = Card
2. PurchasesController processes the purchase
3. Stock is added
4. Supplier ledger is updated
5. Visa Account balance is DECREASED by card amount
6. VisaTransaction record is created (Type 10: Purchase)
7. Transaction is committed
```

### Mixed Payment Example
```
Sale Total: $100
Payment Method: Mixed
Cash Amount: $60
Card Amount: $40

Results:
- Cashbox balance: +$60
- Visa Account balance: +$40
- Two separate transaction records created
```

## API Endpoints

### Financial Accounts
- **GET** `/api/financialaccounts` - List all financial accounts (includes Visa Account)
- **GET** `/api/financialaccounts/{id}` - Get specific account details
- **POST** `/api/financialaccounts` - Create new account (cannot create system accounts)
- **PUT** `/api/financialaccounts/{id}` - Update account (system accounts protected)
- **DELETE** `/api/financialaccounts/{id}` - Delete account (system accounts protected)
- **POST** `/api/financialaccounts/transfer` - Transfer between accounts/cashboxes

### Visa Statement
- **POST** `/api/visastatement/statement` - Get filtered statement
- **GET** `/api/visastatement/balance` - Get current balance
- **GET** `/api/visastatement/transaction/{id}` - Get transaction details
- **POST** `/api/visastatement/transaction` - Record manual transaction
- **POST** `/api/visastatement/opening-balance` - Set opening balance

## System Protection

The Visa Account is protected as a system account:
1. **Cannot be deleted** - FinancialAccountsController checks `IsSystem` flag
2. **Cannot be renamed** - Update endpoint prevents modification of system accounts
3. **Auto-restored** - FinancialAccountService restores if soft-deleted
4. **Fixed ID** - Uses a well-known GUID for consistent reference

## Testing Checklist

- [x] Database migration applied successfully
- [x] Visa Account created with Arabic name "حساب الفيزا"
- [x] Account number "VISA-001" assigned
- [x] Sales with Card payment updates Visa Account (increase)
- [x] Purchases with Card payment updates Visa Account (decrease)
- [x] Mixed payments split correctly between Cash and Card
- [x] Visa Statement report shows all transactions
- [x] Transaction drill-down works for Sales and Purchases
- [x] System account protection prevents deletion/modification
- [x] Balance calculation is accurate across all transaction types

## Future Enhancements

1. **Bank Integration**: Connect Visa Account with bank reconciliation
2. **Card Terminal Integration**: Direct integration with payment terminals
3. **Multi-Currency**: Support for foreign currency card transactions
4. **Commission Tracking**: Track card processing fees
5. **Settlement Reports**: Daily/monthly card settlement reports
6. **Chargeback Handling**: Support for card payment reversals

## Notes

- The Visa Account is a **Financial Account** in the chart of accounts
- It can have sub-accounts for different card types (Visa, Mastercard, etc.)
- All card transactions are automatically recorded in real-time
- The balance represents the total amount received via card payments minus card purchases
- The account integrates seamlessly with the existing ledger system
- Arabic name ensures proper display in bilingual UI

## Related Files

### Backend
- `PointOfSale.Core\Entities\FinancialAccount.cs`
- `PointOfSale.Core\Entities\VisaTransaction.cs`
- `PointOfSale.Infrastructure\Data\ApplicationDbContext.cs`
- `PointOfSale.API\Services\FinancialAccountService.cs`
- `PointOfSale.API\Controllers\FinancialAccountsController.cs`
- `PointOfSale.API\Controllers\VisaStatementController.cs`
- `PointOfSale.API\Controllers\SalesController.cs`
- `PointOfSale.API\Controllers\PurchasesController.cs`
- `PointOfSale.API\Program.cs`

### Database
- Migration: `20251209172937_UpdateVisaAccountName.cs`

## Conclusion

The Visa Account (حساب الفيزا) system is now fully implemented and integrated with:
- ✅ Point of Sale (Card payments)
- ✅ Purchase Invoices (Card payments)
- ✅ Visa Account Statement Report
- ✅ Financial Accounts Management
- ✅ Automatic creation and protection

The system is production-ready and follows best practices for financial transaction management.
