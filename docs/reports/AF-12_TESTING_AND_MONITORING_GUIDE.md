# AF-12 Affiliate Mode — Testing & Monitoring Guide

**Date:** 2026-02-22  
**Status:** Ready for Implementation  
**Audience:** QA Engineers, DevOps, Backend Developers

---

## 📋 OVERVIEW

This guide provides comprehensive testing strategies and monitoring configurations for AF-12 Affiliate Mode. It covers integration tests, end-to-end tests, and production monitoring.

---

## 🧪 INTEGRATION TESTS

### Test Suite 1: Product Discovery Endpoint

**File:** `PointOfSale.Tests.Integration/Storefront/ProductDiscoveryAffiliateTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace PointOfSale.Tests.Integration.Storefront;

public class ProductDiscoveryAffiliateTests : IClassFixture<WebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Guid _tenantId;
    
    public ProductDiscoveryAffiliateTests(WebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _tenantId = TestData.DefaultTenantId;
    }
    
    [Fact]
    public async Task DiscoverProducts_WithValidRef_ReturnsOnlyAffiliateProducts()
    {
        // Arrange
        var validRefCode = await CreateTestAffiliateReferralAsync();
        
        // Act
        var response = await _client.GetAsync(
            $"/api/storefront/products/discover?ref={validRefCode}&q=laptop",
            headers: new { ["X-Tenant-Id"] = _tenantId.ToString() });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ProductDiscoveryResult>();
        result.Should().NotBeNull();
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(p => p.IsAffiliateEnabled == true);
        
        // Verify response headers
        response.Headers.Should().Contain(h => h.Key == "X-Affiliate-Mode" && h.Value.Contains("true"));
        response.Headers.Should().ContainKey("X-Affiliate-Account-Id");
    }
    
    [Fact]
    public async Task DiscoverProducts_WithoutRef_ReturnsAllProducts()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/storefront/products/discover?q=laptop",
            headers: new { ["X-Tenant-Id"] = _tenantId.ToString() });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ProductDiscoveryResult>();
        result.Should().NotBeNull();
        result.Items.Should().NotBeEmpty();
        
        // Should include both affiliate and non-affiliate products
        result.Items.Should().Contain(p => p.IsAffiliateEnabled == true);
        result.Items.Should().Contain(p => p.IsAffiliateEnabled == false);
        
        // Verify no affiliate headers
        response.Headers.Should().NotContainKey("X-Affiliate-Mode");
    }
    
    [Fact]
    public async Task DiscoverProducts_WithInvalidRef_ReturnsAllProducts()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/storefront/products/discover?ref=INVALID_CODE&q=laptop",
            headers: new { ["X-Tenant-Id"] = _tenantId.ToString() });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ProductDiscoveryResult>();
        result.Items.Should().Contain(p => p.IsAffiliateEnabled == false);
        
        // Verify no affiliate headers (invalid ref = no affiliate mode)
        response.Headers.Should().NotContainKey("X-Affiliate-Mode");
    }
    
    [Fact]
    public async Task DiscoverProducts_WithExpiredRef_ReturnsAllProducts()
    {
        // Arrange
        var expiredRefCode = await CreateExpiredAffiliateReferralAsync();
        
        // Act
        var response = await _client.GetAsync(
            $"/api/storefront/products/discover?ref={expiredRefCode}&q=laptop",
            headers: new { ["X-Tenant-Id"] = _tenantId.ToString() });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ProductDiscoveryResult>();
        result.Items.Should().Contain(p => p.IsAffiliateEnabled == false);
        
        // Verify no affiliate headers (expired ref = no affiliate mode)
        response.Headers.Should().NotContainKey("X-Affiliate-Mode");
    }
}
```

---

### Test Suite 2: Cart Validation

**File:** `PointOfSale.Tests.Integration/Storefront/CartAffiliateTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace PointOfSale.Tests.Integration.Storefront;

public class CartAffiliateTests : IClassFixture<WebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Guid _tenantId;
    
    public CartAffiliateTests(WebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _tenantId = TestData.DefaultTenantId;
    }
    
    [Fact]
    public async Task AddItem_AffiliateProduct_WithValidRef_Success()
    {
        // Arrange
        var validRefCode = await CreateTestAffiliateReferralAsync();
        var affiliateProduct = await CreateTestAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        var request = new AddCartItemRequest
        {
            ProductId = affiliateProduct.Id,
            Quantity = 1
        };
        
        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/storefront/cart/items?ref={validRefCode}",
            request,
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString()
            });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var cart = await response.Content.ReadFromJsonAsync<CartDto>();
        cart.Should().NotBeNull();
        cart.Items.Should().ContainSingle();
        cart.Items[0].ProductId.Should().Be(affiliateProduct.Id);
        
        // Verify response headers
        response.Headers.Should().Contain(h => h.Key == "X-Affiliate-Mode" && h.Value.Contains("true"));
    }
    
    [Fact]
    public async Task AddItem_NonAffiliateProduct_WithValidRef_Returns400()
    {
        // Arrange
        var validRefCode = await CreateTestAffiliateReferralAsync();
        var nonAffiliateProduct = await CreateTestNonAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        var request = new AddCartItemRequest
        {
            ProductId = nonAffiliateProduct.Id,
            Quantity = 1
        };
        
        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/storefront/cart/items?ref={validRefCode}",
            request,
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString()
            });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        error.Should().NotBeNull();
        error.ErrorCode.Should().Be("PRODUCT_NOT_ELIGIBLE");
        error.Message.Should().Contain("not available in affiliate mode");
    }
    
    [Fact]
    public async Task AddItem_NonAffiliateProduct_WithoutRef_Success()
    {
        // Arrange
        var nonAffiliateProduct = await CreateTestNonAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        var request = new AddCartItemRequest
        {
            ProductId = nonAffiliateProduct.Id,
            Quantity = 1
        };
        
        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/storefront/cart/items",
            request,
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString()
            });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var cart = await response.Content.ReadFromJsonAsync<CartDto>();
        cart.Items.Should().ContainSingle();
        cart.Items[0].ProductId.Should().Be(nonAffiliateProduct.Id);
    }
    
    [Fact]
    public async Task AddItem_MixedProducts_WithValidRef_OnlyAffiliateSucceeds()
    {
        // Arrange
        var validRefCode = await CreateTestAffiliateReferralAsync();
        var affiliateProduct = await CreateTestAffiliateProductAsync();
        var nonAffiliateProduct = await CreateTestNonAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        // Act 1: Add affiliate product (should succeed)
        var response1 = await _client.PostAsJsonAsync(
            $"/api/storefront/cart/items?ref={validRefCode}",
            new AddCartItemRequest { ProductId = affiliateProduct.Id, Quantity = 1 },
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString()
            });
        
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Act 2: Add non-affiliate product (should fail)
        var response2 = await _client.PostAsJsonAsync(
            $"/api/storefront/cart/items?ref={validRefCode}",
            new AddCartItemRequest { ProductId = nonAffiliateProduct.Id, Quantity = 1 },
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString()
            });
        
        response2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        // Assert: Cart should only contain affiliate product
        var cart = await GetCartAsync(sessionId);
        cart.Items.Should().ContainSingle();
        cart.Items[0].ProductId.Should().Be(affiliateProduct.Id);
    }
}
```

---

### Test Suite 3: Checkout Validation

**File:** `PointOfSale.Tests.Integration/Storefront/CheckoutAffiliateTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace PointOfSale.Tests.Integration.Storefront;

public class CheckoutAffiliateTests : IClassFixture<WebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly Guid _tenantId;
    
    public CheckoutAffiliateTests(WebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _tenantId = TestData.DefaultTenantId;
    }
    
    [Fact]
    public async Task Checkout_WithAffiliateProducts_WithValidRef_Success()
    {
        // Arrange
        var validRefCode = await CreateTestAffiliateReferralAsync();
        var affiliateProduct = await CreateTestAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        // Add affiliate product to cart
        await AddToCartAsync(sessionId, affiliateProduct.Id, validRefCode);
        
        var checkoutRequest = new CheckoutRequest
        {
            ShippingAddress = TestData.ValidShippingAddress,
            PaymentMethodId = TestData.ValidPaymentMethodId
        };
        
        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/storefront/checkout?ref={validRefCode}",
            checkoutRequest,
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString(),
                ["X-Request-Id"] = Guid.NewGuid().ToString()
            });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<CheckoutResult>();
        result.Should().NotBeNull();
        result.OrderId.Should().NotBeEmpty();
        result.Success.Should().BeTrue();
    }
    
    [Fact]
    public async Task Checkout_WithNonAffiliateProduct_WithValidRef_Returns400()
    {
        // Arrange
        var validRefCode = await CreateTestAffiliateReferralAsync();
        var nonAffiliateProduct = await CreateTestNonAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        // Manually inject non-affiliate product into cart (bypass validation)
        await InjectProductIntoCartViaRedisAsync(sessionId, nonAffiliateProduct.Id);
        
        var checkoutRequest = new CheckoutRequest
        {
            ShippingAddress = TestData.ValidShippingAddress,
            PaymentMethodId = TestData.ValidPaymentMethodId
        };
        
        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/storefront/checkout?ref={validRefCode}",
            checkoutRequest,
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString(),
                ["X-Request-Id"] = Guid.NewGuid().ToString()
            });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        error.Should().NotBeNull();
        error.ErrorCode.Should().Be("CHECKOUT_CART_INVALID");
        error.Message.Should().Contain("not eligible for affiliate checkout");
    }
    
    [Fact]
    public async Task Checkout_WithMixedProducts_WithValidRef_Returns400()
    {
        // Arrange
        var validRefCode = await CreateTestAffiliateReferralAsync();
        var affiliateProduct = await CreateTestAffiliateProductAsync();
        var nonAffiliateProduct = await CreateTestNonAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        // Add affiliate product normally
        await AddToCartAsync(sessionId, affiliateProduct.Id, validRefCode);
        
        // Manually inject non-affiliate product (bypass validation)
        await InjectProductIntoCartViaRedisAsync(sessionId, nonAffiliateProduct.Id);
        
        var checkoutRequest = new CheckoutRequest
        {
            ShippingAddress = TestData.ValidShippingAddress,
            PaymentMethodId = TestData.ValidPaymentMethodId
        };
        
        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/storefront/checkout?ref={validRefCode}",
            checkoutRequest,
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString(),
                ["X-Request-Id"] = Guid.NewGuid().ToString()
            });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        error.ErrorCode.Should().Be("CHECKOUT_CART_INVALID");
    }
    
    [Fact]
    public async Task Checkout_WithNonAffiliateProduct_WithoutRef_Success()
    {
        // Arrange
        var nonAffiliateProduct = await CreateTestNonAffiliateProductAsync();
        var sessionId = Guid.NewGuid();
        
        // Add non-affiliate product to cart (no ref = normal mode)
        await AddToCartAsync(sessionId, nonAffiliateProduct.Id, refCode: null);
        
        var checkoutRequest = new CheckoutRequest
        {
            ShippingAddress = TestData.ValidShippingAddress,
            PaymentMethodId = TestData.ValidPaymentMethodId
        };
        
        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/storefront/checkout",
            checkoutRequest,
            headers: new 
            { 
                ["X-Tenant-Id"] = _tenantId.ToString(),
                ["X-Session-Id"] = sessionId.ToString(),
                ["X-Request-Id"] = Guid.NewGuid().ToString()
            });
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<CheckoutResult>();
        result.Success.Should().BeTrue();
    }
}
```

---

## 📊 MONITORING & ALERTS

### Metrics to Monitor

#### 1. Affiliate Mode Activation Rate

**Metric:** `affiliate_mode_activations_total`

**Description:** Total number of valid affiliate referral activations

**Query (Prometheus):**
```promql
rate(affiliate_mode_activations_total[5m])
```

**Alert Threshold:** Alert if drops to 0 for > 1 hour (may indicate middleware issue)

---

#### 2. Affiliate Mode Request Rate

**Metric:** `affiliate_mode_requests_total`

**Description:** Total number of storefront requests in affiliate mode

**Query (Prometheus):**
```promql
rate(affiliate_mode_requests_total[5m])
```

**Alert Threshold:** Alert if drops significantly (> 50% decrease)

---

#### 3. Blocked Product Access Rate

**Metric:** `affiliate_mode_blocked_products_total`

**Description:** Number of times non-affiliate products were blocked

**Query (Prometheus):**
```promql
rate(affiliate_mode_blocked_products_total[1h])
```

**Alert Thresholds:**
- ⚠️ Warning: > 50 blocks/hour (may indicate attack or misconfiguration)
- 🚨 Critical: > 100 blocks/hour (likely attack or major issue)

---

#### 4. Cart Validation Failures

**Metric:** `cart_validation_failures_total{reason="product_not_eligible"}`

**Description:** Number of cart add failures due to non-affiliate products

**Query (Prometheus):**
```promql
rate(cart_validation_failures_total{reason="product_not_eligible"}[5m])
```

**Alert Threshold:** Alert if > 10/minute (may indicate attack)

---

#### 5. Checkout Validation Failures

**Metric:** `checkout_failures_total{reason="affiliate_validation_failed"}`

**Description:** Number of checkout failures due to non-affiliate products in cart

**Query (Prometheus):**
```promql
rate(checkout_failures_total{reason="affiliate_validation_failed"}[5m])
```

**Alert Threshold:** Alert if > 5/minute (indicates bypass attempt or bug)

---

### Alert Configuration

#### Grafana Alert: High Blocked Product Rate

```yaml
name: AF-12 High Blocked Product Rate
condition: rate(affiliate_mode_blocked_products_total[1h]) > 100
for: 5m
severity: warning
annotations:
  summary: "High rate of blocked product access in Affiliate Mode"
  description: "{{ $value }} products blocked per hour. May indicate attack or misconfiguration."
  runbook: "https://wiki.company.com/runbooks/af12-high-blocked-rate"
```

#### Grafana Alert: Checkout Validation Failures

```yaml
name: AF-12 Checkout Validation Failures
condition: rate(checkout_failures_total{reason="affiliate_validation_failed"}[5m]) > 5
for: 2m
severity: critical
annotations:
  summary: "High rate of affiliate checkout validation failures"
  description: "{{ $value }} checkout failures per minute. Indicates bypass attempt or critical bug."
  runbook: "https://wiki.company.com/runbooks/af12-checkout-failures"
```

#### Grafana Alert: Affiliate Mode Activation Drop

```yaml
name: AF-12 Activation Rate Drop
condition: rate(affiliate_mode_activations_total[1h]) == 0
for: 1h
severity: warning
annotations:
  summary: "No affiliate mode activations in the last hour"
  description: "Affiliate referral middleware may be broken or no traffic."
  runbook: "https://wiki.company.com/runbooks/af12-no-activations"
```

---

### Grafana Dashboard

**Dashboard Name:** AF-12 Affiliate Mode Monitoring

**Panels:**

1. **Affiliate Mode Activation Rate** (Time Series)
   - Query: `rate(affiliate_mode_activations_total[5m])`
   - Unit: activations/sec

2. **Affiliate Mode Request Rate** (Time Series)
   - Query: `rate(affiliate_mode_requests_total[5m])`
   - Unit: requests/sec

3. **Blocked Product Access Rate** (Time Series)
   - Query: `rate(affiliate_mode_blocked_products_total[1h])`
   - Unit: blocks/hour
   - Threshold line at 100 (critical)

4. **Cart Validation Failures** (Time Series)
   - Query: `rate(cart_validation_failures_total{reason="product_not_eligible"}[5m])`
   - Unit: failures/sec

5. **Checkout Validation Failures** (Time Series)
   - Query: `rate(checkout_failures_total{reason="affiliate_validation_failed"}[5m])`
   - Unit: failures/sec

6. **Top Blocked Products** (Table)
   - Query: `topk(10, sum by (product_id) (affiliate_mode_blocked_products_total))`

7. **Affiliate Mode Success Rate** (Gauge)
   - Query: `(rate(affiliate_mode_requests_total[5m]) - rate(cart_validation_failures_total{reason="product_not_eligible"}[5m])) / rate(affiliate_mode_requests_total[5m]) * 100`
   - Unit: %
   - Thresholds: Green > 95%, Yellow 90-95%, Red < 90%

---

## 🚨 INCIDENT RESPONSE

### Scenario 1: High Blocked Product Rate

**Symptoms:**
- `affiliate_mode_blocked_products_total` > 100/hour
- Alert triggered

**Investigation Steps:**
1. Check if specific products are being blocked repeatedly
2. Verify products have `IsAffiliateEnabled = true` in MongoDB
3. Check if Elasticsearch index is out of sync
4. Review logs for suspicious patterns (same IP, same session)

**Resolution:**
- If attack: Block IP or session
- If misconfiguration: Update product `IsAffiliateEnabled` flag
- If index issue: Reindex Elasticsearch

---

### Scenario 2: Checkout Validation Failures

**Symptoms:**
- `checkout_failures_total{reason="affiliate_validation_failed"}` > 5/minute
- Alert triggered

**Investigation Steps:**
1. Check if cart validation is being bypassed
2. Review Redis cart data for suspicious entries
3. Check logs for cart injection attempts
4. Verify middleware is running correctly

**Resolution:**
- If bypass attempt: Investigate and patch vulnerability
- If Redis corruption: Clear affected carts
- If middleware issue: Restart service

---

### Scenario 3: No Affiliate Activations

**Symptoms:**
- `affiliate_mode_activations_total` = 0 for > 1 hour
- Alert triggered

**Investigation Steps:**
1. Check if `AffiliateReferralMiddleware` is registered
2. Verify referral codes exist in database
3. Check if traffic is reaching the service
4. Review middleware logs for errors

**Resolution:**
- If middleware not registered: Deploy fix
- If no referral codes: Create test codes
- If no traffic: Check load balancer/DNS

---

## 📈 SUCCESS METRICS

### Key Performance Indicators (KPIs)

1. **Affiliate Mode Activation Rate**
   - Target: > 0 activations/hour during business hours
   - Measure: `rate(affiliate_mode_activations_total[1h])`

2. **Affiliate Mode Success Rate**
   - Target: > 95% (< 5% blocked products)
   - Measure: `(requests - blocks) / requests * 100`

3. **Cart Validation Success Rate**
   - Target: > 99% (< 1% validation failures)
   - Measure: `(cart_adds - failures) / cart_adds * 100`

4. **Checkout Validation Success Rate**
   - Target: 100% (0 bypass attempts)
   - Measure: `checkout_failures{reason="affiliate_validation_failed"} == 0`

5. **Commission Attribution Accuracy**
   - Target: 100% (all affiliate orders attributed correctly)
   - Measure: Manual audit of orders with `?ref` parameter

---

## 🎯 TESTING SCHEDULE

### Pre-Deployment Testing
- [ ] Run all integration tests
- [ ] Run end-to-end tests
- [ ] Perform manual security testing
- [ ] Load test with affiliate mode enabled

### Post-Deployment Testing
- [ ] Smoke tests (first 1 hour)
- [ ] Monitor metrics (first 24 hours)
- [ ] Manual verification (first week)
- [ ] Commission audit (first month)

### Ongoing Testing
- [ ] Weekly: Review blocked product metrics
- [ ] Monthly: Audit affiliate commission attribution
- [ ] Quarterly: Security penetration testing

---

**Testing & Monitoring Guide Completed:** 2026-02-22  
**Next Action:** Implement integration tests and configure alerts  
**Estimated Time:** 8-12 hours for full implementation
