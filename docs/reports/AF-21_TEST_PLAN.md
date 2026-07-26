# AF-21: Test Plan

## 🧪 Test Scenarios

### TEST 1: Order Delivered Flow
**Objective:** Verify FulfillmentStatus updates when order is delivered

**Setup:**
1. Create affiliate account with referral code
2. Place order using affiliate link
3. Wait for commission to be earned (payment captured)

**Steps:**
1. Login to POS Admin
2. Navigate to Orders → Find the test order
3. Click "Mark as Delivered"
4. Wait 5 seconds for event processing

**Verification:**
1. Call `GET /api/affiliate/orders` with affiliate token
2. Find the test order in response
3. Verify `fulfillmentStatus = "Delivered"`
4. Verify `orderStatus` is populated
5. Verify `paymentMethod` is populated
6. Verify `paidAmount` matches order total

**Expected Result:** ✅ All fields populated correctly

**MongoDB Verification:**
```javascript
db.affiliate_orders.findOne({ orderNumber: "ORD-XXX" })
// Should show fulfillmentStatus: "Delivered"
```

---

### TEST 2: Order Completed Flow
**Objective:** Verify both OrderStatus and FulfillmentStatus update on completion

**Setup:**
1. Use order from TEST 1 (already delivered)

**Steps:**
1. In POS Admin, mark order as "Completed"
2. Wait 5 seconds for event processing

**Verification:**
1. Call `GET /api/affiliate/orders`
2. Verify `orderStatus = "Completed"`
3. Verify `fulfillmentStatus = "Completed"`
4. Verify commission status unchanged (still "Approved" or "Paid")

**Expected Result:** ✅ Order lifecycle status updated, commission status independent

---

### TEST 3: Order Cancelled Flow
**Objective:** Verify cancellation updates both statuses

**Setup:**
1. Create new affiliate order (pending state)

**Steps:**
1. In POS Admin, cancel the order
2. Wait 5 seconds for event processing

**Verification:**
1. Call `GET /api/affiliate/orders`
2. Verify `orderStatus = "Cancelled"`
3. Verify `fulfillmentStatus = "Cancelled"`
4. If commission was earned, verify reversal (separate flow)

**Expected Result:** ✅ Both statuses show "Cancelled"

---

### TEST 4: Multi-Store Affiliate
**Objective:** Verify tenant isolation and global affiliate queries

**Setup:**
1. Create affiliate account (global, no tenant)
2. Create Store A (TenantId = A)
3. Create Store B (TenantId = B)
4. Place order in Store A using affiliate link
5. Place order in Store B using same affiliate link

**Steps:**
1. Mark Store A order as "Delivered"
2. Mark Store B order as "Completed"
3. Wait 5 seconds for event processing

**Verification:**
1. Call `GET /api/affiliate/orders` with affiliate token
2. Verify response contains BOTH orders
3. Verify Store A order shows `fulfillmentStatus = "Delivered"`
4. Verify Store B order shows `fulfillmentStatus = "Completed"`
5. Verify no cross-contamination (Store A status ≠ Store B status)

**Expected Result:** ✅ Both orders visible with correct independent statuses

**MongoDB Verification:**
```javascript
db.affiliate_orders.find({ 
  affiliateAccountId: ObjectId("...") 
}).forEach(doc => {
  print(`Order: ${doc.orderNumber}, Tenant: ${doc.tenantId}, Status: ${doc.fulfillmentStatus}`)
})
// Should show 2 orders with different tenantIds and statuses
```

---

### TEST 5: Tenant Safety (Admin View)
**Objective:** Verify admin can only see their tenant's orders

**Setup:**
1. Use multi-store setup from TEST 4

**Steps:**
1. Login to Admin Portal as Store A admin
2. Navigate to Affiliate Orders view (if exists)
3. Verify only Store A orders visible

**Verification:**
1. Admin API should filter by TenantId
2. Store A admin should NOT see Store B orders
3. Affiliate portal (no tenant filter) should see both

**Expected Result:** ✅ Tenant isolation enforced

---

### TEST 6: Payment Method Stamping
**Objective:** Verify PaymentMethod is captured correctly

**Setup:**
1. Create 3 orders with different payment methods:
   - Order 1: COD
   - Order 2: Card (Stripe/Paymob)
   - Order 3: Wallet

**Steps:**
1. Place orders using affiliate link
2. Wait for commission events
3. Wait 5 seconds for order created events

**Verification:**
1. Call `GET /api/affiliate/orders`
2. Verify Order 1 shows `paymentMethod = "COD"`
3. Verify Order 2 shows `paymentMethod = "Card"` or gateway name
4. Verify Order 3 shows `paymentMethod = "Wallet"`

**Expected Result:** ✅ Payment methods correctly identified

---

### TEST 7: Backfill Existing Orders
**Objective:** Verify backfill script populates historical data

**Setup:**
1. Identify existing affiliate orders in production (before AF-21)
2. Note their OrderIds

**Steps:**
1. Run backfill script: `dotnet run -- affiliate:backfill-order-status`
2. Wait for completion
3. Check logs for success count

**Verification:**
1. Query MongoDB for historical orders
2. Verify `orderStatus` is populated
3. Verify `fulfillmentStatus` is populated
4. Verify `paymentMethod` is populated
5. Verify `paidAmount` is populated

**MongoDB Verification:**
```javascript
// Before backfill
db.affiliate_orders.countDocuments({ orderStatus: { $exists: false } })
// Should be > 0

// After backfill
db.affiliate_orders.countDocuments({ orderStatus: { $exists: false } })
// Should be 0

db.affiliate_orders.countDocuments({ orderStatus: { $exists: true, $ne: "" } })
// Should equal total affiliate orders
```

**Expected Result:** ✅ All historical orders have lifecycle data

---

### TEST 8: Idempotency
**Objective:** Verify duplicate events don't cause issues

**Setup:**
1. Create affiliate order
2. Mark as delivered

**Steps:**
1. Manually publish duplicate `FulfillmentDeliveredEvent` (same EventId)
2. Wait 5 seconds
3. Check MongoDB document

**Verification:**
1. Verify `fulfillmentStatus = "Delivered"` (correct)
2. Verify `processedEventIds` contains EventId only ONCE
3. Verify no duplicate updates in logs

**Expected Result:** ✅ Duplicate event ignored gracefully

---

### TEST 9: Event Ordering
**Objective:** Verify out-of-order events handled correctly

**Setup:**
1. Create affiliate order

**Steps:**
1. Publish `OrderCompletedEvent` BEFORE `FulfillmentDeliveredEvent`
2. Wait 5 seconds
3. Publish `FulfillmentDeliveredEvent`
4. Wait 5 seconds

**Verification:**
1. Verify final state is correct (both events processed)
2. Verify `fulfillmentStatus = "Delivered"` (last event wins)
3. Verify both EventIds in `processedEventIds`

**Expected Result:** ✅ Last event wins, no data corruption

---

### TEST 10: Performance (Load Test)
**Objective:** Verify consumer handles high event volume

**Setup:**
1. Create 1000 affiliate orders
2. Prepare to mark all as delivered simultaneously

**Steps:**
1. Trigger bulk delivery (via script or admin bulk action)
2. Monitor RabbitMQ queue depth
3. Monitor consumer logs
4. Wait for all events to process

**Verification:**
1. Verify all 1000 orders updated in MongoDB
2. Verify no errors in consumer logs
3. Verify event processing lag < 30 seconds
4. Verify no memory leaks or performance degradation

**Expected Result:** ✅ All orders processed successfully within acceptable time

---

## 🔍 Verification Checklist

After running all tests, verify:

- [ ] No SQL queries in affiliate portal (check logs)
- [ ] No cross-tenant data leakage (TEST 4, TEST 5)
- [ ] All order statuses populated (TEST 1-3, TEST 7)
- [ ] Payment methods captured (TEST 6)
- [ ] Idempotency works (TEST 8)
- [ ] Event ordering handled (TEST 9)
- [ ] Performance acceptable (TEST 10)
- [ ] No errors in consumer logs
- [ ] RabbitMQ queues healthy
- [ ] MongoDB indexes efficient

## 📊 Success Metrics

| Metric | Target | Actual |
|--------|--------|--------|
| Event processing lag | < 5 seconds | ___ |
| Consumer error rate | 0% | ___ |
| MongoDB update success | 100% | ___ |
| API response time | < 200ms | ___ |
| Backfill success rate | 100% | ___ |
| Cross-tenant leaks | 0 | ___ |

## 🚨 Failure Scenarios

### Scenario: Consumer Crashes
**Symptom:** Events published but MongoDB not updated  
**Debug:**
1. Check consumer logs for exceptions
2. Check RabbitMQ dead letter queue
3. Verify consumer is registered in Program.cs
4. Restart application

### Scenario: Wrong Tenant Data Visible
**Symptom:** Affiliate sees orders from wrong store  
**Debug:**
1. Check MongoDB documents for TenantId
2. Verify consumer filters by TenantId
3. Check event TenantId values
4. Review affiliate portal query (should NOT filter by tenant)

### Scenario: Empty Status Fields
**Symptom:** New fields are empty strings  
**Debug:**
1. Check if backfill ran
2. Verify events are being published
3. Check consumer is processing events
4. Verify MongoDB connection

### Scenario: Duplicate Updates
**Symptom:** Same order updated multiple times  
**Debug:**
1. Check ProcessedEventIds in MongoDB
2. Verify EventId is unique per event
3. Check for duplicate event publishing
4. Review idempotency logic

## 📝 Test Report Template

```markdown
# AF-21 Test Report

**Date:** ___________
**Tester:** ___________
**Environment:** Production / Staging / Local

## Test Results

| Test | Status | Notes |
|------|--------|-------|
| TEST 1: Order Delivered | ✅ / ❌ | |
| TEST 2: Order Completed | ✅ / ❌ | |
| TEST 3: Order Cancelled | ✅ / ❌ | |
| TEST 4: Multi-Store | ✅ / ❌ | |
| TEST 5: Tenant Safety | ✅ / ❌ | |
| TEST 6: Payment Method | ✅ / ❌ | |
| TEST 7: Backfill | ✅ / ❌ | |
| TEST 8: Idempotency | ✅ / ❌ | |
| TEST 9: Event Ordering | ✅ / ❌ | |
| TEST 10: Performance | ✅ / ❌ | |

## Issues Found

1. ___________
2. ___________

## Recommendations

1. ___________
2. ___________

## Sign-off

- [ ] All tests passed
- [ ] No critical issues
- [ ] Ready for production

**Approved by:** ___________
**Date:** ___________
```

---

**Test Duration:** ~2 hours (manual) or ~30 minutes (automated)  
**Prerequisites:** Staging environment with test data  
**Tools:** Postman, MongoDB Compass, RabbitMQ Management UI
