# SOA Frontend Implementation Guide

## Overview

The Statement of Account (SOA) frontend module provides a complete user interface for managing customer and supplier accounts, including viewing statements, recording payments, tracking outstanding balances, and creating adjustments.

## Pages Created

### 1. Statement of Account (`/statement-of-account`)
**File:** `PointOfSale.Web/src/pages/StatementOfAccount.jsx`

**Features:**
- Filter by party type (Customer/Supplier)
- Select specific party or view all
- Date range filtering
- Pagination support
- Summary cards showing:
  - Opening Balance
  - Total Debit
  - Total Credit
  - Closing Balance with type (Debit/Credit)
- Detailed transaction list with:
  - Date
  - Transaction type
  - Document number
  - Debit/Credit amounts
  - Running balance
  - Notes

**Usage:**
1. Select party type (Customer or Supplier)
2. Choose a specific party or leave blank for all
3. Optionally set date range
4. Click "Load Statement"
5. View summary and transaction details

### 2. Payments (`/payments`)
**File:** `PointOfSale.Web/src/pages/Payments.jsx`

**Features:**
- Two tabs: Receive Payment (Customers) and Make Payment (Suppliers)
- Party selection with balance display
- Payment recording with:
  - Amount
  - Payment date
  - Payment method (Cash, Card, Bank Transfer, Mixed)
  - Notes
- Payment history for selected party
- Real-time balance updates

**Usage:**
1. Select tab (Receive or Make Payment)
2. Choose customer/supplier
3. View current balance
4. Click payment button
5. Fill in payment details
6. Submit to record payment

### 3. Outstanding Balances (`/outstanding-balances`)
**File:** `PointOfSale.Web/src/pages/OutstandingBalances.jsx`

**Features:**
- Two tabs: Customers (Receivables) and Suppliers (Payables)
- Summary cards showing:
  - Total parties with balances
  - Total debit balances
  - Total credit balances
- Search functionality
- Grid view of all parties with outstanding balances
- Color-coded balance types (Red for Debit, Green for Credit)
- Quick actions:
  - View statement
  - Record payment

**Usage:**
1. Select tab (Customers or Suppliers)
2. Use search to filter parties
3. Click on a party card to view statement
4. Use quick action buttons for payments

### 4. Adjustments (`/adjustments`)
**File:** `PointOfSale.Web/src/pages/Adjustments.jsx`

**Features:**
- Party type selection (Customer/Supplier)
- Party selection with balance display
- Adjustment types:
  - Credit Note (reduces amount owed)
  - Debit Note (increases amount owed)
  - Credit Adjustment (general credit)
  - Debit Adjustment (general debit)
- Required notes for audit trail
- Real-time balance updates

**Usage:**
1. Select party type
2. Choose party
3. View current balance
4. Click "Create Adjustment"
5. Select adjustment type
6. Enter amount and notes
7. Submit to create adjustment

## Components Created

### 1. PaymentModal
**File:** `PointOfSale.Web/src/components/PaymentModal.jsx`

Reusable modal for recording payments with:
- Party information display
- Current balance with color coding
- Payment form fields
- Mixed payment support (cash + card)
- Loading states

### 2. AdjustmentModal
**File:** `PointOfSale.Web/src/components/AdjustmentModal.jsx`

Reusable modal for creating adjustments with:
- Party information display
- Adjustment type selection with descriptions
- Amount input
- Required notes field
- Loading states

## Services Created

### SOA API Service
**File:** `PointOfSale.Web/src/services/soaApi.js`

**Methods:**
- `getStatement(filters)` - Get filtered statement of account
- `getPartyBalance(partyType, partyId)` - Get current balance for a party
- `createOpeningBalance(data)` - Create opening balance
- `createAdjustment(data)` - Create adjustment
- `getOutstandingBalances(partyType)` - Get all parties with balances

### Payments API Service
**File:** `PointOfSale.Web/src/services/soaApi.js`

**Methods:**
- `receivePayment(data)` - Record payment from customer
- `makePayment(data)` - Record payment to supplier
- `getCustomerPayments(customerId)` - Get customer payment history
- `getSupplierPayments(supplierId)` - Get supplier payment history

## Navigation

Added "Accounts" dropdown menu in the main navigation with links to:
- Statement of Account
- Payments
- Outstanding Balances
- Adjustments

## Styling & UI

### Color Scheme
- **Debit Balances:** Red (`text-red-600`, `bg-red-50`)
- **Credit Balances:** Green (`text-green-600`, `bg-green-50`)
- **Primary Actions:** Blue (`bg-blue-600`)
- **Adjustments:** Purple (`bg-purple-600`)

### Responsive Design
- Mobile-friendly layouts
- Responsive grids (1 column on mobile, 2-3 on desktop)
- Horizontal scrolling tables on mobile
- Touch-friendly buttons and inputs

### Icons
- 📄 Statement of Account
- 💵 Payments
- ⚠️ Outstanding Balances
- ⚙️ Adjustments

## Data Flow

### Statement of Account
```
User selects filters → API call to /StatementOfAccount/query → Display summary and transactions
```

### Payments
```
User selects party → Load balance → User fills form → API call to /Payments/receive or /Payments/make → Update balance → Refresh history
```

### Outstanding Balances
```
User selects tab → API call to /StatementOfAccount/outstanding/{type} → Display grid of parties
```

### Adjustments
```
User selects party → Load balance → User fills form → API call to /StatementOfAccount/adjustment → Update balance
```

## Key Features

### 1. Real-time Balance Updates
All payment and adjustment operations immediately update the displayed balance without requiring page refresh.

### 2. Color-Coded Balances
- Debit balances (amounts owed to us) shown in red
- Credit balances (amounts we owe) shown in green
- Zero balances shown in gray

### 3. Comprehensive Filtering
- Party type filtering
- Specific party selection
- Date range filtering
- Search functionality

### 4. Pagination
Large transaction lists are paginated for better performance and usability.

### 5. Audit Trail
All transactions show:
- Date and time
- Document number
- User who created it (where applicable)
- Notes

### 6. Mixed Payment Support
Payments can be split between cash and card, with automatic calculation of card amount.

## Usage Examples

### Example 1: View Customer Statement
1. Navigate to "Accounts" → "Statement of Account"
2. Select "Customer" from party type
3. Choose customer from dropdown
4. Set date range (optional)
5. Click "Load Statement"
6. View summary and transactions

### Example 2: Receive Payment from Customer
1. Navigate to "Accounts" → "Payments"
2. Ensure "Receive Payment" tab is active
3. Select customer from dropdown
4. View current balance
5. Click "Receive Payment" button
6. Enter amount, date, payment method
7. Add notes (optional)
8. Click "Receive Payment"

### Example 3: View Outstanding Customer Balances
1. Navigate to "Accounts" → "Outstanding Balances"
2. Ensure "Customers" tab is active
3. View all customers with balances
4. Use search to find specific customer
5. Click on customer card to view statement
6. Or click "Receive Payment" for quick payment

### Example 4: Create Credit Note
1. Navigate to "Accounts" → "Adjustments"
2. Select "Customer" party type
3. Choose customer from dropdown
4. View current balance
5. Click "Create Adjustment"
6. Select "Credit Note" from type
7. Enter amount
8. Add notes explaining reason
9. Click "Create Adjustment"

## Error Handling

All API calls include error handling with toast notifications:
- Success messages (green)
- Error messages (red)
- Warning messages (yellow)

## Loading States

All forms and data fetching operations show loading indicators:
- Disabled buttons with "Loading..." text
- Skeleton loaders for data tables
- Spinner icons where appropriate

## Validation

### Payment Form
- Amount: Required, must be positive number
- Payment Date: Required, valid date
- Payment Method: Required selection
- Cash Amount: Required if Mixed payment method

### Adjustment Form
- Adjustment Type: Required selection
- Amount: Required, must be positive number
- Notes: Required (for audit trail)

## Best Practices

### 1. Always Add Notes
When creating adjustments, always add descriptive notes for audit purposes.

### 2. Verify Balance Before Payment
Always check the current balance before recording a payment to ensure accuracy.

### 3. Use Date Filters
When viewing statements for specific periods, use date filters to focus on relevant transactions.

### 4. Regular Reconciliation
Regularly check outstanding balances to ensure all accounts are up to date.

### 5. Document Adjustments
For any manual adjustments, document the reason clearly in the notes field.

## Troubleshooting

### Balance Not Updating
- Refresh the page
- Check if the API call was successful (check browser console)
- Verify the transaction was recorded in the statement

### Statement Not Loading
- Check if party is selected
- Verify date range is valid
- Check browser console for API errors
- Ensure API is running

### Payment Not Recording
- Verify all required fields are filled
- Check if amount is valid (positive number)
- Ensure payment method is selected
- Check API logs for errors

## Future Enhancements

Potential features to add:
- [ ] Export statement to PDF
- [ ] Email statement to party
- [ ] Bulk payment recording
- [ ] Payment reminders
- [ ] Aging analysis charts
- [ ] Credit limit warnings
- [ ] Payment terms tracking
- [ ] Automated reconciliation
- [ ] Multi-currency support
- [ ] Advanced filtering options

## Technical Details

### State Management
- Uses React hooks (useState, useEffect)
- Local component state for forms
- API calls through axios

### Routing
- React Router v6
- Protected routes (requires authentication)
- Query parameters for deep linking

### Styling
- Tailwind CSS utility classes
- Responsive design patterns
- Consistent color scheme

### API Integration
- Axios for HTTP requests
- JWT authentication via interceptors
- Error handling with try-catch
- Toast notifications for feedback

## Testing Checklist

- [ ] Load statement for customer
- [ ] Load statement for supplier
- [ ] Filter by date range
- [ ] Paginate through transactions
- [ ] Record customer payment
- [ ] Record supplier payment
- [ ] View outstanding customer balances
- [ ] View outstanding supplier balances
- [ ] Create credit note
- [ ] Create debit note
- [ ] Search parties
- [ ] Navigate between pages
- [ ] Test on mobile device
- [ ] Test error scenarios
- [ ] Verify balance calculations

## Conclusion

The SOA frontend module provides a complete, user-friendly interface for managing all aspects of customer and supplier accounts. It integrates seamlessly with the backend API and provides real-time updates, comprehensive filtering, and detailed transaction tracking.
