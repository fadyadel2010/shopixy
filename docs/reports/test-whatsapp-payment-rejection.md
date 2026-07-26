# WhatsApp Payment Rejection Notification - Test Guide

## Issue Found
The event is being published successfully but the consumer is not receiving it. This could be due to:
1. Consumer not properly registered after code changes
2. RabbitMQ binding not created correctly

## Solution
Restart the API to ensure the consumer is properly registered with RabbitMQ.

## Test Steps

### 1. Start the API
```powershell
cd PointOfSale.API
dotnet run
```

Wait for the API to fully start and see this log:
```
info: PointOfSale.Infrastructure.Consumers.WhatsApp.WhatsAppPaymentRejectedNotificationConsumer
```

### 2. Verify RabbitMQ Queue
```powershell
.\diagnose-whatsapp-payment-notifications.ps1
```

Look for:
- `whats-app-payment-rejected-notification` queue
- Consumer count should be 1

### 3. Test Payment Rejection

1. Go to POS Admin → Manual Payments
2. Find the pending payment
3. Click "Reject" and enter a reason
4. Submit

### 4. Check Logs

Watch for these log entries in sequence:

```
[WhatsApp Payment] ATTEMPTING to emit rejection notification
[WhatsApp Payment] Publishing rejection event to RabbitMQ
[WhatsApp Payment] Rejected notification event PUBLISHED SUCCESSFULLY
[WhatsApp Payment] *** CONSUMER INVOKED *** Processing rejection notification
[WhatsApp Payment] *** SERVICE METHOD INVOKED *** NotifyCustomerPaymentRejectedAsync
[WhatsApp Payment] Feature gate check result: True
[WhatsApp Payment] Active account lookup result: True
[WhatsApp Payment] Customer phone check | HasPhone: True
[WhatsApp Payment] Creating message log
[WhatsApp Payment] Message log created: xxx | Dispatching message
[WhatsApp Payment] Customer notified (rejected)
```

### 5. Verify WhatsApp Message

Check the customer's WhatsApp for the rejection message:
```
❌ *Payment Rejected*

Order #ORD-8A9F-2026-00075

Reason: Please Resend the receipt again.

Please upload a clearer payment proof.
```

## What Was Fixed

1. ✅ Added `use_for_payment_approval` column to database
2. ✅ Enabled feature for all active WhatsApp accounts
3. ✅ Added comprehensive logging throughout the flow
4. ✅ Verified RabbitMQ queues and consumers are registered
5. ✅ Consumer registration in Program.cs is correct

## If Still Not Working

### Check Consumer Registration
Look for this in startup logs:
```
info: MassTransit[0]Usage Telemetry: ...
"whats-app-payment-rejected-notification","type":"RabbitMQ","consumer_count":1
```

### Check Feature Gate
Run this SQL:
```sql
SELECT id, phone_number, use_for_payment_approval, enabled 
FROM whatsapp_accounts 
WHERE "IsDeleted" = false;
```

Both accounts should have `use_for_payment_approval = true`.

### Check Customer Phone
Run this SQL:
```sql
SELECT "Id", "OrderNumber", "GuestPhone", "GuestName"
FROM "Orders"
WHERE "Id" = '<order-id>';
```

`GuestPhone` must not be null or empty.

### Manual Event Test
If you want to test the consumer directly, you can publish a test event from RabbitMQ Management UI:
1. Go to http://<rabbitmq-management-host>:15672
2. Login using the rotated RabbitMQ credentials from your secret store
3. Go to Queues → `whats-app-payment-rejected-notification`
4. Publish a test message

## Expected Behavior

After restart, when you reject a payment:
1. Event is published to RabbitMQ
2. Consumer receives event immediately
3. Service checks feature gate (should be enabled)
4. Service checks for active WhatsApp account (should find one)
5. Service checks customer phone (should be present)
6. Message log is created in MongoDB
7. Message is dispatched to Baileys
8. Customer receives WhatsApp message

The entire flow should complete in < 1 second.

