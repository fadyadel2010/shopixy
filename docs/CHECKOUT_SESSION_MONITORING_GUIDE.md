# Checkout Session Monitoring Guide

## Overview

This guide provides monitoring, alerting, and operational procedures for checkout session token expiration, specifically focusing on the `RecoveryTokenTtlMinutes` configuration which controls the recovery link expiration window.

## Configuration Reference

### Current Settings (appsettings.json)

```json
"CheckoutSession": {
  "Security": {
    "RecoveryTokenTtlMinutes": 20,
    "PaymentTokenTtlMinutes": 20,
    "ManualWalletUploadTokenTtlMinutes": 20
  }
}
```

### Environment Variable Override

```bash
CheckoutSession__Security__RecoveryTokenTtlMinutes=20
```

## Critical Metrics to Monitor

### 1. SESSION_EXPIRED Error Rate

**Metric Name:** `checkout_session_expired_errors_total`

**Description:** Tracks the number of SESSION_EXPIRED errors returned when users attempt to use recovery links.

**Alert Threshold:**
- **Warning:** 10% increase above 7-day baseline
- **Critical:** 25% increase above 7-day baseline OR >50 occurrences in 15 minutes

**Query Examples:**

```promql
# Prometheus/Grafana
rate(checkout_session_expired_errors_total[5m]) > 0.5

# Count over time window
increase(checkout_session_expired_errors_total[15m]) > 50
```

### 2. Recovery Token Age at Use

**Metric Name:** `checkout_recovery_token_age_seconds`

**Description:** Histogram tracking how old recovery tokens are when users attempt to use them.

**Alert Threshold:**
- **Warning:** P95 > 1080 seconds (18 minutes) - users are cutting it close
- **Critical:** P50 > 900 seconds (15 minutes) - median user is in danger zone

**Query Examples:**

```promql
# P95 token age
histogram_quantile(0.95, rate(checkout_recovery_token_age_seconds_bucket[5m]))

# P50 token age
histogram_quantile(0.50, rate(checkout_recovery_token_age_seconds_bucket[5m]))
```

### 3. Recovery Link Click-Through Rate

**Metric Name:** `checkout_recovery_link_clicks_total` vs `checkout_recovery_link_sent_total`

**Description:** Ratio of recovery links clicked vs sent to detect delivery or UX issues.

**Alert Threshold:**
- **Warning:** Click-through rate drops below 30%
- **Critical:** Click-through rate drops below 15%

**Query Examples:**

```promql
# Click-through rate
(rate(checkout_recovery_link_clicks_total[1h]) / rate(checkout_recovery_link_sent_total[1h])) * 100 < 30
```

### 4. Recovery Link Success Rate

**Metric Name:** `checkout_recovery_success_total` vs `checkout_recovery_attempts_total`

**Description:** Percentage of recovery attempts that succeed (not expired, not invalid).

**Alert Threshold:**
- **Warning:** Success rate drops below 85%
- **Critical:** Success rate drops below 70%

**Query Examples:**

```promql
# Success rate
(rate(checkout_recovery_success_total[5m]) / rate(checkout_recovery_attempts_total[5m])) * 100 < 85
```

## Dashboard Requirements

### Checkout Session Health Dashboard

Create a dedicated dashboard with the following panels:

#### Panel 1: SESSION_EXPIRED Error Rate (Time Series)
- **Visualization:** Line graph
- **Metrics:** `rate(checkout_session_expired_errors_total[5m])`
- **Time Range:** Last 24 hours
- **Annotations:** Mark deployment times

#### Panel 2: Recovery Token Age Distribution (Heatmap)
- **Visualization:** Heatmap
- **Metrics:** `checkout_recovery_token_age_seconds_bucket`
- **Time Range:** Last 6 hours
- **Threshold Lines:** 10min (green), 15min (yellow), 18min (orange), 20min (red)

#### Panel 3: Recovery Flow Funnel (Stat Panels)
- **Metrics:**
  - Links Sent: `increase(checkout_recovery_link_sent_total[1h])`
  - Links Clicked: `increase(checkout_recovery_link_clicks_total[1h])`
  - Recovery Attempts: `increase(checkout_recovery_attempts_total[1h])`
  - Successful Recoveries: `increase(checkout_recovery_success_total[1h])`
  - Expired Errors: `increase(checkout_session_expired_errors_total[1h])`

#### Panel 4: Success Rate vs Baseline (Gauge)
- **Visualization:** Gauge
- **Metrics:** Current success rate vs 7-day average
- **Thresholds:** 
  - Green: >90%
  - Yellow: 85-90%
  - Orange: 70-85%
  - Red: <70%

#### Panel 5: Top Error Codes (Bar Chart)
- **Visualization:** Bar chart
- **Metrics:** All checkout session error codes by frequency
- **Time Range:** Last 1 hour

## Alert Configuration

### Alert 1: High SESSION_EXPIRED Rate

```yaml
alert: HighCheckoutSessionExpiredRate
expr: |
  (
    rate(checkout_session_expired_errors_total[15m]) 
    / 
    rate(checkout_session_expired_errors_total[15m] offset 7d)
  ) > 1.25
for: 5m
labels:
  severity: critical
  component: checkout
  team: backend
annotations:
  summary: "SESSION_EXPIRED errors increased by >25% vs baseline"
  description: |
    SESSION_EXPIRED error rate is {{ $value | humanizePercentage }} above 7-day baseline.
    Current rate: {{ $value }} errors/sec
    This may indicate the RecoveryTokenTtlMinutes (20min) is too short.
  runbook_url: "https://docs.internal/runbooks/checkout-session-expired"
  dashboard_url: "https://grafana.internal/d/checkout-health"
```

### Alert 2: Recovery Token Age Near Expiration

```yaml
alert: RecoveryTokenAgeNearExpiration
expr: |
  histogram_quantile(0.95, 
    rate(checkout_recovery_token_age_seconds_bucket[10m])
  ) > 1080
for: 10m
labels:
  severity: warning
  component: checkout
  team: backend
annotations:
  summary: "95% of recovery tokens are used within 18+ minutes (near 20min limit)"
  description: |
    P95 recovery token age: {{ $value | humanizeDuration }}
    Users are cutting it very close to the 20-minute expiration.
    Consider increasing RecoveryTokenTtlMinutes if this persists.
  runbook_url: "https://docs.internal/runbooks/checkout-session-expired"
```

### Alert 3: Low Recovery Success Rate

```yaml
alert: LowCheckoutRecoverySuccessRate
expr: |
  (
    rate(checkout_recovery_success_total[15m]) 
    / 
    rate(checkout_recovery_attempts_total[15m])
  ) * 100 < 70
for: 10m
labels:
  severity: critical
  component: checkout
  team: backend
annotations:
  summary: "Checkout recovery success rate dropped below 70%"
  description: |
    Only {{ $value | humanizePercentage }} of recovery attempts are succeeding.
    Check for SESSION_EXPIRED spikes, token generation issues, or email delivery delays.
  runbook_url: "https://docs.internal/runbooks/checkout-session-expired"
```

## Runbook: Responding to SESSION_EXPIRED Alerts

### Step 1: Assess Impact

1. **Check the dashboard:** Navigate to the Checkout Session Health Dashboard
2. **Quantify the spike:** Compare current error rate to 7-day baseline
3. **Check user impact:** Review customer support tickets for checkout complaints
4. **Identify timing:** Did this start after a recent deployment?

### Step 2: Investigate Root Cause

#### Scenario A: Email Delivery Delays

**Symptoms:**
- Recovery token age histogram shows most tokens used >15 minutes after creation
- Email delivery metrics show increased latency

**Actions:**
1. Check SMTP service health and queue depth
2. Review email provider (SendGrid, SES, etc.) delivery metrics
3. Check for spam/reputation issues

**Quick Fix:**
```bash
# Increase TTL temporarily via environment variable
kubectl set env deployment/pointofsale-api \
  CheckoutSession__Security__RecoveryTokenTtlMinutes=30
```

#### Scenario B: User Behavior Change

**Symptoms:**
- Gradual increase in token age over days/weeks
- No infrastructure changes
- Users report "taking time to gather payment info"

**Actions:**
1. Review user session analytics
2. Check if new payment methods were added (requiring more user steps)
3. Survey users about checkout flow friction

**Long-term Fix:**
- Increase `RecoveryTokenTtlMinutes` to 30-45 minutes
- Update configuration in `appsettings.Production.json` or environment config
- Deploy during low-traffic window

#### Scenario C: Configuration Rollback Needed

**Symptoms:**
- Spike immediately after deployment
- Token age distribution unchanged
- TTL was recently reduced from higher value

**Actions:**
1. **Immediate rollback:**
   ```bash
   # Revert to previous TTL value (e.g., 30 minutes)
   kubectl set env deployment/pointofsale-api \
     CheckoutSession__Security__RecoveryTokenTtlMinutes=30
   
   # Verify pods restarted
   kubectl rollout status deployment/pointofsale-api
   ```

2. **Verify recovery:**
   - Monitor SESSION_EXPIRED rate for 15 minutes
   - Check success rate returns to baseline
   - Review user feedback channels

3. **Post-incident:**
   - Document why 20 minutes was insufficient
   - Plan gradual reduction strategy if still desired
   - Update monitoring thresholds

### Step 3: Communication

**Internal:**
- Post incident update in #engineering-alerts
- Notify customer support team of potential user impact
- Update status page if widespread

**External (if severe):**
- Post status update: "Some users may experience expired checkout recovery links. We're investigating."
- Provide workaround: "If your checkout link expired, please restart checkout from the cart."

### Step 4: Post-Incident Review

1. **Document timeline:** When did alerts fire? When was it resolved?
2. **Calculate impact:** How many users affected? Revenue impact?
3. **Root cause:** Why did 20 minutes prove insufficient?
4. **Action items:**
   - Should TTL be permanently increased?
   - Do we need better email delivery monitoring?
   - Should we add client-side warnings at 15 minutes?

## Configuration Tuning Guidelines

### Determining Optimal TTL

1. **Analyze token age distribution:**
   ```promql
   # Get P50, P75, P95, P99 token ages
   histogram_quantile(0.50, rate(checkout_recovery_token_age_seconds_bucket[7d]))
   histogram_quantile(0.75, rate(checkout_recovery_token_age_seconds_bucket[7d]))
   histogram_quantile(0.95, rate(checkout_recovery_token_age_seconds_bucket[7d]))
   histogram_quantile(0.99, rate(checkout_recovery_token_age_seconds_bucket[7d]))
   ```

2. **Set TTL to cover P95 + buffer:**
   - If P95 = 12 minutes, set TTL to 20 minutes (67% buffer)
   - If P95 = 18 minutes, set TTL to 30 minutes (67% buffer)

3. **Balance security vs UX:**
   - **Shorter TTL (15-20 min):** Better security, higher expiration risk
   - **Longer TTL (30-45 min):** Better UX, slightly higher security risk
   - **Recommended:** 25-30 minutes for most use cases

### Gradual Rollout Strategy

When changing TTL:

1. **Test in staging:** Monitor for 48 hours with production-like traffic
2. **Deploy to 10% of production:** Use feature flag or canary deployment
3. **Monitor for 24 hours:** Check all metrics in dashboard
4. **Expand to 50%:** If no issues, expand rollout
5. **Full rollout:** Complete after 48 hours of stable metrics
6. **Monitor for 7 days:** Ensure no long-tail issues

## Related Configuration

### Other Checkout Session Timeouts

```json
"CheckoutSession": {
  "Security": {
    "RecoveryTokenTtlMinutes": 20,        // Recovery link expiration
    "PaymentTokenTtlMinutes": 20,         // Payment intent token expiration
    "ManualWalletUploadTokenTtlMinutes": 20  // Manual payment proof upload window
  }
}
```

**Note:** All three timeouts should generally be aligned. If users need more time for recovery, they likely need more time for payment upload as well.

## Code References

### Error Code Location
- **Controller:** `PointOfSale.API/Controllers/CheckoutSessionController.cs:431`
- **Error Code:** `SESSION_EXPIRED`
- **Frontend Handler:** `PointOfSale.Storefront/src/pages/RecoverCheckoutPage.tsx:33`

### Configuration Binding
- **Options Class:** `PointOfSale.Infrastructure/Checkout/CheckoutSessionSecurityOptions.cs`
- **Section Name:** `CheckoutSession:Security`

## Support Resources

- **Dashboard:** [Checkout Session Health](https://grafana.internal/d/checkout-health)
- **Logs:** `kubectl logs -l app=pointofsale-api --tail=100 | grep SESSION_EXPIRED`
- **Slack Channel:** #checkout-alerts
- **On-Call:** PagerDuty rotation "Backend - Checkout"

## Changelog

| Date | Change | Reason |
|------|--------|--------|
| 2026-04-21 | Initial TTL set to 20 minutes | Security hardening, reduce token lifetime |
| TBD | Monitor for 30 days | Assess if 20 minutes is sufficient for user behavior |
