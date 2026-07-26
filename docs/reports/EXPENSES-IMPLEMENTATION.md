# Expenses Management Implementation

## Overview
Complete implementation of the Expenses Management module for the POS system.

## Database Entities Created

### 1. ExpenseCategory
- **Fields**: Id, Name, Description, IsActive
- **Purpose**: Categorize expenses (e.g., Rent, Utilities, Salaries, Marketing)

### 2. Expense
- **Fields**: 
  - Basic: ExpenseCategoryId, ExpenseDate, Amount, Description, Notes
  - Payment: PaymentMethod, CashboxId, BankId, SupplierId
  - Document: ReceiptPath, ReferenceNumber
  - Recurring: IsRecurring, RecurringFrequency
  - Approval: RequiresApproval, IsApproved, ApprovedBy, ApprovedAt

## API Endpoints

### Expense Categories
- `GET /api/expensecategories` - List all categories
- `GET /api/expensecategories/{id}` - Get category by ID
- `POST /api/expensecategories` - Create category
- `PUT /api/expensecategories/{id}` - Update category
- `DELETE /api/expensecategories/{id}` - Delete category

### Expenses
- `GET /api/expenses` - List expenses (with pagination & filters)
- `GET /api/expenses/{id}` - Get expense by ID
- `POST /api/expenses` - Create expense
- `PUT /api/expenses/{id}` - Update expense
- `DELETE /api/expenses/{id}` - Delete expense
- `POST /api/expenses/{id}/approve` - Approve/reject expense
- `POST /api/expenses/report` - Generate expense report

## Frontend Pages

### 1. Expense Categories Management
- **Route**: `/expense-categories`
- **Features**:
  - List all categories
  - Add/Edit/Delete categories
  - Toggle active status

### 2. Expenses Management
- **Route**: `/expenses`
- **Features**:
  - List expenses with filters
  - Create new expense
  - Edit/Delete expense
  - Upload receipt
  - Approve expenses (if required)
  - Link to cashbox/bank/supplier

### 3. Expense Reports
- **Route**: `/reports/expenses`
- **Features**:
  - Filter by date range, category, payment method
  - Summary cards (total, by category, by payment method)
  - Export to Excel/PDF
  - Print functionality

## Integration Points

### Cashbox Integration
- When expense payment method is "Cash", deduct from selected cashbox
- Create CashboxTransaction record

### Bank Integration
- When payment method is "Bank Transfer", deduct from selected bank
- Create BankTransaction record

### Supplier Integration
- Link expenses to suppliers for tracking
- Useful for recurring expenses (rent, utilities)

### Financial Accounts
- All expenses should be recorded in the general ledger
- Create AccountLedger entries

## Workflow

### Creating an Expense
1. User selects category
2. Enters amount, date, description
3. Selects payment method
4. If Cash → selects cashbox
5. If Bank Transfer → selects bank
6. Optionally links to supplier
7. Uploads receipt (optional)
8. If requires approval → sets flag
9. System creates expense record
10. System updates cashbox/bank balance
11. System creates financial ledger entry

### Approving an Expense
1. Admin/Manager reviews expense
2. Approves or rejects
3. System updates expense status
4. If rejected → optionally reverses cashbox/bank transaction

## Next Steps
1. Create database migration
2. Implement ExpenseCategoriesController
3. Implement ExpensesController
4. Create frontend components
5. Add translations (English & Arabic)
6. Test integration with cashbox and banks

## Files Created
- ✅ ExpenseCategory.cs (Entity)
- ✅ Expense.cs (Entity)
- ✅ ExpenseDTOs.cs (DTOs)
- ✅ ApplicationDbContext.cs (Updated)
- ⏳ ExpenseCategoriesController.cs
- ⏳ ExpensesController.cs
- ⏳ ExpenseCategories.jsx (Frontend)
- ⏳ Expenses.jsx (Frontend)
- ⏳ ExpenseReport.jsx (Frontend)
