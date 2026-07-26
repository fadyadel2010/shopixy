# Commerce AI Admin Control - Quick Start Guide

## 🚀 Getting Started

### Step 1: Run Database Migration

The Commerce AI feature requires new columns in the `StoreConfigs` table.

**Option A: Using PowerShell (Windows)**
```powershell
.\add-commerce-ai-columns.ps1
```

**Option B: Using psql directly**
```bash
psql -h localhost -U postgres -d pos_db -f add_commerce_ai_columns.sql
```

### Step 2: Restart API Server

After running the migration, restart your API server to apply the changes:

```bash
# Navigate to API project
cd PointOfSale.API

# Run the API
dotnet run
```

### Step 3: Access Admin Control Page

1. Log in to the POS Admin UI
2. Navigate to **E-commerce → Commerce AI**
3. You'll see the Commerce AI Control Page

## 🎛️ Using the Admin Control Page

### Enable Commerce AI

1. Click the **ON/OFF toggle** at the top of the page
2. The toggle will turn green and show "Enabled"
3. A success message will appear
4. The ChatWidget will now appear on your Storefront

### Disable Commerce AI

1. Click the **ON/OFF toggle** to turn it off
2. The toggle will turn gray and show "Disabled"
3. A success message will appear
4. The ChatWidget will immediately disappear from your Storefront

## 📊 Understanding the Control Page

### What the AI Can Do ✅
- Answer customer questions about products
- Explain prices and shipping options
- Help customers prepare an order
- Explain order & delivery status

### What the AI Cannot Do 🚫
- Place orders
- Change prices
- Cancel orders
- Trigger payments
- Contact shipping companies
- Act without customer input

### Safety & Privacy 🔒
- The AI only sees your store data
- No customer data is shared externally
- All actions are read-only
- You can disable the feature at any time

### Status Indicators
- **Status**: Shows if AI is Enabled or Disabled
- **AI Models**: Shows available models (DeepSeek, GPT-OSS, Gemma)
- **Mode**: Shows supported modes (Pre-purchase, Post-purchase, Admin)

## 🛒 Storefront Experience

### When AI is Enabled
- A floating chat button appears in the bottom-right corner
- Customers can click to open the chat widget
- AI assistant helps customers with questions
- All interactions are read-only and safe

### When AI is Disabled
- No chat button appears
- No AI functionality is available
- Storefront operates normally without AI

## 🔐 Security & Governance

### Admin Control
- ✅ Only admins can enable/disable the feature
- ✅ Changes take effect immediately (no restart needed)
- ✅ All toggles are logged for audit purposes
- ✅ No API keys or technical configuration required

### Audit Trail
Every time you toggle the feature, the system logs:
- Who made the change (your admin email)
- When the change was made (timestamp)
- What the change was (enabled/disabled)

## 🌍 Multi-Language Support

The Commerce AI Control Page supports:
- **English** (default)
- **Arabic** (RTL layout)

The language automatically matches your Admin UI language setting.

## 🧪 Testing the Feature

### Test Enabling AI:
1. Go to `/admin/ecommerce/commerce-ai`
2. Toggle AI **ON**
3. Open your Storefront in a new tab
4. Look for the chat button in the bottom-right corner
5. Click the chat button and send a test message

### Test Disabling AI:
1. Go back to `/admin/ecommerce/commerce-ai`
2. Toggle AI **OFF**
3. Refresh your Storefront
4. The chat button should disappear

## 📝 API Endpoints (For Developers)

### Admin Endpoints
```
GET  /api/admin/commerce-ai          # Get AI status and info
POST /api/admin/commerce-ai/toggle   # Enable/disable AI
GET  /api/admin/commerce-ai/status   # Lightweight status check
```

### Storefront Endpoint
```
GET /api/storefront/store-config     # Includes commerceAiEnabled flag
```

## ❓ Troubleshooting

### Chat Widget Not Appearing
1. Verify AI is enabled in Admin Control Page
2. Check browser console for errors
3. Clear browser cache and refresh
4. Verify API server is running

### Toggle Not Working
1. Check that you're logged in as an admin
2. Verify X-Tenant-Id header is being sent
3. Check API server logs for errors
4. Ensure database migration was successful

### Changes Not Taking Effect
1. Refresh the Storefront page
2. Clear Redis cache if using caching
3. Check MongoDB projection is up to date
4. Verify API server is running

## 🆘 Support

If you encounter issues:
1. Check the implementation guide: `COMMERCE-AI-ADMIN-CONTROL-IMPLEMENTATION.md`
2. Review API server logs for errors
3. Verify database migration completed successfully
4. Check browser console for frontend errors

## 🎉 Success!

Once enabled, your customers can now:
- Ask questions about products
- Get help with shipping options
- Prepare orders with AI assistance
- Track their orders

All while you maintain full control over the feature!
