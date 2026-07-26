# Unit System - Quick Guide

## 🔍 Understanding the Two Unit Systems

### Old System (Basic Unit Field in ProductForm)
**Location:** Add/Edit Product → Basic Information → Unit field
**Purpose:** Simple text field for display purposes only
**Example:** "Piece", "Kg", "Box"
**Limitations:** 
- No conversion factors
- No price per unit
- Just a label

### New System (Multi-Unit with ProductUnitsModal)
**Location:** Products List → "Manage Units" button
**Purpose:** Advanced multi-unit management with conversion and pricing
**Features:**
- Multiple units per product
- Conversion factors (e.g., 1 Carton = 12 Pieces)
- Different prices per unit
- Default unit selection
- Base unit designation

---

## 🚀 Quick Setup Guide

### Step 1: Seed Units (REQUIRED FIRST!)

The "Manage Units" dropdown is empty because you need to create units first.

**Option A: Run PowerShell Script**
```powershell
cd c:\Users\fadya\source\repos\PointOfSale
.\seed-units.ps1
```

**Option B: Use API Endpoint**
Open browser and go to:
```
http://localhost:5000/swagger
```
Find: `POST /api/units/seed-defaults`
Click "Try it out" → "Execute"

**Option C: Manual Creation**
Go to Swagger → `POST /api/units` and create each unit:
```json
{
  "name": "Piece",
  "symbol": "pc",
  "description": "Individual piece"
}
```

### Step 2: Configure Product Units

After seeding units:
1. Go to **Products** page
2. Click **"Manage Units"** on any product
3. Click **"Add Unit"**
4. Now you'll see units in the dropdown!
5. Select unit (e.g., "Carton")
6. Set conversion factor (e.g., 12 = 1 carton = 12 pieces)
7. Set prices
8. Save

---

## 📋 Recommended Approach

### For New Products:
1. **Create product** using ProductForm
   - Fill basic info
   - Set default prices
   - Use simple "Unit" field (e.g., "Piece") for display
   
2. **Configure multi-unit** after creation
   - Click "Manage Units"
   - Add additional units (Carton, Box, etc.)
   - Set conversion factors
   - Set prices per unit

### For Existing Products:
1. Keep the basic "Unit" field as is (for backward compatibility)
2. Use "Manage Units" to add advanced multi-unit support
3. The basic unit field will still work for display

---

## 🔧 Troubleshooting

### "Unit dropdown is empty in Manage Units"
**Problem:** No units in database
**Solution:** Run `seed-units.ps1` or use seed-defaults API

### "Which unit field should I use?"
**Basic Unit Field:** For simple display name
**Manage Units:** For actual multi-unit functionality with conversion

### "Do I need both?"
**No!** The new system (Manage Units) is more powerful.
- Old field: Optional, for display only
- New system: Required for multi-unit transactions

---

## 💡 Best Practices

1. **Always seed units first** before using "Manage Units"
2. **Set one unit as base unit** (conversion factor = 1)
3. **Set one unit as default** (used in transactions by default)
4. **Use meaningful conversion factors** (e.g., 1 Carton = 12 Pieces)
5. **Set prices per unit** for auto-fill in transactions

---

## 📊 Example Setup

### Product: Coca Cola Can

**Step 1: Create Product (ProductForm)**
- Name: Coca Cola Can
- Unit: "Piece" (basic field)
- Cost Price: $0.50
- Selling Price: $1.00

**Step 2: Configure Multi-Unit (Manage Units)**

| Unit | Conversion | Purchase Price | Selling Price | Default | Base |
|------|-----------|----------------|---------------|---------|------|
| Piece | 1 | $0.50 | $1.00 | ✓ | ✓ |
| 6-Pack | 6 | $2.50 | $5.00 | | |
| Carton | 24 | $10.00 | $20.00 | | |

**Result:**
- Buy in cartons: 5 cartons @ $10 = $50 → Stock: 120 pieces
- Sell in 6-packs: 3 packs @ $5 = $15 → Stock: 102 pieces
- Sell in pieces: 2 pieces @ $1 = $2 → Stock: 100 pieces

---

## 🎯 Next Steps

1. ✅ Run `seed-units.ps1` to create units
2. ✅ Refresh "Manage Units" modal - dropdown should now have units
3. ✅ Configure your products with multiple units
4. ✅ Test purchases and sales with different units

---

## 📞 Quick Commands

**Seed Units:**
```powershell
.\seed-units.ps1
```

**Check Units in Database:**
```
GET http://localhost:5000/api/units
```

**Add Unit to Product:**
```
Products → Manage Units → Add Unit
```

---

**Status:** Ready to use after seeding units!
