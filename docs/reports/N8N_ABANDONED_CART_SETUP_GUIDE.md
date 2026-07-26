# 🔧 n8n ABANDONED CART RECOVERY - SETUP GUIDE

**Date:** February 4, 2026  
**n8n Instance:** http://<n8n-host>:5678  
**Email:** ops@example.com  
**Status:** Ready for Configuration

---

## 📋 OVERVIEW

This guide walks you through setting up the n8n workflow for abandoned cart recovery. The backend (Phase 2) is complete and will send cart data to your n8n webhook. Now we need to configure n8n to receive that data and send WhatsApp messages.

---

## 🎯 WHAT YOU'LL BUILD

```
Backend Webhook → n8n Workflow → WhatsApp Messages
                      ↓
                  [Delay Logic]
                  [Working Hours]
                  [Message Templates]
```

---

## ⚙️ CONFIGURATION ALREADY DONE

✅ **Backend Configuration Added:**
```json
{
  "AbandonedCart": {
    "N8nWebhookUrl": "http://<n8n-host>:5678/webhook/abandoned-cart",
    "TokenSecret": "<inject-via-secret-manager>"
  }
}
```

This is already in your `PointOfSale.API/appsettings.json` file.

---

## 🚀 STEP-BY-STEP SETUP

### STEP 1: Access n8n

1. Open your browser
2. Navigate to: **http://<n8n-host>:5678**
3. Login with:
   - Email: `ops@example.com`
   - Password: `<retrieve-from-secret-manager>`

---

### STEP 2: Create New Workflow

1. Click **"+ New Workflow"** button (top right)
2. Name it: **"Abandoned Cart Recovery"**
3. Click **"Save"** (Ctrl+S)

---

### STEP 3: Add Webhook Trigger Node

1. Click **"+ Add first step"**
2. Search for **"Webhook"**
3. Select **"Webhook"** node
4. Configure:
   - **HTTP Method:** `POST`
   - **Path:** `abandoned-cart`
   - **Authentication:** `None` (for now)
   - **Response Mode:** `Immediately`
   - **Response Code:** `200`

5. Click **"Execute Node"** to get the webhook URL
6. **Copy the URL** - it should be:
   ```
   http://<n8n-host>:5678/webhook/abandoned-cart
   ```

7. **Verify** this matches your `appsettings.json` configuration ✅

---

### STEP 4: Add Function Node (Parse Data)

1. Click **"+"** after Webhook node
2. Search for **"Code"**
3. Select **"Code"** node
4. Rename to: **"Parse Cart Data"**
5. Paste this code:

```javascript
// Parse incoming webhook data
const data = $input.item.json;

// Extract cart information
const cartData = {
  tenantId: data.tenantId,
  cartId: data.cartId,
  abandonedCartId: data.abandonedCartId,
  phoneNumber: data.phoneNumber,
  email: data.email,
  recoveryLink: data.recoveryLink,
  
  // Cart snapshot
  cartSnapshot: data.cartSnapshot,
  
  // Timing configuration
  firstMessageDelayMinutes: data.firstMessageDelayMinutes,
  secondMessageDelayMinutes: data.secondMessageDelayMinutes,
  workingHoursFrom: data.workingHoursFrom,
  workingHoursTo: data.workingHoursTo,
  
  // Metadata
  triggeredAtUtc: data.triggeredAtUtc,
  
  // Calculated fields
  cartTotal: data.cartSnapshot?.total || 0,
  cartCurrency: data.cartSnapshot?.currency || 'USD',
  itemCount: data.cartSnapshot?.items?.length || 0
};

return { json: cartData };
```

6. Click **"Test step"** to verify

---

### STEP 5: Add Wait Node (First Message Delay)

1. Click **"+"** after Parse Cart Data
2. Search for **"Wait"**
3. Select **"Wait"** node
4. Rename to: **"Wait for First Message"**
5. Configure:
   - **Resume:** `After time interval`
   - **Amount:** `{{ $json.firstMessageDelayMinutes }}`
   - **Unit:** `Minutes`

**Note:** This respects the tenant's configured delay (default: 60 minutes)

---

### STEP 6: Add Function Node (Check Working Hours)

1. Click **"+"** after Wait node
2. Search for **"Code"**
3. Select **"Code"** node
4. Rename to: **"Check Working Hours"**
5. Paste this code:

```javascript
// Check if current time is within working hours
const now = new Date();
const currentHour = now.getHours();
const currentMinute = now.getMinutes();
const currentTime = currentHour * 60 + currentMinute;

// Parse working hours (format: "HH:mm")
const workingFrom = $json.workingHoursFrom.split(':');
const workingTo = $json.workingHoursTo.split(':');

const workingFromMinutes = parseInt(workingFrom[0]) * 60 + parseInt(workingFrom[1]);
const workingToMinutes = parseInt(workingTo[0]) * 60 + parseInt(workingTo[1]);

// Check if within working hours
const isWithinWorkingHours = currentTime >= workingFromMinutes && currentTime <= workingToMinutes;

// If outside working hours, calculate delay until next working day
let delayMinutes = 0;
if (!isWithinWorkingHours) {
  if (currentTime < workingFromMinutes) {
    // Before working hours today
    delayMinutes = workingFromMinutes - currentTime;
  } else {
    // After working hours today, wait until tomorrow
    delayMinutes = (24 * 60 - currentTime) + workingFromMinutes;
  }
}

return {
  json: {
    ...$json,
    isWithinWorkingHours,
    delayMinutes,
    shouldDelay: delayMinutes > 0
  }
};
```

6. Click **"Test step"**

---

### STEP 7: Add IF Node (Working Hours Check)

1. Click **"+"** after Check Working Hours
2. Search for **"IF"**
3. Select **"IF"** node
4. Configure:
   - **Condition:** `{{ $json.shouldDelay }}`
   - **Operation:** `is equal to`
   - **Value:** `true`

---

### STEP 8: Add Wait Node (Until Working Hours) - TRUE Branch

1. Click **"+"** on the **TRUE** output of IF node
2. Search for **"Wait"**
3. Select **"Wait"** node
4. Rename to: **"Wait Until Working Hours"**
5. Configure:
   - **Resume:** `After time interval`
   - **Amount:** `{{ $json.delayMinutes }}`
   - **Unit:** `Minutes`

---

### STEP 9: Add HTTP Request Node (Send WhatsApp Message)

1. Click **"+"** after the Wait Until Working Hours node (or FALSE branch)
2. Search for **"HTTP Request"**
3. Select **"HTTP Request"** node
4. Rename to: **"Send First WhatsApp Message"**
5. Configure:
   - **Method:** `POST`
   - **URL:** `http://<baileys-host>:3000/api/whatsapp/send` (your Baileys service)
   - **Authentication:** `None`
   - **Body Content Type:** `JSON`
   - **Body:**

```json
{
  "tenantId": "={{ $json.tenantId }}",
  "to": "={{ $json.phoneNumber }}",
  "message": "Hi! 👋\n\nWe noticed you left {{ $json.itemCount }} item(s) in your cart.\n\nYour cart total: {{ $json.cartCurrency }} {{ $json.cartTotal }}\n\nComplete your order now:\n{{ $json.recoveryLink }}\n\nNeed help? Just reply to this message!"
}
```

6. Click **"Test step"**

---

### STEP 10: Add Wait Node (Second Message Delay)

1. Click **"+"** after Send First WhatsApp Message
2. Search for **"Wait"**
3. Select **"Wait"** node
4. Rename to: **"Wait for Second Message"**
5. Configure:
   - **Resume:** `After time interval`
   - **Amount:** `{{ $json.secondMessageDelayMinutes }}`
   - **Unit:** `Minutes`

**Note:** Default is 1440 minutes (24 hours)

---

### STEP 11: Add HTTP Request Node (Send Second Message)

1. Click **"+"** after Wait for Second Message
2. Search for **"HTTP Request"**
3. Select **"HTTP Request"** node
4. Rename to: **"Send Second WhatsApp Message"**
5. Configure same as Step 9, but with different message:

```json
{
  "tenantId": "={{ $json.tenantId }}",
  "to": "={{ $json.phoneNumber }}",
  "message": "⏰ Last chance!\n\nYour cart is still waiting with {{ $json.itemCount }} item(s).\n\nDon't miss out! Complete your order:\n{{ $json.recoveryLink }}\n\n💬 Questions? Reply anytime!"
}
```

---

### STEP 12: Activate Workflow

1. Click **"Save"** (Ctrl+S)
2. Toggle **"Active"** switch (top right) to **ON** ✅
3. Your workflow is now live!

---

## 🧪 TESTING THE WORKFLOW

### Test 1: Manual Webhook Test

1. In n8n, click on the **Webhook** node
2. Click **"Listen for Test Event"**
3. Open a new terminal and run:

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
        {"productId": "prod1", "name": "Test Product", "quantity": 2, "price": 50}
      ],
      "total": 100,
      "currency": "USD"
    },
    "firstMessageDelayMinutes": 1,
    "secondMessageDelayMinutes": 2,
    "workingHoursFrom": "00:00",
    "workingHoursTo": "23:59",
    "recoveryLink": "https://store.com/cart/recover/test-token",
    "triggeredAtUtc": "2026-02-04T10:00:00Z"
  }'
```

4. Check n8n - you should see the workflow execute!

---

### Test 2: End-to-End Test

1. **Enable automation** in POS Admin:
   ```
   PUT /api/admin/ecommerce/abandoned-cart-recovery/settings
   {
     "enableAbandonedRecovery": true,
     "firstMessageDelayMinutes": 1,
     "secondMessageDelayMinutes": 2,
     "workingHoursFrom": "00:00",
     "workingHoursTo": "23:59"
   }
   ```

2. **Configure WhatsApp account** for abandoned cart recovery

3. **Create a test cart** in storefront:
   - Add items to cart
   - Enter phone number
   - Leave checkout page

4. **Wait 1 minute** (FirstMessageDelayMinutes)

5. **Check n8n executions**:
   - Go to **"Executions"** tab
   - You should see a new execution
   - Click to view details

6. **Check WhatsApp**:
   - First message should be sent after 1 minute
   - Second message should be sent after 2 more minutes

---

## 📊 MONITORING & DEBUGGING

### View Executions

1. In n8n, click **"Executions"** (left sidebar)
2. See all workflow runs
3. Click any execution to see:
   - Input data
   - Each node's output
   - Errors (if any)

### Common Issues

**Issue:** Webhook not receiving data
- **Solution:** Check that workflow is **Active** (toggle ON)
- **Solution:** Verify webhook URL matches `appsettings.json`

**Issue:** WhatsApp message not sent
- **Solution:** Check Baileys service is running (`http://<baileys-host>:3000`)
- **Solution:** Verify WhatsApp account is connected
- **Solution:** Check phone number format (E.164: +201234567890)

**Issue:** Working hours not respected
- **Solution:** Check tenant timezone configuration
- **Solution:** Verify working hours format (HH:mm)

---

## 🎨 CUSTOMIZATION

### Message Templates

Edit the message content in the HTTP Request nodes:

**First Message (Gentle Reminder):**
```
Hi! 👋

We noticed you left {{ $json.itemCount }} item(s) in your cart.

Your cart total: {{ $json.cartCurrency }} {{ $json.cartTotal }}

Complete your order now:
{{ $json.recoveryLink }}

Need help? Just reply to this message!
```

**Second Message (Urgency):**
```
⏰ Last chance!

Your cart is still waiting with {{ $json.itemCount }} item(s).

Don't miss out! Complete your order:
{{ $json.recoveryLink }}

💬 Questions? Reply anytime!
```

### Add Product Details

Modify the "Parse Cart Data" node to extract product names:

```javascript
// Extract product names from cart
const productNames = data.cartSnapshot?.items?.map(item => item.name).join(', ') || 'your items';

return {
  json: {
    ...cartData,
    productNames
  }
};
```

Then use in message:
```
You left: {{ $json.productNames }}
```

---

## 🔐 SECURITY (OPTIONAL)

### Add Webhook Authentication

1. In Webhook node, change **Authentication** to **Header Auth**
2. Set:
   - **Name:** `X-Webhook-Secret`
   - **Value:** `your-secret-key`

3. Update backend configuration:
```json
{
  "AbandonedCart": {
    "N8nWebhookUrl": "http://<n8n-host>:5678/webhook/abandoned-cart",
    "N8nWebhookSecret": "your-secret-key",
    "TokenSecret": "<inject-via-secret-manager>"
  }
}
```

4. Modify `N8nRecoveryOrchestrator.cs` to send header:
```csharp
client.DefaultRequestHeaders.Add("X-Webhook-Secret", _configuration["AbandonedCart:N8nWebhookSecret"]);
```

---

## 📈 ANALYTICS (FUTURE)

### Track Message Delivery

Add HTTP Request node after each WhatsApp message:

```
POST http://localhost:5000/api/admin/abandoned-cart/track
{
  "abandonedCartId": "={{ $json.abandonedCartId }}",
  "messageType": "first",
  "status": "sent",
  "sentAt": "={{ $now }}"
}
```

### Track Recovery Success

When customer completes order, backend will call:
```csharp
await _cartService.MarkAsRecoveredAsync(cartId, orderId);
```

---

## ✅ CHECKLIST

- [ ] n8n workflow created
- [ ] Webhook node configured
- [ ] Parse Cart Data node added
- [ ] First message delay configured
- [ ] Working hours check added
- [ ] WhatsApp message nodes configured
- [ ] Second message delay configured
- [ ] Workflow activated
- [ ] Manual test successful
- [ ] End-to-end test successful

---

## 🎉 YOU'RE DONE!

Your abandoned cart recovery system is now fully operational:

✅ Backend detects abandoned carts  
✅ Backend triggers n8n webhook  
✅ n8n respects working hours  
✅ n8n sends WhatsApp messages  
✅ Customers receive recovery links  
✅ System tracks recovery success  

**Next Steps:**
- Monitor executions in n8n
- Customize message templates
- Add analytics tracking
- Optimize message timing
- A/B test different messages

---

**Need Help?**
- n8n Documentation: https://docs.n8n.io
- n8n Community: https://community.n8n.io
- Check backend logs: `PointOfSale.API/api.log`
- Check n8n executions: http://<n8n-host>:5678/executions


