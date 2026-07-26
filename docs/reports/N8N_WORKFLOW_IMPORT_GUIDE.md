# 📥 n8n WORKFLOW IMPORT GUIDE

**Quick guide to import and activate the abandoned cart recovery workflow**

---

## 🚀 QUICK START (5 Minutes)

### Step 1: Access n8n
1. Open browser
2. Navigate to: **http://<n8n-host>:5678**
3. Login:
   - Email: `ops@example.com`
   - Password: `<retrieve-from-secret-manager>`

### Step 2: Import Workflow
1. Click **"Workflows"** (left sidebar)
2. Click **"Import from File"** button (top right)
3. Select file: `n8n-workflows/abandoned-cart-recovery-workflow.json`
4. Click **"Import"**

### Step 3: Verify Configuration
1. Workflow should open automatically
2. Check **Webhook Trigger** node:
   - Path should be: `abandoned-cart`
   - URL should be: `http://<n8n-host>:5678/webhook/abandoned-cart`

3. Check **Send First WhatsApp Message** node:
   - URL should be: `http://<baileys-host>:3000/api/whatsapp/send`

4. Check **Send Second WhatsApp Message** node:
   - URL should be: `http://<baileys-host>:3000/api/whatsapp/send`

5. Check **Result Logger** node:
   - URL should be: `http://<pointofsale-api-host>:5000/api/admin/ecommerce/abandoned-cart-recovery/recovery-log`

### Step 4: Activate Workflow
1. Click **"Save"** (Ctrl+S)
2. Toggle **"Active"** switch (top right) to **ON** ✅
3. Workflow is now live!

---

## 🧪 TEST THE WORKFLOW

### Quick Test (1 Minute)

Run this command in terminal:

```bash
curl -X POST http://<n8n-host>:5678/webhook/abandoned-cart \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "00000000-0000-0000-0000-000000000001",
    "cartId": "00000000-0000-0000-0000-000000000002",
    "abandonedCartId": "00000000-0000-0000-0000-000000000003",
    "phoneNumber": "+201234567890",
    "email": "test@example.com",
    "cartSnapshot": {
      "items": [
        {"name": "Test Product", "quantity": 2, "price": 50}
      ],
      "total": 100,
      "currency": "USD"
    },
    "firstMessageDelayMinutes": 0,
    "secondMessageDelayMinutes": 0,
    "workingHoursFrom": "00:00",
    "workingHoursTo": "23:59",
    "recoveryLink": "https://store.com/cart/recover/test-token",
    "triggeredAtUtc": "2026-02-04T10:00:00Z"
  }'
```

**Expected Result:**
- ✅ Webhook returns 200 OK
- ✅ Check n8n **"Executions"** tab
- ✅ See new execution
- ✅ All nodes should be green (success)

---

## 📊 VIEW EXECUTIONS

1. Click **"Executions"** (left sidebar)
2. See list of all workflow runs
3. Click any execution to view:
   - Input data
   - Each node's output
   - Execution time
   - Errors (if any)

---

## 🔧 TROUBLESHOOTING

### Workflow Not Receiving Webhooks
- **Check:** Workflow is Active (toggle ON)
- **Check:** Webhook URL matches `appsettings.json`
- **Check:** n8n is running: `curl http://<n8n-host>:5678`

### WhatsApp Messages Not Sent
- **Check:** Baileys service running: `curl http://<baileys-host>:3000/health`
- **Check:** WhatsApp account connected
- **Check:** Phone number format (E.164: +201234567890)

### Workflow Fails on Validation
- **Check:** All required fields in webhook payload
- **Check:** Phone number starts with `+`
- **Check:** Delays are not negative

---

## ✅ CHECKLIST

- [ ] n8n accessed successfully
- [ ] Workflow imported
- [ ] Webhook URL verified
- [ ] WhatsApp API URLs verified
- [ ] Backend API URL verified
- [ ] Workflow saved
- [ ] Workflow activated
- [ ] Test webhook sent
- [ ] Execution visible in n8n
- [ ] All nodes executed successfully

---

## 🎉 YOU'RE DONE!

Your abandoned cart recovery system is now fully operational!

**Next Steps:**
1. Enable automation in POS Admin
2. Configure WhatsApp account for abandoned cart recovery
3. Test with real cart abandonment
4. Monitor executions in n8n
5. Check backend logs for results

**Need Help?**
- Full documentation: `ABANDONED_CART_RECOVERY_PHASE_3_N8N_AUTOMATION.md`
- Testing guide: See "Testing Guide" section in Phase 3 docs
- n8n Documentation: https://docs.n8n.io


