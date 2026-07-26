# WhatsApp Admin API Reference

**Base URL:** `/api/admin/whatsapp`
**Authentication:** Required (Bearer Token)
**Roles:** `SuperAdmin`, `TenantAdmin`

---

## Accounts

### List Accounts
`GET /accounts`
Returns all WhatsApp accounts for the current tenant.

### Get Account
`GET /accounts/{id}`
Returns details for a specific account.

### Create Account
`POST /accounts`
**Body:**
```json
{
  "displayName": "Store WhatsApp",
  "phoneNumber": "+1234567890",
  "useForOrderConfirmation": true,
  "useForOtpVerification": false
}
```

### Update Account
`PUT /accounts/{id}`
**Body:** Same as Create Account.

### Delete Account
`DELETE /accounts/{id}`
Disconnects and deletes the account.

### Enable/Disable
- `POST /accounts/{id}/enable` - Enables messaging (must be Connected).
- `POST /accounts/{id}/disable` - Disables messaging.

---

## Connection & status

### Generate QR Code
`GET /accounts/{id}/qr`
Returns:
```json
{
  "qrCodeBase64": "data:image/png;base64,...",
  "expiresAtUtc": "2024-03-20T10:05:00Z"
}
```

### Check Status
`GET /accounts/{id}/status`
Returns:
```json
{
  "isConnected": true, // or false
  "status": "Connected" // Connected, Disconnected, PendingQR
}
```

### Disconnect
`POST /accounts/{id}/disconnect`
Forcefully disconnects the session from WhatsApp Web.

---

## Operational Safety (Kill Switch)

### Activate Kill Switch
`POST /killswitch/activate`
**Body:**
```json
{
  "reason": "Suspicious activity detected"
}
```
Instantly stops all WhatsApp messaging for the tenant.

### Deactivate Kill Switch
`POST /killswitch/deactivate`
Restores messaging functionality.

### Kill Switch Status
`GET /killswitch/status`
Returns:
```json
{
  "isKilled": true,
  "reason": "Suspicious activity detected"
}
```

---

## Message Templates

### List Templates
`GET /templates`
Returns all message templates for the current tenant.

### Get Template
`GET /templates/{id}`
Returns details for a specific template.

### Create Template
`POST /templates`
**Body:**
```json
{
  "type": "OrderConfirmation",
  "language": "en",
  "content": "📦 Hello {{customer_name}}, your order #{{order_number}} is confirmed!",
  "isActive": true
}
```

**Supported Variables:**
- `{{customer_name}}` - Customer's name
- `{{order_number}}` - Order display number
- `{{total}}` - Formatted total amount
- `{{currency}}` - Currency code
- `{{items_count}}` - Number of items
- `{{store_name}}` - Store name
- `{{otp_code}}` - OTP code (for OTP templates)

### Update Template
`PUT /templates/{id}`
**Body:** Same as Create Template.

### Delete Template
`DELETE /templates/{id}`
Deletes the template. System falls back to default template.

---

## Logs & Analytics

### Get Logs
`GET /logs?page=1&pageSize=20&status=FAILED`
Returns paginated message logs.

### Get Analytics
`GET /analytics?from=2024-01-01&to=2024-02-01`
Returns message counts and success rates.
