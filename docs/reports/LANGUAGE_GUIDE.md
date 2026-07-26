# Multi-Language Support Implementation Guide

## Overview
Your POS system now supports both **English** and **Arabic** with automatic RTL (Right-to-Left) layout switching.

## Features Implemented

### ✅ Language Switcher
- Button in the top navigation bar
- Shows "EN" when in Arabic mode, "ع" when in English mode
- Automatically saves language preference to localStorage
- Switches the entire UI instantly

### ✅ RTL Support
- Automatic direction change when switching to Arabic
- Proper text alignment and layout flip
- Improved navigation spacing for Arabic text

### ✅ Comprehensive Translations
Translation keys are organized by feature area:
- `app.*` - Application-level (name, logout)
- `nav.*` - Navigation menu items
- `sales.*` - Sales-related pages
- `inventory.*` - Inventory pages
- `purchases.*` - Purchase pages
- `accounts.*` - Accounting pages
- `common.*` - Common UI elements (buttons, labels, etc.)
- `dashboard.*` - Dashboard specific
- `login.*` - Login page
- `pos.*` - Point of Sale
- `products.*` - Products page
- `categories.*` - Categories page
- `customers.*` - Customers page
- `suppliers.*` - Suppliers page
- `warehouses.*` - Warehouses page
- `cashbox.*` - Cashbox page

## How to Use Translations in Your Pages

### Step 1: Import the hook
```javascript
import { useTranslation } from 'react-i18next';
```

### Step 2: Use the hook in your component
```javascript
export default function YourPage() {
    const { t } = useTranslation();
    
    return (
        <div>
            <h1>{t('dashboard.title')}</h1>
            <button>{t('common.add')}</button>
        </div>
    );
}
```

### Step 3: Replace hardcoded text
**Before:**
```javascript
<h1>Dashboard</h1>
<button>Add Product</button>
<label>Search</label>
```

**After:**
```javascript
<h1>{t('dashboard.title')}</h1>
<button>{t('products.addProduct')}</button>
<label>{t('common.search')}</label>
```

## Example: Translating a Page

Here's how to translate the Dashboard page:

```javascript
import { useTranslation } from 'react-i18next';

export default function Dashboard() {
    const { t } = useTranslation();
    
    return (
        <div>
            <h1>{t('dashboard.title')}</h1>
            <p>{t('dashboard.welcomeBack')}</p>
            
            <div className="stats">
                <div className="stat-card">
                    <h3>{t('dashboard.todaySales')}</h3>
                    <p>$1,234</p>
                </div>
                
                <div className="stat-card">
                    <h3>{t('dashboard.totalCustomers')}</h3>
                    <p>156</p>
                </div>
            </div>
            
            <button>{t('common.refresh')}</button>
        </div>
    );
}
```

## Adding New Translations

To add new translation keys, edit `src/i18n/config.js`:

```javascript
const en = {
    // ... existing translations
    yourNewSection: {
        title: "Your Title",
        description: "Your Description"
    }
};

const ar = {
    // ... existing translations
    yourNewSection: {
        title: "عنوانك",
        description: "وصفك"
    }
};
```

## Common Translation Keys Reference

### Common UI Elements
- `common.search` - "Search" / "بحث"
- `common.add` - "Add" / "إضافة"
- `common.edit` - "Edit" / "تعديل"
- `common.delete` - "Delete" / "حذف"
- `common.save` - "Save" / "حفظ"
- `common.cancel` - "Cancel" / "إلغاء"
- `common.loading` - "Loading..." / "جاري التحميل..."
- `common.noData` - "No data available" / "لا توجد بيانات"

### Form Fields
- `common.name` - "Name" / "الاسم"
- `common.description` - "Description" / "الوصف"
- `common.date` - "Date" / "التاريخ"
- `common.amount` - "Amount" / "المبلغ"
- `common.price` - "Price" / "السعر"
- `common.quantity` - "Quantity" / "الكمية"

### Navigation
- `nav.dashboard` - "Dashboard" / "لوحة التحكم"
- `nav.pos` - "POS" / "نقطة البيع"
- `nav.sales` - "Sales" / "المبيعات"
- `nav.inventory` - "Inventory" / "المخزون"
- `nav.purchases` - "Purchases" / "المشتريات"
- `nav.accounts` - "Accounts" / "الحسابات"
- `nav.cashbox` - "Cashbox" / "الصندوق"

## Testing the Language Switcher

1. **Login Page**: The language switcher appears in the top-right corner
2. **After Login**: The switcher is in the main navigation bar
3. **Click to Switch**: Click the button to toggle between English and Arabic
4. **Automatic RTL**: The layout automatically flips for Arabic
5. **Persistent**: Your language choice is saved and restored on next visit

## Best Practices

1. **Always use translation keys** instead of hardcoded text
2. **Group related translations** under the same parent key
3. **Use descriptive key names** (e.g., `products.addProduct` not `prod.add`)
4. **Test both languages** after adding new features
5. **Keep translations consistent** across similar features

## Next Steps

To fully translate your application:

1. Open each page file in `src/pages/`
2. Import `useTranslation` hook
3. Replace hardcoded English text with `t('key.name')`
4. Test the page in both languages
5. Add any missing translation keys to `config.js`

## Example Pages Already Translated

- ✅ Login Page
- ✅ Layout/Navigation
- ⏳ Dashboard (translations available, needs implementation)
- ⏳ POS (translations available, needs implementation)
- ⏳ Products (translations available, needs implementation)
- ⏳ Other pages (translations available, needs implementation)

All translation keys are ready - you just need to use them in your components!
