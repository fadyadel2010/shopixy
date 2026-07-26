# Unified Financial Engine Architecture
## Production-Grade Double-Entry Accounting System

> **Version**: 1.0  
> **Date**: 2026-02-06  
> **Status**: Architecture Design  
> **Philosophy**: Think Stripe + Odoo combined

---

## 🎯 Executive Summary

This document defines a **Unified Accounting & Treasury Architecture** that serves:
- ✅ POS System (Cash, Visa, Mixed payments)
- ✅ E-commerce Platform (COD, Gateways, Wallets)
- ✅ Payment Gateways (Stripe, Paymob, Fawry)
- ✅ Shipping Companies (COD collection & settlements)

**Core Principles:**
1. **Double-Entry Accounting**: Every transaction has balanced debits and credits
2. **Channel-Agnostic**: Same accounting rules for POS and E-commerce
3. **Full Traceability**: Every penny tracked with audit trail
4. **Clearing Accounts**: Money in transit is never hidden
5. **Atomic Operations**: No partial postings

---

## 📊 1. Chart of Accounts

### 1.1 Account Categories & Codes

```
CHART OF ACCOUNTS STRUCTURE
════════════════════════════════════════════════════════════════

1000-1999 │ ASSETS (Normal Balance: DEBIT)
──────────┼────────────────────────────────────────────────────
1000      │ Cash & Cash Equivalents
1010      │   ├── Cashbox (Main)
1011      │   ├── Cashbox (Branch 1)
1012      │   ├── Cashbox (Branch 2)
1020      │   ├── Visa/Card Account (POS)
1030      │   ├── Bank Account (Main)
1031      │   ├── Bank Account (Secondary)
1040      │   └── Petty Cash
          │
1100      │ Clearing Accounts (Temporary Assets)
1110      │   ├── Visa Clearing (POS Card Pending)
1120      │   ├── Gateway Clearing (Stripe/Paymob Pending)
1121      │   │     ├── Stripe Clearing
1122      │   │     ├── Paymob Clearing
1123      │   │     └── Fawry Clearing
1130      │   ├── COD Clearing (Carrier Pending)
1131      │   │     ├── Bosta COD Clearing
1132      │   │     ├── Aramex COD Clearing
1133      │   │     └── Manual COD Clearing
1140      │   └── Wallet Clearing (VodafoneCash Pending)
          │
1200      │ Accounts Receivable
1210      │   ├── Customer Receivables (POS Credit)
1220      │   └── Online Customer Receivables
          │
1300      │ Inventory
1310      │   ├── Merchandise Inventory
1320      │   └── Inventory in Transit
          │
1400      │ Prepaid & Other Assets
1410      │   └── Prepaid Expenses

2000-2999 │ LIABILITIES (Normal Balance: CREDIT)
──────────┼────────────────────────────────────────────────────
2000      │ Accounts Payable
2010      │   ├── Supplier Payables (POS Credit Purchases)
2020      │   └── Vendor Payables
          │
2100      │ Customer Deposits
2110      │   ├── Customer Prepayments
2120      │   └── Gift Card Liability
          │
2200      │ Accrued Liabilities
2210      │   ├── Accrued Shipping Costs
2220      │   └── Gateway Fees Payable
          │
2300      │ Taxes Payable
2310      │   └── Sales Tax Payable

3000-3999 │ EQUITY (Normal Balance: CREDIT)
──────────┼────────────────────────────────────────────────────
3000      │ Owner's Equity
3100      │ Retained Earnings
3200      │ Current Period Earnings

4000-4999 │ REVENUE (Normal Balance: CREDIT)
──────────┼────────────────────────────────────────────────────
4000      │ Sales Revenue
4010      │   ├── POS Sales
4020      │   ├── E-commerce Sales
4030      │   └── Marketplace Sales (Amazon, etc.)
          │
4100      │ Shipping Revenue
4110      │   └── Shipping Charges Collected
          │
4200      │ Other Income
4210      │   ├── Discounts Received
4220      │   └── Miscellaneous Income

5000-5999 │ COST OF GOODS SOLD (Normal Balance: DEBIT)
──────────┼────────────────────────────────────────────────────
5000      │ Cost of Goods Sold
5010      │   ├── Product Cost
5020      │   └── Shipping Cost (Outbound)

6000-6999 │ EXPENSES (Normal Balance: DEBIT)
──────────┼────────────────────────────────────────────────────
6000      │ Operating Expenses
6010      │   ├── Gateway Processing Fees
6020      │   ├── Shipping Expenses
6030      │   ├── Refunds & Returns
6040      │   └── Chargebacks
          │
6100      │ Administrative Expenses
6110      │   ├── Bank Fees
6120      │   └── General Expenses
```

### 1.2 System Account Definitions

| Account Code | Account Name | Type | Purpose |
|--------------|--------------|------|---------|
| 1010 | Cashbox | Asset | Physical cash from POS sales |
| 1020 | Visa Account | Asset | Settled card payments (POS) |
| 1030 | Bank Account | Asset | Actual bank balance |
| 1110 | Visa Clearing | Asset | Card payments pending settlement |
| 1120 | Gateway Clearing | Asset | Online payments pending settlement |
| 1130 | COD Clearing | Asset | Cash collected by carriers pending remittance |
| 1140 | Wallet Clearing | Asset | Manual wallet payments pending verification |
| 1210 | Customer Receivables | Asset | Credit sales to customers |
| 2010 | Supplier Payables | Liability | Credit purchases from suppliers |
| 4010 | Sales Revenue | Revenue | Product sales income |
| 5010 | COGS | Expense | Cost of products sold |

---

## 📝 2. Transaction Posting Rules

### 2.1 POS Transactions

#### POS Cash Sale
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Customer pays $100 cash for products              │
├─────────────────────────────────────────────────────────────┤
│ DEBIT:  1010 Cashbox              $100.00                   │
│ CREDIT: 4010 Sales Revenue        $100.00                   │
├─────────────────────────────────────────────────────────────┤
│ (COGS Entry - separate journal)                             │
│ DEBIT:  5010 COGS                 $60.00                    │
│ CREDIT: 1310 Inventory            $60.00                    │
└─────────────────────────────────────────────────────────────┘
```

#### POS Visa/Card Sale
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Customer pays $100 by card at POS                 │
├─────────────────────────────────────────────────────────────┤
│ STEP 1: At Point of Sale (Immediate)                        │
│ DEBIT:  1110 Visa Clearing        $100.00                   │
│ CREDIT: 4010 Sales Revenue        $100.00                   │
├─────────────────────────────────────────────────────────────┤
│ STEP 2: Daily Bank Settlement (Next Business Day)           │
│ DEBIT:  1020 Visa Account         $97.50  (Net of fees)     │
│ DEBIT:  6010 Gateway Fees         $2.50   (2.5% fee)        │
│ CREDIT: 1110 Visa Clearing        $100.00                   │
└─────────────────────────────────────────────────────────────┘
```

#### POS Mixed Payment Sale
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: $100 sale, Customer pays $60 cash + $40 card      │
├─────────────────────────────────────────────────────────────┤
│ DEBIT:  1010 Cashbox              $60.00                    │
│ DEBIT:  1110 Visa Clearing        $40.00                    │
│ CREDIT: 4010 Sales Revenue        $100.00                   │
└─────────────────────────────────────────────────────────────┘
```

#### POS Purchase - Cash
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Purchase $500 inventory, pay cash immediately     │
├─────────────────────────────────────────────────────────────┤
│ DEBIT:  1310 Inventory            $500.00                   │
│ CREDIT: 1010 Cashbox              $500.00                   │
└─────────────────────────────────────────────────────────────┘
```

#### POS Purchase - Credit (Ajal)
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Purchase $500 inventory on credit                 │
├─────────────────────────────────────────────────────────────┤
│ DEBIT:  1310 Inventory            $500.00                   │
│ CREDIT: 2010 Supplier Payables    $500.00                   │
└─────────────────────────────────────────────────────────────┘
```

#### Supplier Payment (Later)
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Pay supplier $500 from Cashbox                    │
├─────────────────────────────────────────────────────────────┤
│ DEBIT:  2010 Supplier Payables    $500.00                   │
│ CREDIT: 1010 Cashbox              $500.00                   │
├─────────────────────────────────────────────────────────────┤
│ ALTERNATIVE: Pay from Bank Account                          │
│ DEBIT:  2010 Supplier Payables    $500.00                   │
│ CREDIT: 1030 Bank Account         $500.00                   │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 E-commerce Transactions

#### E-commerce COD Order Created
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Customer places $100 COD order                    │
├─────────────────────────────────────────────────────────────┤
│ NO FINANCIAL ENTRY YET                                      │
│ (Revenue is NOT recognized until delivery)                  │
│                                                             │
│ ✓ Create Order record with PaymentStatus = Pending          │
│ ✓ Create Shipment record with COD flag                      │
│ ✓ Reserve inventory (optional memo entry)                   │
└─────────────────────────────────────────────────────────────┘
```

#### E-commerce COD Delivered
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Carrier delivers & collects $100 COD              │
├─────────────────────────────────────────────────────────────┤
│ STEP 1: On Delivery Confirmation (webhook/manual)           │
│ DEBIT:  1130 COD Clearing (Bosta) $100.00                   │
│ CREDIT: 4020 E-commerce Sales     $100.00                   │
├─────────────────────────────────────────────────────────────┤
│ STEP 2: Carrier Settlement (Weekly/Bi-weekly)               │
│ DEBIT:  1030 Bank Account         $95.00  (Net of fees)     │
│ DEBIT:  6020 Shipping Expenses    $5.00   (COD fee)         │
│ CREDIT: 1130 COD Clearing (Bosta) $100.00                   │
└─────────────────────────────────────────────────────────────┘
```

#### E-commerce Vodafone Cash Payment
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Customer pays $100 via VodafoneCash               │
├─────────────────────────────────────────────────────────────┤
│ STEP 1: Customer uploads proof (PaymentIntent created)      │
│ DEBIT:  1140 Wallet Clearing      $100.00                   │
│ CREDIT: 4020 E-commerce Sales     $100.00                   │
├─────────────────────────────────────────────────────────────┤
│ STEP 2: Admin verifies payment proof                        │
│ DEBIT:  1030 Bank Account         $100.00                   │
│ CREDIT: 1140 Wallet Clearing      $100.00                   │
├─────────────────────────────────────────────────────────────┤
│ ALTERNATIVE: Admin rejects proof                            │
│ DEBIT:  4020 E-commerce Sales     $100.00  (Reverse)        │
│ CREDIT: 1140 Wallet Clearing      $100.00  (Reverse)        │
└─────────────────────────────────────────────────────────────┘
```

#### E-commerce Gateway Payment (Stripe/Paymob)
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Customer pays $100 via Stripe                     │
├─────────────────────────────────────────────────────────────┤
│ STEP 1: Payment Intent Created (checkout)                   │
│ NO ENTRY - Payment not yet captured                         │
├─────────────────────────────────────────────────────────────┤
│ STEP 2: Payment Captured (webhook: payment_intent.succeeded)│
│ DEBIT:  1121 Stripe Clearing      $100.00                   │
│ CREDIT: 4020 E-commerce Sales     $100.00                   │
├─────────────────────────────────────────────────────────────┤
│ STEP 3: Stripe Payout (T+2 to T+7)                          │
│ DEBIT:  1030 Bank Account         $97.10  (Net)             │
│ DEBIT:  6010 Gateway Fees         $2.90   (2.9% + $0.30)    │
│ CREDIT: 1121 Stripe Clearing      $100.00                   │
└─────────────────────────────────────────────────────────────┘
```

#### Shipping Company Settlement
```
┌─────────────────────────────────────────────────────────────┐
│ SCENARIO: Bosta sends weekly settlement of $1,000           │
│           (10 COD orders, $100 each, $5 fee per order)      │
├─────────────────────────────────────────────────────────────┤
│ Settlement Entry:                                           │
│ DEBIT:  1030 Bank Account         $950.00  (Net received)   │
│ DEBIT:  6020 Shipping Expenses    $50.00   (Carrier fees)   │
│ CREDIT: 1131 Bosta COD Clearing   $1,000.00                 │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔄 3. Clearing Accounts Strategy

### 3.1 Why Clearing Accounts?

```
THE PROBLEM WITHOUT CLEARING ACCOUNTS:
══════════════════════════════════════

Day 1: Customer pays $100 by card
  → If we credit Bank immediately: WRONG (money not in bank yet)
  → If we don't record: WHERE IS THE $100?!

Day 3: Bank settlement arrives
  → Only $97.50 (fees deducted)
  → How do we reconcile the $2.50 difference?

THE SOLUTION: CLEARING ACCOUNTS
═══════════════════════════════

Day 1: Card swipe
  → DEBIT: Visa Clearing $100 (Asset: money we're OWED)
  → CREDIT: Sales $100

Day 3: Settlement
  → DEBIT: Bank $97.50
  → DEBIT: Gateway Fees $2.50
  → CREDIT: Visa Clearing $100 (Clears the holding account)

✅ No hidden money
✅ Clear audit trail
✅ Easy reconciliation
```

### 3.2 Clearing Account Lifecycle

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│   TRANSACTION    │────▶│    CLEARING      │────▶│     SETTLED      │
│     OCCURS       │     │    ACCOUNT       │     │     ACCOUNT      │
└──────────────────┘     └──────────────────┘     └──────────────────┘
       │                        │                        │
   POS Card Sale           Visa Clearing            Bank Account
   Stripe Payment          Stripe Clearing          Bank Account
   COD Delivered           COD Clearing             Bank Account
   Wallet Upload           Wallet Clearing          Bank Account
```

### 3.3 Clearing Account Monitoring Alerts

| Clearing Account | Expected Settlement Time | Alert Threshold |
|------------------|--------------------------|-----------------|
| Visa Clearing | T+1 to T+2 | Balance > 3 days old |
| Stripe Clearing | T+2 to T+7 | Balance > 10 days old |
| Paymob Clearing | T+2 to T+5 | Balance > 7 days old |
| Bosta COD Clearing | Weekly | Balance > 14 days old |
| Wallet Clearing | Manual | Balance > 24 hours |

---

## 🔍 4. Settlement & Reconciliation Flow

### 4.1 Daily Reconciliation Process

```
┌─────────────────────────────────────────────────────────────────┐
│                    DAILY RECONCILIATION FLOW                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │   SYSTEM     │    │   EXTERNAL   │    │   COMPARE    │       │
│  │   BALANCES   │───▶│   SOURCES    │───▶│   & MATCH    │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
│         │                   │                   │                │
│    Clearing        Bank Statement       ┌───────┴───────┐       │
│    Account         Gateway Portal       │               │       │
│    Balances        Carrier Portal       ▼               ▼       │
│                                    ┌─────────┐    ┌─────────┐   │
│                                    │  MATCH  │    │MISMATCH │   │
│                                    └────┬────┘    └────┬────┘   │
│                                         │              │        │
│                                         ▼              ▼        │
│                                    Auto-Clear     Create        │
│                                    Clearing       Exception     │
│                                    Account        Report        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 4.2 Settlement Recording Flow

```csharp
// SETTLEMENT RECORDING PSEUDOCODE

async Task RecordSettlement(SettlementRequest request)
{
    // 1. Validate settlement against clearing balance
    var clearingBalance = await GetClearingBalance(request.ClearingAccountId);
    
    if (request.GrossAmount > clearingBalance)
        throw new SettlementMismatchException(
            $"Settlement {request.GrossAmount} exceeds clearing {clearingBalance}");
    
    // 2. Create atomic journal entry
    var entries = new List<JournalEntry>
    {
        // DEBIT: Bank (net amount received)
        new JournalEntry
        {
            AccountId = request.BankAccountId,
            DebitAmount = request.NetAmount,
            CreditAmount = 0
        },
        // DEBIT: Fees (if any)
        new JournalEntry
        {
            AccountId = GATEWAY_FEES_ACCOUNT,
            DebitAmount = request.Fees,
            CreditAmount = 0
        },
        // CREDIT: Clearing Account
        new JournalEntry
        {
            AccountId = request.ClearingAccountId,
            DebitAmount = 0,
            CreditAmount = request.GrossAmount
        }
    };
    
    // 3. Validate balance (Debits = Credits)
    ValidateJournalBalance(entries);
    
    // 4. Post atomically
    await PostJournalEntries(entries, request.SettlementReference);
}
```

### 4.3 Mismatch Detection & Resolution

```
MISMATCH TYPES & RESOLUTION:
════════════════════════════

1. TIMING DIFFERENCE
   └─ Cause: Transaction posted but settlement not yet received
   └─ Resolution: Wait for settlement, auto-clears

2. AMOUNT DIFFERENCE
   └─ Cause: Fees, chargebacks, adjustments
   └─ Resolution: 
      • If fees: Post to Gateway Fees account
      • If chargeback: Post to Chargebacks expense
      • If unknown: Create exception for manual review

3. MISSING TRANSACTION
   └─ Cause: Settlement received for unrecorded transaction
   └─ Resolution: 
      • Research source (order, payment intent)
      • Create correcting journal entry
      • Flag for audit

4. DUPLICATE SETTLEMENT
   └─ Cause: Webhook retry, carrier error
   └─ Resolution: 
      • Idempotency check (by settlement reference)
      • Reject duplicate, log event
```

---

## 💾 5. Data Model

### 5.1 Core Tables

```sql
-- ═══════════════════════════════════════════════════════════════
-- ACCOUNTS: Chart of Accounts
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE Accounts (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Tenants(Id),
    
    -- Account Identity
    AccountCode         VARCHAR(20) NOT NULL,      -- "1010", "1110", "4010"
    Name                VARCHAR(200) NOT NULL,     -- "Cashbox", "Visa Clearing"
    NameAr              VARCHAR(200),              -- Arabic name
    
    -- Classification
    AccountType         INT NOT NULL,              -- 1=Asset, 2=Liability, 3=Equity, 4=Revenue, 5=Expense, 6=COGS
    NormalBalance       VARCHAR(10) NOT NULL,      -- "DEBIT" or "CREDIT"
    
    -- Hierarchy
    ParentAccountId     UUID REFERENCES Accounts(Id),
    Level               INT NOT NULL DEFAULT 1,
    
    -- Balance (denormalized for performance)
    CurrentBalance      DECIMAL(18,2) NOT NULL DEFAULT 0,
    
    -- Flags
    IsSystem            BOOLEAN NOT NULL DEFAULT FALSE,  -- Cannot be deleted
    IsClearing          BOOLEAN NOT NULL DEFAULT FALSE,  -- Requires settlement
    IsActive            BOOLEAN NOT NULL DEFAULT TRUE,
    
    -- Metadata
    ClearingSourceType  VARCHAR(50),               -- "Gateway", "Carrier", "Wallet"
    ClearingSourceId    VARCHAR(100),              -- "stripe", "bosta", etc.
    ExpectedSettlementDays INT,                    -- Alert threshold
    
    -- Audit
    CreatedAt           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UpdatedAt           TIMESTAMPTZ,
    
    UNIQUE(TenantId, AccountCode)
);

-- ═══════════════════════════════════════════════════════════════
-- JOURNAL_ENTRIES: Immutable audit log of all postings
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE JournalEntries (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Tenants(Id),
    
    -- Journal Identity
    JournalNumber       VARCHAR(50) NOT NULL,      -- "JE-2026-000123"
    JournalDate         DATE NOT NULL,
    PostedAt            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Source Tracking
    SourceType          VARCHAR(50) NOT NULL,      -- "SalesInvoice", "Order", "Settlement"
    SourceId            UUID NOT NULL,             -- ID of source document
    SourceReference     VARCHAR(100),              -- "INV-2026-001234", "ORD-2026-005678"
    
    -- Description
    Description         VARCHAR(500) NOT NULL,
    
    -- Idempotency
    IdempotencyKey      VARCHAR(100) NOT NULL,     -- Prevents duplicate postings
    
    -- Status
    Status              VARCHAR(20) NOT NULL DEFAULT 'POSTED',  -- POSTED, REVERSED
    ReversedByJournalId UUID REFERENCES JournalEntries(Id),
    ReversalReason      VARCHAR(500),
    
    -- Audit
    CreatedByUserId     UUID REFERENCES Users(Id),
    
    UNIQUE(TenantId, IdempotencyKey)
);

-- ═══════════════════════════════════════════════════════════════
-- LEDGER_ENTRIES: Individual debit/credit lines
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE LedgerEntries (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Tenants(Id),
    JournalEntryId      UUID NOT NULL REFERENCES JournalEntries(Id),
    
    -- Account
    AccountId           UUID NOT NULL REFERENCES Accounts(Id),
    
    -- Amounts (exactly one should be non-zero)
    DebitAmount         DECIMAL(18,2) NOT NULL DEFAULT 0,
    CreditAmount        DECIMAL(18,2) NOT NULL DEFAULT 0,
    
    -- Running Balance (for this account)
    BalanceAfter        DECIMAL(18,2) NOT NULL,
    
    -- Line Description (optional)
    LineDescription     VARCHAR(200),
    
    -- Sequence within journal
    LineNumber          INT NOT NULL,
    
    -- Timestamps
    CreatedAt           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    CONSTRAINT chk_single_amount CHECK (
        (DebitAmount > 0 AND CreditAmount = 0) OR 
        (DebitAmount = 0 AND CreditAmount > 0)
    )
);

-- ═══════════════════════════════════════════════════════════════
-- SETTLEMENTS: External settlement records
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE Settlements (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Tenants(Id),
    
    -- Settlement Identity
    SettlementNumber    VARCHAR(50) NOT NULL,      -- "STL-2026-000001"
    SettlementDate      DATE NOT NULL,
    
    -- Source
    SourceType          VARCHAR(50) NOT NULL,      -- "Gateway", "Carrier"
    SourceCode          VARCHAR(50) NOT NULL,      -- "stripe", "bosta"
    ExternalReference   VARCHAR(200),              -- Provider's settlement ID
    
    -- Amounts
    GrossAmount         DECIMAL(18,2) NOT NULL,    -- Expected from clearing
    FeesAmount          DECIMAL(18,2) NOT NULL DEFAULT 0,
    NetAmount           DECIMAL(18,2) NOT NULL,    -- Actual received
    
    -- Accounts
    ClearingAccountId   UUID NOT NULL REFERENCES Accounts(Id),
    BankAccountId       UUID NOT NULL REFERENCES Accounts(Id),
    FeesAccountId       UUID REFERENCES Accounts(Id),
    
    -- Linked Transactions
    TransactionCount    INT NOT NULL DEFAULT 0,
    
    -- Journal Link
    JournalEntryId      UUID REFERENCES JournalEntries(Id),
    
    -- Status
    Status              VARCHAR(20) NOT NULL DEFAULT 'PENDING',  -- PENDING, MATCHED, RECONCILED, EXCEPTION
    ReconciliationNotes VARCHAR(500),
    
    -- Audit
    CreatedAt           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ReconciledAt        TIMESTAMPTZ,
    ReconciledByUserId  UUID REFERENCES Users(Id),
    
    UNIQUE(TenantId, SourceType, SourceCode, ExternalReference)
);

-- ═══════════════════════════════════════════════════════════════
-- PAYMENT_SOURCES: Unified payment tracking across channels
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE PaymentSources (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Tenants(Id),
    
    -- Source Document
    SourceType          VARCHAR(50) NOT NULL,      -- "POS", "Ecommerce", "Shipping"
    SourceDocumentType  VARCHAR(50) NOT NULL,      -- "SalesInvoice", "Order", "Shipment"
    SourceDocumentId    UUID NOT NULL,
    SourceReference     VARCHAR(100),              -- Invoice/Order number
    
    -- Payment Details
    PaymentMethodCode   VARCHAR(50) NOT NULL,      -- "CASH", "CARD", "COD", "STRIPE"
    PaymentProvider     VARCHAR(50),               -- "stripe", "paymob", "bosta"
    ProviderReference   VARCHAR(200),              -- External payment ID
    
    -- Amounts
    GrossAmount         DECIMAL(18,2) NOT NULL,
    FeesAmount          DECIMAL(18,2) NOT NULL DEFAULT 0,
    NetAmount           DECIMAL(18,2) NOT NULL,
    Currency            VARCHAR(3) NOT NULL DEFAULT 'EGP',
    
    -- Status
    Status              VARCHAR(20) NOT NULL,      -- "PENDING", "CAPTURED", "SETTLED", "REFUNDED"
    
    -- Clearing
    ClearingAccountId   UUID REFERENCES Accounts(Id),
    SettlementId        UUID REFERENCES Settlements(Id),
    
    -- Journal Link
    CaptureJournalId    UUID REFERENCES JournalEntries(Id),
    SettlementJournalId UUID REFERENCES JournalEntries(Id),
    
    -- Timestamps
    CapturedAt          TIMESTAMPTZ,
    SettledAt           TIMESTAMPTZ,
    CreatedAt           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ═══════════════════════════════════════════════════════════════
-- PAYMENT_METHODS: Available payment methods per tenant
-- ═══════════════════════════════════════════════════════════════
CREATE TABLE PaymentMethods (
    Id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    TenantId            UUID NOT NULL REFERENCES Tenants(Id),
    
    -- Identity
    Code                VARCHAR(50) NOT NULL,      -- "CASH", "CARD", "COD", "STRIPE"
    DisplayName         VARCHAR(100) NOT NULL,
    DisplayNameAr       VARCHAR(100),
    
    -- Classification
    Channel             VARCHAR(20) NOT NULL,      -- "POS", "ECOMMERCE", "BOTH"
    PaymentType         VARCHAR(20) NOT NULL,      -- "IMMEDIATE", "DEFERRED", "GATEWAY"
    
    -- Linked Accounts
    TargetAccountId     UUID REFERENCES Accounts(Id),     -- Where money lands (Cashbox, Bank)
    ClearingAccountId   UUID REFERENCES Accounts(Id),     -- For deferred (Clearing)
    FeesAccountId       UUID REFERENCES Accounts(Id),     -- For fees
    
    -- Fees Configuration
    FeeType             VARCHAR(20),               -- "FIXED", "PERCENTAGE", "MIXED"
    FeeFixed            DECIMAL(18,2) DEFAULT 0,
    FeePercentage       DECIMAL(5,4) DEFAULT 0,
    
    -- Status
    IsEnabled           BOOLEAN NOT NULL DEFAULT TRUE,
    SortOrder           INT NOT NULL DEFAULT 0,
    
    UNIQUE(TenantId, Code)
);

-- ═══════════════════════════════════════════════════════════════
-- INDEXES FOR PERFORMANCE
-- ═══════════════════════════════════════════════════════════════
CREATE INDEX idx_journal_entries_tenant_date ON JournalEntries(TenantId, JournalDate);
CREATE INDEX idx_journal_entries_source ON JournalEntries(TenantId, SourceType, SourceId);
CREATE INDEX idx_ledger_entries_account ON LedgerEntries(AccountId, CreatedAt);
CREATE INDEX idx_ledger_entries_journal ON LedgerEntries(JournalEntryId);
CREATE INDEX idx_settlements_status ON Settlements(TenantId, Status);
CREATE INDEX idx_payment_sources_clearing ON PaymentSources(ClearingAccountId, Status);
```

### 5.2 Entity Relationship Diagram

```
┌─────────────────┐       ┌─────────────────┐
│    Accounts     │       │ PaymentMethods  │
├─────────────────┤       ├─────────────────┤
│ Id              │◄──────│ TargetAccountId │
│ TenantId        │       │ ClearingAccountId│
│ AccountCode     │       │ FeesAccountId   │
│ AccountType     │       │ Code            │
│ CurrentBalance  │       │ Channel         │
│ IsClearing      │       │ PaymentType     │
└────────┬────────┘       └─────────────────┘
         │
         │ 1:N
         ▼
┌─────────────────┐       ┌─────────────────┐
│  LedgerEntries  │◄──────│ JournalEntries  │
├─────────────────┤  N:1  ├─────────────────┤
│ Id              │       │ Id              │
│ JournalEntryId  │       │ JournalNumber   │
│ AccountId       │       │ SourceType      │
│ DebitAmount     │       │ SourceId        │
│ CreditAmount    │       │ IdempotencyKey  │
│ BalanceAfter    │       │ Status          │
└─────────────────┘       └────────┬────────┘
                                   │
         ┌─────────────────────────┼─────────────────────────┐
         │                         │                         │
         ▼                         ▼                         ▼
┌─────────────────┐       ┌─────────────────┐       ┌─────────────────┐
│  SalesInvoice   │       │     Order       │       │   Settlements   │
│  (POS)          │       │  (E-commerce)   │       │                 │
├─────────────────┤       ├─────────────────┤       ├─────────────────┤
│ PaymentMethod   │       │ PaymentMethod   │       │ SourceType      │
│ CashAmount      │       │ PaymentStatus   │       │ GrossAmount     │
│ CardAmount      │       │ Total           │       │ NetAmount       │
└─────────────────┘       └─────────────────┘       │ FeesAmount      │
                                                    │ JournalEntryId  │
                                                    └─────────────────┘
```

---

## 🔌 6. API Flow

### 6.1 POS Financial Entry Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    POS SALE API FLOW                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. POST /api/sales                                              │
│     ┌──────────────────────────────────────────────────────┐    │
│     │ {                                                    │    │
│     │   "items": [...],                                    │    │
│     │   "paymentMethod": "Mixed",                          │    │
│     │   "cashAmount": 60.00,                               │    │
│     │   "cardAmount": 40.00                                │    │
│     │ }                                                    │    │
│     └──────────────────────────────────────────────────────┘    │
│                              │                                   │
│                              ▼                                   │
│  2. SalesController.CreateSale()                                │
│     ├─ Validate inventory                                       │
│     ├─ Calculate totals                                         │
│     ├─ BEGIN TRANSACTION                                        │
│     │   ├─ Create SalesInvoice                                  │
│     │   ├─ Deduct stock                                         │
│     │   ├─ Call FinancialPostingService.PostSale()              │
│     │   │   ├─ Generate idempotency key                         │
│     │   │   ├─ Create JournalEntry                              │
│     │   │   ├─ Create LedgerEntries:                            │
│     │   │   │   ├─ DEBIT: Cashbox $60                           │
│     │   │   │   ├─ DEBIT: Visa Clearing $40                     │
│     │   │   │   └─ CREDIT: Sales Revenue $100                   │
│     │   │   ├─ Update Account Balances                          │
│     │   │   └─ Create PaymentSource records                     │
│     │   └─ Publish SaleCreatedEvent                             │
│     └─ COMMIT TRANSACTION                                       │
│                              │                                   │
│                              ▼                                   │
│  3. Return 201 Created                                          │
│     ┌──────────────────────────────────────────────────────┐    │
│     │ { "invoiceNumber": "INV-2026-001234", ... }          │    │
│     └──────────────────────────────────────────────────────┘    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 6.2 E-commerce Financial Entry Flow

```
┌─────────────────────────────────────────────────────────────────┐
│               E-COMMERCE PAYMENT WEBHOOK FLOW                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. POST /api/webhooks/stripe                                   │
│     (Event: payment_intent.succeeded)                           │
│                              │                                   │
│                              ▼                                   │
│  2. StripeWebhookController.HandleEvent()                       │
│     ├─ Verify webhook signature                                 │
│     ├─ Parse event                                              │
│     ├─ Extract PaymentIntent & Order                            │
│     ├─ Check idempotency (LastWebhookEventId)                   │
│     │                                                           │
│     ├─ BEGIN TRANSACTION                                        │
│     │   ├─ Update PaymentIntent status → Captured               │
│     │   ├─ Update Order.PaymentStatus → Paid                    │
│     │   ├─ Call FinancialPostingService.PostPaymentCapture()    │
│     │   │   ├─ Generate idempotency key                         │
│     │   │   ├─ Create JournalEntry                              │
│     │   │   ├─ Create LedgerEntries:                            │
│     │   │   │   ├─ DEBIT: Stripe Clearing $100                  │
│     │   │   │   └─ CREDIT: E-commerce Sales $100                │
│     │   │   ├─ Update Account Balances                          │
│     │   │   └─ Create PaymentSource (status=CAPTURED)           │
│     │   └─ Publish OrderPaidEvent                               │
│     └─ COMMIT TRANSACTION                                       │
│                              │                                   │
│                              ▼                                   │
│  3. Return 200 OK                                               │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 6.3 Settlement Recording Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                 SHIPPING SETTLEMENT API FLOW                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. POST /api/settlements                                       │
│     ┌──────────────────────────────────────────────────────┐    │
│     │ {                                                    │    │
│     │   "sourceType": "Carrier",                           │    │
│     │   "sourceCode": "bosta",                             │    │
│     │   "externalReference": "STL-BOSTA-2026-W05",         │    │
│     │   "settlementDate": "2026-02-06",                    │    │
│     │   "grossAmount": 1000.00,                            │    │
│     │   "feesAmount": 50.00,                               │    │
│     │   "netAmount": 950.00,                               │    │
│     │   "bankAccountId": "...",                            │    │
│     │   "shipmentIds": ["...", "...", "..."]               │    │
│     │ }                                                    │    │
│     └──────────────────────────────────────────────────────┘    │
│                              │                                   │
│                              ▼                                   │
│  2. SettlementsController.RecordSettlement()                    │
│     ├─ Validate clearing balance ≥ gross amount                 │
│     ├─ Validate shipments match settlement                      │
│     │                                                           │
│     ├─ BEGIN TRANSACTION                                        │
│     │   ├─ Create Settlement record                             │
│     │   ├─ Call FinancialPostingService.PostSettlement()        │
│     │   │   ├─ Create JournalEntry                              │
│     │   │   ├─ Create LedgerEntries:                            │
│     │   │   │   ├─ DEBIT: Bank Account $950                     │
│     │   │   │   ├─ DEBIT: Shipping Expenses $50                 │
│     │   │   │   └─ CREDIT: Bosta COD Clearing $1000             │
│     │   │   └─ Update Account Balances                          │
│     │   ├─ Update PaymentSources → SETTLED                      │
│     │   └─ Update Shipments → COD collected                     │
│     └─ COMMIT TRANSACTION                                       │
│                              │                                   │
│                              ▼                                   │
│  3. Return 201 Created                                          │
│     ┌──────────────────────────────────────────────────────┐    │
│     │ { "settlementNumber": "STL-2026-000001", ... }       │    │
│     └──────────────────────────────────────────────────────┘    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🖥️ 7. UI/UX Behavior

### 7.1 Admin Balance Dashboard

```
┌─────────────────────────────────────────────────────────────────┐
│                    TREASURY DASHBOARD                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │   CASHBOX   │  │ VISA ACCOUNT│  │    BANK     │              │
│  │  EGP 45,230 │  │  EGP 12,500 │  │ EGP 234,100 │              │
│  │  ▲ +2,340   │  │  ▲ +890     │  │  ▲ +15,000  │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ CLEARING ACCOUNTS (Money in Transit)                    │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ Visa Clearing        EGP 3,200   ⚠️ 2 days pending      │    │
│  │ Stripe Clearing      EGP 8,450   ✓ Normal               │    │
│  │ Paymob Clearing      EGP 2,100   ✓ Normal               │    │
│  │ Bosta COD Clearing   EGP 15,680  ⚠️ Settlement due      │    │
│  │ Wallet Clearing      EGP 450     🔴 3 pending reviews   │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ TODAY'S ACTIVITY                                        │    │
│  ├─────────────────────────────────────────────────────────┤    │
│  │ POS Cash Sales         EGP 12,340    45 transactions    │    │
│  │ POS Card Sales         EGP 8,920     32 transactions    │    │
│  │ E-commerce Payments    EGP 5,670     18 orders          │    │
│  │ COD Collections        EGP 3,200     12 deliveries      │    │
│  │ Settlements Received   EGP 15,000    2 settlements      │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 7.2 Account Transfer UI

```
┌─────────────────────────────────────────────────────────────────┐
│                    TRANSFER BETWEEN ACCOUNTS                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  From Account: ┌──────────────────────────────────────────┐     │
│                │ Cashbox (Main)               EGP 45,230 ▼│     │
│                └──────────────────────────────────────────┘     │
│                                                                  │
│  To Account:   ┌──────────────────────────────────────────┐     │
│                │ Bank Account (Main)         EGP 234,100 ▼│     │
│                └──────────────────────────────────────────┘     │
│                                                                  │
│  Amount:       ┌──────────────────────────────────────────┐     │
│                │ EGP 10,000.00                            │     │
│                └──────────────────────────────────────────┘     │
│                                                                  │
│  Reference:    ┌──────────────────────────────────────────┐     │
│                │ Daily bank deposit                       │     │
│                └──────────────────────────────────────────┘     │
│                                                                  │
│                                                                  │
│  [Cancel]                                    [Transfer →]        │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ PREVIEW:                                                │    │
│  │ DEBIT:  Bank Account (Main)          EGP 10,000.00      │    │
│  │ CREDIT: Cashbox (Main)               EGP 10,000.00      │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 7.3 Audit Trail View

```
┌─────────────────────────────────────────────────────────────────┐
│  ACCOUNT: Cashbox (Main) - 1010                                  │
│  Balance: EGP 45,230.00                                          │
├─────────────────────────────────────────────────────────────────┤
│  Filter: [All Types ▼] [Last 7 Days ▼] [Search...         🔍]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Feb 6, 2026 14:32 │ Sale INV-2026-001245                 │  │
│  │ JE-2026-000891    │ POS Cash Sale - Customer: Ahmed      │  │
│  │                   │ DEBIT: +EGP 250.00                   │  │
│  │                   │ Balance After: EGP 45,230.00         │  │
│  │                   │ [View Details]                       │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ Feb 6, 2026 14:15 │ Sale INV-2026-001244                 │  │
│  │ JE-2026-000890    │ POS Cash Sale                        │  │
│  │                   │ DEBIT: +EGP 180.00                   │  │
│  │                   │ Balance After: EGP 44,980.00         │  │
│  │                   │ [View Details]                       │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ Feb 6, 2026 12:00 │ Transfer TRF-2026-000045             │  │
│  │ JE-2026-000885    │ Transfer to Bank - Daily deposit     │  │
│  │                   │ CREDIT: -EGP 20,000.00               │  │
│  │                   │ Balance After: EGP 44,800.00         │  │
│  │                   │ [View Details]                       │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  [← Previous]  Page 1 of 45  [Next →]                           │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🛡️ 8. Safety Guarantees

### 8.1 Idempotency

```csharp
// Every financial operation uses idempotency keys

public class FinancialPostingService
{
    public async Task<JournalEntry> PostTransaction(
        PostingRequest request,
        string idempotencyKey)
    {
        // 1. Check if already posted
        var existing = await _db.JournalEntries
            .FirstOrDefaultAsync(j => 
                j.TenantId == request.TenantId && 
                j.IdempotencyKey == idempotencyKey);
        
        if (existing != null)
        {
            _logger.LogWarning(
                "Duplicate posting attempt. Key: {Key}, Journal: {JournalId}",
                idempotencyKey, existing.Id);
            return existing; // Return existing, don't double-post
        }
        
        // 2. Proceed with posting
        return await CreateJournalEntry(request, idempotencyKey);
    }
}

// Idempotency key generation patterns:
// POS Sale:     "SALE:{TenantId}:{InvoiceId}"
// Order Payment: "ORDER-PAY:{TenantId}:{OrderId}:{PaymentIntentId}"
// Settlement:   "SETTLE:{TenantId}:{SourceCode}:{ExternalReference}"
```

### 8.2 Atomic Transactions

```csharp
// All financial operations wrapped in database transactions

public async Task<SalesInvoice> CreateSale(CreateSaleRequest request)
{
    // Use explicit transaction with serializable isolation
    await using var transaction = await _db.Database
        .BeginTransactionAsync(IsolationLevel.Serializable);
    
    try
    {
        // 1. Create invoice
        var invoice = await CreateInvoice(request);
        
        // 2. Deduct stock
        await DeductStock(invoice.Items);
        
        // 3. Post financial entries (CRITICAL)
        await _financialPostingService.PostSale(invoice);
        
        // 4. Commit all or nothing
        await transaction.CommitAsync();
        
        return invoice;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

### 8.3 Balance Validation

```csharp
// Validate debits = credits before posting

private void ValidateJournalBalance(List<LedgerEntry> entries)
{
    var totalDebits = entries.Sum(e => e.DebitAmount);
    var totalCredits = entries.Sum(e => e.CreditAmount);
    
    if (totalDebits != totalCredits)
    {
        throw new UnbalancedJournalException(
            $"Journal unbalanced. Debits: {totalDebits}, Credits: {totalCredits}");
    }
}

// Validate account balance constraints

private async Task ValidateWithdrawal(Guid accountId, decimal amount)
{
    var account = await _db.Accounts.FindAsync(accountId);
    
    // Only check for Asset accounts (DEBIT normal balance)
    if (account.AccountType == AccountType.Asset)
    {
        if (account.CurrentBalance < amount)
        {
            throw new InsufficientBalanceException(
                $"Cannot withdraw {amount:C} from {account.Name}. " +
                $"Current balance: {account.CurrentBalance:C}");
        }
    }
}
```

### 8.4 No Hidden Balances

```csharp
// Reconciliation check: Sum of ledger entries = Account balance

public async Task<ReconciliationResult> ReconcileAccount(Guid accountId)
{
    var account = await _db.Accounts.FindAsync(accountId);
    
    var ledgerDebits = await _db.LedgerEntries
        .Where(e => e.AccountId == accountId)
        .SumAsync(e => e.DebitAmount);
    
    var ledgerCredits = await _db.LedgerEntries
        .Where(e => e.AccountId == accountId)
        .SumAsync(e => e.CreditAmount);
    
    var calculatedBalance = account.NormalBalance == "DEBIT"
        ? ledgerDebits - ledgerCredits
        : ledgerCredits - ledgerDebits;
    
    if (calculatedBalance != account.CurrentBalance)
    {
        return ReconciliationResult.Mismatch(
            account, calculatedBalance, account.CurrentBalance);
    }
    
    return ReconciliationResult.Matched(account);
}
```

### 8.5 Negative Balance Prevention

```csharp
// Prevention at multiple layers

// Layer 1: Service validation
public async Task ValidateBalanceConstraints(PostingRequest request)
{
    foreach (var line in request.Lines.Where(l => l.CreditAmount > 0))
    {
        var account = await _db.Accounts.FindAsync(line.AccountId);
        
        // Only block negative for tracked accounts
        if (account.PreventNegative && account.AccountType == AccountType.Asset)
        {
            var projectedBalance = account.CurrentBalance - line.CreditAmount;
            if (projectedBalance < 0)
            {
                throw new NegativeBalanceException(account, line.CreditAmount);
            }
        }
    }
}

// Layer 2: Database constraint (for critical accounts)
ALTER TABLE Accounts 
ADD CONSTRAINT chk_cashbox_positive 
CHECK (
    AccountCode != '1010' OR CurrentBalance >= 0
);
```

---

## 📈 Design Rationale

### Why This Architecture?

| Decision | Rationale |
|----------|-----------|
| **Double-Entry** | Industry standard. Self-balancing. Catches errors automatically. |
| **Clearing Accounts** | Money in transit is explicitly tracked. No hidden balances. Easy reconciliation. |
| **Channel-Agnostic** | Same accounting rules for POS and E-commerce. Single source of truth. |
| **Immutable Ledger** | Audit trail. Compliance. Forensic capability. |
| **Idempotency Keys** | Webhooks retry. Network failures. No double-posting. |
| **Atomic Transactions** | All-or-nothing. No partial states. Data integrity. |
| **Explicit Fees** | Separate fee tracking. Accurate P&L. Reconciliation. |

### What This Solves

```
BEFORE (Current System)
═══════════════════════

❌ Multiple transaction tables (CashboxTransaction, VisaTransaction, etc.)
❌ No unified view of all money movement
❌ Clearing accounts not explicit (implicit in PaymentIntent status)
❌ Settlement reconciliation manual
❌ Hard to audit cross-channel payments

AFTER (Unified Engine)
══════════════════════

✅ Single JournalEntry + LedgerEntry for ALL transactions
✅ Unified Chart of Accounts
✅ Explicit Clearing Accounts with monitoring
✅ Automated reconciliation workflows
✅ Full audit trail with drill-down
```

---

## 🚀 Implementation Roadmap

### Phase 1: Core Infrastructure (Week 1-2)
- [ ] Create Account, JournalEntry, LedgerEntry entities
- [ ] Implement FinancialPostingService
- [ ] Create Chart of Accounts seeding
- [ ] Migrate existing VisaTransaction/CashboxTransaction

### Phase 2: POS Integration (Week 3)
- [ ] Modify SalesController to use FinancialPostingService
- [ ] Modify PurchasesController
- [ ] Create inter-account transfer API
- [ ] Build treasury dashboard

### Phase 3: E-commerce Integration (Week 4)
- [ ] Integrate payment webhooks with FinancialPostingService
- [ ] Add COD clearing workflow
- [ ] Add wallet clearing workflow

### Phase 4: Settlements (Week 5)
- [ ] Create Settlement entity and API
- [ ] Build carrier settlement recording
- [ ] Build gateway settlement recording
- [ ] Implement reconciliation checks

### Phase 5: Reporting & UI (Week 6)
- [ ] Account statement reports
- [ ] Clearing account monitoring
- [ ] Reconciliation dashboard
- [ ] Audit trail UI

---

*This architecture provides the foundation for a production-grade financial system that tracks every penny with full traceability across all channels.*
