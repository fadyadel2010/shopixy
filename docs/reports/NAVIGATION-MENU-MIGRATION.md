# Navigation Menu Rebuild - Migration Notes

**Date:** 2025-12-04  
**Status:** Completed

## Summary

The application navigation menu has been completely rebuilt and reordered in both Arabic and English languages. This document lists all renames and structural changes made.

---

## Files Modified

1. **`PointOfSale.Web/src/components/Sidebar.jsx`** - Navigation menu component
2. **`PointOfSale.Web/src/i18n/config.js`** - Translation configuration (both English and Arabic)

---

## STEP 1 — Arabic Rename Mapping

| Old Name (Arabic) | New Name (Arabic) | Section/Context |
|---|---|---|
| العملاء | تسجيل العملاء | Database → Customers |
| المودرين | تسجيل الموردين | Database → Suppliers |
| مندوبي المبيعات | تسجيل منوب المبيعات | Database → Sales Rep |
| الفئات | الأقسام الرئيسية & الفرعية | Database → Categories |
| المستودعات | المخازن | Database → Warehouses |
| المنتجات | كارت الصنف | Database → Products |
| تحويل الرصيد | إيداع و سحب الحسابات | Accounts |
| الرصيد الأفتتاحي | رصيد أول المدة | Opening Balance (section) |
| الخزنة (in Opening Balance) | رصيد أول المدة الخزينة | Opening Balance → Treasury |
| العملاء (in Opening Balance) | رصيد أول المدة العملاء | Opening Balance → Customers |
| الموردين (in Opening Balance) | رصيد أول المدة الموردين | Opening Balance → Suppliers |
| مخزون المستودع | رصيد أول المدة المخازن | Opening Balance → Warehouse |
| نقطع البيع | المبيعات | Transactions → Sales |
| مرتجعات المبيعات | مرتجع المبيعات | Transactions → Sales Return |
| مشتريات جديدة | المشتريات | Transactions → Purchases |
| مرتجعات المشتريات | مرتجع المشتريات | Transactions → Purchase Return |
| الصندوق | رصيد الخزنة | Treasury → Cash Box |
| المدفوعات | مدفوعات عميل & مورد | Treasury → Payments |
| التسويات | تسوية عميل & مورد | Treasury → Settlements |
| إدارة المخزون | رصيد المخازن | Inventory |
| نقل المخزون | تحويلات المخازن | Inventory |
| قئات المصروفات | تسجيل المصروفات | Expenses |
| المصروفات | حركة المصروفات | Expenses |
| سجل المبيعات | تقرير المبيعات | Reports |
| كشف الحساب / الحسابان | كشف مورد & عميل | Account Statement |
| كشف الحساب (bank) | كشف حساب البنك | Account Statement |

---

## STEP 2 — English Rename Mapping

| Old Name (English) | New Name (English) | Translation Key |
|---|---|---|
| Customers | Customers | nav.customersRegistration |
| Suppliers | Suppliers | nav.suppliersRegistration |
| Sales Representatives | Sales Representatives | nav.salesRepRegistration |
| Categories | Main & Sub Categories | nav.mainSubCategories |
| Warehouses | Warehouses | nav.warehouses |
| Products | Item Card | nav.itemCard |
| Balance Transfer | Accounts Deposit & Withdrawal | nav.accountsDepositWithdrawal |
| Opening Balance | Opening Balance | nav.openingBalance |
| (Treasury in Opening Balance) | Treasury Opening Balance | nav.treasuryOpeningBalance |
| (Customers in Opening Balance) | Customers Opening Balance | nav.customersOpeningBalance |
| (Suppliers in Opening Balance) | Suppliers Opening Balance | nav.suppliersOpeningBalance |
| (Warehouse Stock) | Warehouse Opening Balance | nav.warehouseOpeningBalance |
| Sales Invoice / Sales | Sales | nav.sales |
| Sales Returns | Sales Return | nav.salesReturn |
| Purchases | Purchases | nav.purchases |
| Purchase Returns | Purchase Return | nav.purchaseReturn |
| Cash Box / Treasury | Treasury Balance | nav.treasuryBalance |
| Payments | Customer & Supplier Payments | nav.customerSupplierPayments |
| Settlements | Customer & Supplier Settlement | nav.customerSupplierSettlement |
| (Treasury Movements) | Treasury Deposit & Withdrawal | nav.treasuryDepositWithdrawal |
| Inventory Management | Inventory Balance | nav.inventoryBalance |
| Inventory Transfer | Inventory Transfers | nav.inventoryTransfers |
| Expense Categories | Expenses Registration | nav.expensesRegistration |
| Expenses | Expenses Movement | nav.expensesMovement |
| (Expense Report) | Expenses Report | nav.expensesReport |
| Sales Log | Sales Report | nav.salesReport |
| Statement of Account | Supplier & Customer Statement | nav.supplierCustomerStatement |
| Bank Statement | Bank Statement | nav.bankStatement |
| Rep Reports | Sales Representatives Reports | nav.salesRepReports |

---

## STEP 3 — New Final Navigation Structure

### Final Menu Order (Both Languages)

| # | Arabic | English | Route/Path |
|---|---|---|---|
| **1** | **لوحة التحكم** | **Dashboard** | `/` |
| **2** | **قاعدة البيانات** | **Database** | (group) |
| 2.1 | تسجيل العملاء | Customers | `/customers` |
| 2.2 | تسجيل الموردين | Suppliers | `/suppliers` |
| 2.3 | تسجيل منوب المبيعات | Sales Representatives | `/sales-representatives` |
| 2.4 | الأقسام الرئيسية & الفرعية | Main & Sub Categories | `/categories` |
| 2.5 | المخازن | Warehouses | `/warehouses` |
| 2.6 | البنوك | Banks | `/banks` |
| 2.7 | كارت الصنف | Item Card | `/products` |
| **3** | **الحسابات** | **Accounts** | (group) |
| 3.1 | الحسابات الرئيسية | Main Accounts | `/financial-accounts?tab=main` |
| 3.2 | الحسابات الفرعية | Sub Accounts | `/financial-accounts?tab=sub` |
| 3.3 | إيداع و سحب الحسابات | Accounts Deposit & Withdrawal | `/balance-transfer` |
| **4** | **رصيد أول المدة** | **Opening Balance** | (group) |
| 4.1 | رصيد أول المدة الخزينة | Treasury Opening Balance | `/opening-balance/cashbox` |
| 4.2 | رصيد أول المدة العملاء | Customers Opening Balance | `/opening-balance/customers` |
| 4.3 | رصيد أول المدة الموردين | Suppliers Opening Balance | `/opening-balance/suppliers` |
| 4.4 | رصيد أول المدة المخازن | Warehouse Opening Balance | `/opening-balance/warehouse-stock` |
| **5** | **الحركة** | **Transactions** | (group) |
| 5.1 | المبيعات | Sales | `/pos` |
| 5.2 | مرتجع المبيعات | Sales Return | `/sales-returns` |
| 5.3 | المشتريات | Purchases | `/purchase-invoice` |
| 5.4 | مرتجع المشتريات | Purchase Return | `/purchase-returns` |
| **6** | **الخزنة** | **Treasury** | (group) |
| 6.1 | رصيد الخزنة | Treasury Balance | `/cashbox` |
| 6.2 | مدفوعات عميل & مورد | Customer & Supplier Payments | `/payments` |
| 6.3 | تسوية عميل & مورد | Customer & Supplier Settlement | `/adjustments` |
| 6.4 | الأرصدة المستحقة | Outstanding Balances | `/outstanding-balances` |
| 6.5 | إيداع و سحب الخزينة | Treasury Deposit & Withdrawal | `/treasury-movements` |
| **7** | **المخزن** | **Inventory** | (group) |
| 7.1 | رصيد المخازن | Inventory Balance | `/inventory` |
| 7.2 | تحويلات المخازن | Inventory Transfers | `/stock-transfers` |
| **8** | **المصروفات** | **Expenses** | (group) |
| 8.1 | تسجيل المصروفات | Expenses Registration | `/expense-categories` |
| 8.2 | حركة المصروفات | Expenses Movement | `/expenses` |
| 8.3 | تقرير المصروفات | Expenses Report | `/reports/expenses` |
| **9** | **كشف الحساب** | **Account Statement** | (group) |
| 9.1 | كشف مورد & عميل | Supplier & Customer Statement | `/statement-of-account` |
| 9.2 | كشف حساب البنك | Bank Statement | `/reports/bank-statement` |
| **10** | **التقارير** | **Reports** | (group) |
| 10.1 | تقرير المبيعات | Sales Report | `/sales-history` |
| 10.2 | تقارير المندوبين | Sales Representatives Reports | `/sales-rep-reports` |

---

## Routes Preserved

All existing routes have been preserved. The navigation changes are purely cosmetic (label/display name changes). No route changes were made, so:

- No redirects are needed
- No backend controller changes are required
- All existing bookmarks and links will continue to work

---

## New Translation Keys Added

### English (`en` object in config.js)

```javascript
nav: {
    // ... existing keys ...
    customersRegistration: "Customers",
    suppliersRegistration: "Suppliers",
    salesRepRegistration: "Sales Representatives",
    mainSubCategories: "Main & Sub Categories",
    warehouses: "Warehouses",
    itemCard: "Item Card",
    accountsDepositWithdrawal: "Accounts Deposit & Withdrawal",
    treasuryOpeningBalance: "Treasury Opening Balance",
    customersOpeningBalance: "Customers Opening Balance",
    suppliersOpeningBalance: "Suppliers Opening Balance",
    warehouseOpeningBalance: "Warehouse Opening Balance",
    transactions: "Transactions",
    salesReturn: "Sales Return",
    purchaseReturn: "Purchase Return",
    treasuryBalance: "Treasury Balance",
    customerSupplierPayments: "Customer & Supplier Payments",
    customerSupplierSettlement: "Customer & Supplier Settlement",
    treasuryDepositWithdrawal: "Treasury Deposit & Withdrawal",
    inventoryBalance: "Inventory Balance",
    inventoryTransfers: "Inventory Transfers",
    expensesRegistration: "Expenses Registration",
    expensesMovement: "Expenses Movement",
    expensesReport: "Expenses Report",
    accountStatement: "Account Statement",
    supplierCustomerStatement: "Supplier & Customer Statement",
    bankStatement: "Bank Statement",
    salesReport: "Sales Report",
    salesRepReports: "Sales Representatives Reports"
}
```

### Arabic (`ar` object in config.js)

```javascript
nav: {
    // ... existing keys ...
    customersRegistration: "تسجيل العملاء",
    suppliersRegistration: "تسجيل الموردين",
    salesRepRegistration: "تسجيل منوب المبيعات",
    mainSubCategories: "الأقسام الرئيسية & الفرعية",
    warehouses: "المخازن",
    itemCard: "كارت الصنف",
    accountsDepositWithdrawal: "إيداع و سحب الحسابات",
    treasuryOpeningBalance: "رصيد أول المدة الخزينة",
    customersOpeningBalance: "رصيد أول المدة العملاء",
    suppliersOpeningBalance: "رصيد أول المدة الموردين",
    warehouseOpeningBalance: "رصيد أول المدة المخازن",
    transactions: "الحركة",
    salesReturn: "مرتجع المبيعات",
    purchaseReturn: "مرتجع المشتريات",
    treasuryBalance: "رصيد الخزنة",
    customerSupplierPayments: "مدفوعات عميل & مورد",
    customerSupplierSettlement: "تسوية عميل & مورد",
    treasuryDepositWithdrawal: "إيداع و سحب الخزينة",
    inventoryBalance: "رصيد المخازن",
    inventoryTransfers: "تحويلات المخازن",
    expensesRegistration: "تسجيل المصروفات",
    expensesMovement: "حركة المصروفات",
    expensesReport: "تقرير المصروفات",
    accountStatement: "كشف الحساب",
    supplierCustomerStatement: "كشف مورد & عميل",
    bankStatement: "كشف حساب البنك",
    salesReport: "تقرير المبيعات",
    salesRepReports: "تقارير المندوبين"
}
```

---

## Items Removed from Menu

The following items were removed or reorganized:

1. **POS** (as a top-level menu item) - Now part of "Transactions" section as "Sales"
2. **Movement** (as a section) - Merged into new "Transactions" section
3. **Users Management** section - "Sales Representatives" moved to "Database" section
4. **Opening Balance → Banks** - Removed (only Treasury, Customers, Suppliers, Warehouse remain)

---

## Testing Checklist

- [ ] Verify all menu items display correctly in English
- [ ] Verify all menu items display correctly in Arabic
- [ ] Verify RTL layout works properly for Arabic
- [ ] Verify all navigation links work correctly
- [ ] Verify menu expansion/collapse works
- [ ] Test on mobile view (responsive sidebar)

---

## Rollback Instructions

If needed, revert the following files to their previous state:

1. `PointOfSale.Web/src/components/Sidebar.jsx`
2. `PointOfSale.Web/src/i18n/config.js`

No database changes were made, so no database rollback is needed.
