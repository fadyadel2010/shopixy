using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests.Shipping;

public sealed class ManualShippingHttpEndToEndTests
{
    [Fact]
    public async Task RealHttpCheckout_PersistsSelectedManualMethod_SeparateAddresses_ReplayAndFulfillmentOutbox()
    {
        var connection = await ShippingIsolatedPostgres.VerifyAsync("SHIPPING_MANUAL_TEST_CONNECTION", ManualShippingRuntimeFixtureTests.DatabaseName);
        var tenant = new TenantContext(); tenant.SetTenantId(ManualShippingRuntimeFixtureTests.TenantId);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, tenant);
        Assert.True(await db.Tenants.AnyAsync(t => t.Id == ManualShippingRuntimeFixtureTests.TenantId && t.Slug == "shipping-manual"));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri("http://127.0.0.1:5018"), Timeout = TimeSpan.FromSeconds(40) };
        var guestCookies = new Dictionary<string, string>();
        http.DefaultRequestHeaders.Host = "shipping-manual.localhost:5184";
        http.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());
        var cart = await Send(HttpMethod.Post, "/api/storefront/cart/items", new { productId = ManualShippingRuntimeFixtureTests.ProductId,
            variantId = ManualShippingRuntimeFixtureTests.VariantId, quantity = 1 });
        var cartId = cart["cartId"]!.GetValue<Guid>();
        var session = await Send(HttpMethod.Post, "/api/storefront/checkout-session", new { cartId });
        var sessionId = session["sessionId"]!.GetValue<Guid>();
        var preview = await Send(HttpMethod.Post, "/api/storefront/shipping-methods/preview", new { checkoutSessionId = sessionId });
        Assert.Equal("AVAILABLE", preview["shippingMethodsStatus"]!.GetValue<string>());
        Assert.Equal(2, preview["shippingMethods"]!.AsArray().Count);
        Assert.Null(preview["warehouseResolutionId"]);
        await Send(HttpMethod.Post, "/api/storefront/shipping-methods/select", new { checkoutSessionId = sessionId, shippingMethodId = Guid.NewGuid() }, accepted: false);
        var selection = await Send(HttpMethod.Post, "/api/storefront/shipping-methods/select", new { checkoutSessionId = sessionId, shippingMethodId = ManualShippingRuntimeFixtureTests.MethodId });
        Assert.Equal(ManualShippingRuntimeFixtureTests.MethodId, selection["shippingMethodId"]!.GetValue<Guid>());
        Assert.Equal("RESOLVED", selection["status"]!.GetValue<string>());
        var shipping = new { name = "Synthetic HTTP Recipient", phone = "15555550100", email = "http-recipient@example.invalid", street = "20 Synthetic Street",
            street2 = "Suite 2", city = "Memphis", state = "TN", zipCode = "38103", country = "US", building = "B", floor = "2", apartment = "4", landmark = "Synthetic landmark" };
        var billing = new { name = "Synthetic Billing Recipient", phone = "15555550101", email = "billing@example.invalid", street = "30 Synthetic Billing Street",
            city = "Nashville", state = "TN", zipCode = "37201", country = "US" };
        var request = new { cartId, checkoutSessionId = sessionId, warehouseResolutionId = selection["warehouseResolutionId"]!.GetValue<Guid>(),
            warehouseResolutionVersion = selection["resolutionVersion"]!.GetValue<int>(), shippingMethodId = ManualShippingRuntimeFixtureTests.MethodId,
            shippingAddress = shipping, billingAddress = billing, paymentMethod = "COD",
            expectedPricing = new { subtotal = 100m, shipping = 25m, tax = 0m, discount = 0m, total = 125m } };
        http.DefaultRequestHeaders.Add("X-Request-Id", Guid.NewGuid().ToString());
        var response = await Send(HttpMethod.Post, "/api/storefront/checkout", request);
        var orderId = response["orderId"]!.GetValue<Guid>();
        Assert.Equal(125m, response["totalAmount"]!.GetValue<decimal>()); Assert.Equal("USD", response["currency"]!.GetValue<string>());
        var replay = await Send(HttpMethod.Post, "/api/storefront/checkout", request);
        Assert.Equal(orderId, replay["orderId"]!.GetValue<Guid>());
        // OrderCreated consumption closes the real checkout session asynchronously.
        // Verify that durable effect before testing the stale-session guard, rather
        // than racing its earlier consumed-resolution guard or accepting any 4xx.
        PointOfSale.Core.Entities.CheckoutSession? convertedSession = null;
        using (var conversionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (!conversionDeadline.IsCancellationRequested)
            {
                convertedSession = await db.CheckoutSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
                if (convertedSession.Status == CheckoutSessionStatus.ConvertedToOrder) break;
                await Task.Delay(100);
            }
        }
        Assert.NotNull(convertedSession);
        Assert.Equal(CheckoutSessionStatus.ConvertedToOrder, convertedSession.Status);
        Assert.Equal(orderId, convertedSession.ConvertedOrderId);
        var changed = System.Text.Json.JsonSerializer.SerializeToNode(request)!.AsObject(); changed["shippingAddress"]!["street"] = "Substituted Street";
        var substituted = await Send(HttpMethod.Post, "/api/storefront/checkout", changed, accepted: false);
        Assert.Equal("CHECKOUT_SESSION_STALE", substituted["code"]!.GetValue<string>());
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderId);
        Assert.Equal(1, await db.Orders.CountAsync(o => o.CartId == cartId));
        Assert.Equal(ManualShippingRuntimeFixtureTests.MethodId, order.ShippingMethodId);
        Assert.Equal(25m, order.ShippingCost); Assert.Equal(125m, order.Total); Assert.Equal("USD", order.Currency);
        Assert.Equal(FulfillmentMode.Manual, order.FulfillmentMode); Assert.Null(order.FulfillmentCarrierCode);
        Assert.Equal("20 Synthetic Street", order.ShippingStreet); Assert.Equal("30 Synthetic Billing Street", order.BillingStreet);
        Assert.Equal("Suite 2", order.ShippingStreet2); Assert.Equal("B", order.ShippingBuilding); Assert.Equal("2", order.ShippingFloor);
        Assert.Equal("4", order.ShippingApartment); Assert.Equal("Synthetic landmark", order.ShippingLandmark);
        Assert.Equal("Nashville", order.BillingCity); Assert.Equal("TN", order.BillingState); Assert.Equal("37201", order.BillingZipCode); Assert.Equal("US", order.BillingCountry);
        Assert.Equal(http.DefaultRequestHeaders.GetValues("X-Session-Id").Single(), order.SessionId);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus); Assert.False(order.IsPaid);
        Assert.Single(await db.MarketplaceEventOutboxMessages.Where(m => m.TenantId == order.TenantId && m.EntityId == order.Id && m.EventType == "OrderCreatedEvent").ToListAsync());

        // Genuine Storefront CQRS reread, with the original guest session and
        // server-issued token/cookie. A substituted guest must not gain access.
        Assert.Null(response["trackingToken"]); // Production transport keeps bearer material HttpOnly.
        Assert.Contains("storefront_guest_tracking_token", guestCookies.Keys);
        var tracked = await Send(HttpMethod.Get, $"/api/storefront/orders/{orderId}/track-guest");
        Assert.Equal(25m, tracked["shippingCost"]!.GetValue<decimal>());
        Assert.Equal(125m, tracked["total"]!.GetValue<decimal>());
        Assert.Equal("COD", tracked["paymentMethodCode"]!.GetValue<string>());
        var originalSession = http.DefaultRequestHeaders.GetValues("X-Session-Id").Single();
        http.DefaultRequestHeaders.Remove("X-Session-Id");
        http.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());
        var denied = await Send(HttpMethod.Get, $"/api/storefront/orders/{orderId}/track-guest", accepted: false);
        Assert.Equal("SESSION_MISMATCH", denied["code"]!.GetValue<string>());
        http.DefaultRequestHeaders.Remove("X-Session-Id"); http.DefaultRequestHeaders.Add("X-Session-Id", originalSession);

        // Authenticate the actual synthetic staff input through normal production
        // login, HttpOnly cookies and CSRF, never a test issuer or invented claims.
        var password = Environment.GetEnvironmentVariable("SHIPPING_MANUAL_POS_PASSWORD")
            ?? throw new InvalidOperationException("Private isolated staff password input required.");
        var login = await Send(HttpMethod.Post, "/api/auth/login", new { username = "shipping-isolated-operator", password });
        Assert.Equal(order.TenantId, login["user"]!["tenantId"]!.GetValue<Guid>());
        Assert.Contains("access_token", guestCookies.Keys); Assert.Contains("csrf_token", guestCookies.Keys);
        var detail = await Send(HttpMethod.Get, $"/api/admin/orders/{orderId}");
        Assert.Equal(25m, detail["totals"]!["shippingCost"]!.GetValue<decimal>());
        Assert.Equal(125m, detail["totals"]!["total"]!.GetValue<decimal>());
        var confirmed = await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/confirm", new { });
        var shipment = await Send(HttpMethod.Post, "/api/shipments", new { orderId, shippingMethodId = ManualShippingRuntimeFixtureTests.MethodId });
        var shipmentId = shipment["id"]!.GetValue<Guid>();
        await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/pack", new { packingSlipNumber = "SYNTHETIC-ONLY" });
        await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/out-for-delivery", new { shipmentId });
        var missingProof = await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/delivered", new { deliveryReference = "SYNTHETIC-ONLY", recipientName = shipping.name }, accepted: false);
        Assert.Equal("MANUAL_DELIVERY_EVIDENCE_REQUIRED", missingProof["code"]!.GetValue<string>());
        // Simulated local delivery evidence for this disposable order only;
        // neither this URL nor the transition asserts actual carrier delivery.
        var delivery = new { deliveryReference = "SYNTHETIC-ONLY", proofOfDeliveryUrl = "https://synthetic.example.invalid/manual-proof", recipientName = shipping.name };
        await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/delivered", delivery);
        await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/delivered", delivery);
        await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/delivered", new { deliveryReference = "SUBSTITUTED", delivery.proofOfDeliveryUrl }, accepted: false);
        await Send(HttpMethod.Post, $"/api/orders/{orderId}/fulfillment/complete", new { }, accepted: false);

        // Traverse real local shipment management too. The prior order-delivery
        // evidence remains authoritative; a shipment transition is not collection.
        http.DefaultRequestHeaders.Add("X-Tenant-Id", order.TenantId.ToString());
        foreach (var status in new[] { "ReadyForPickup", "PickedUp", "InTransit", "OutForDelivery", "Delivered" })
            await Send(HttpMethod.Put, $"/api/admin/shipments/{shipmentId}/status", new { newStatus = status, trackingNumber = "SYNTHETIC-LOCAL-ONLY", reason = "Isolated lifecycle verification" });
        var deliveredShipment = await Send(HttpMethod.Get, $"/api/admin/shipments/{shipmentId}");
        Assert.Equal("Delivered", deliveredShipment["status"]!.GetValue<string>());
        Assert.Equal(orderId, deliveredShipment["orderId"]!.GetValue<Guid>());
        Assert.Equal(25m, deliveredShipment["shippingCost"]!.GetValue<decimal>());
        Assert.False(deliveredShipment["codCollected"]!.GetValue<bool>());
        await Send(HttpMethod.Put, $"/api/admin/shipments/{shipmentId}/status", new { newStatus = "Delivered", reason = "Duplicate local delivery" });
        await Send(HttpMethod.Put, $"/api/admin/shipments/{shipmentId}/status", new { newStatus = "ReadyForPickup", reason = "Rejected regression" }, accepted: false);

        var mongoConnection = Environment.GetEnvironmentVariable("SHIPPING_MANUAL_MONGO_CONNECTION")
            ?? throw new InvalidOperationException("Explicit isolated Mongo connection required.");
        var collection = new MongoClient(mongoConnection).GetDatabase(ManualShippingRuntimeFixtureTests.RuntimeMongoDatabase).GetCollection<OrderDocument>("orders");
        OrderDocument? projection = null;
        using (var deliveryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (!deliveryDeadline.IsCancellationRequested)
            {
                projection = await collection.Find(o => o.OrderId == orderId && o.TenantId == order.TenantId).FirstOrDefaultAsync();
                var published = await db.MarketplaceEventOutboxMessages.AsNoTracking().CountAsync(m => m.TenantId == order.TenantId && m.EntityId == orderId &&
                    (m.EventType == "OrderCreatedEvent" || m.EventType == "OrderDeliveredEvent") && m.PublishedAtUtc != null && !m.IsFailed);
                if (projection?.ShippingStatus == "Delivered" && projection.Status == "Delivered" && published == 2) break;
                await Task.Delay(100);
            }
        }
        Assert.NotNull(projection);
        Assert.Equal("Delivered", projection.Status); Assert.Equal("Delivered", projection.ShippingStatus);
        Assert.Equal("Pending", projection.PaymentStatus); Assert.Equal("COD", projection.PaymentMethodCode);
        Assert.Equal(25m, projection.ShippingCost); Assert.Equal(125m, projection.Total); Assert.Equal("USD", projection.Currency);
        Assert.Equal(order.ShippingAddress, projection.ShippingAddress!.Street); // Served legacy line retains all structured address detail.
        var marketplaceMessages = await db.MarketplaceEventOutboxMessages.AsNoTracking().Where(m => m.TenantId == order.TenantId && m.EntityId == orderId &&
            (m.EventType == "OrderCreatedEvent" || m.EventType == "OrderDeliveredEvent")).ToListAsync();
        Assert.Equal(2, marketplaceMessages.Count);
        Assert.All(marketplaceMessages, m => { Assert.NotNull(m.PublishedAtUtc); Assert.False(m.IsFailed); });
        var adminReread = await Send(HttpMethod.Get, $"/api/admin/orders/{orderId}");
        Assert.Equal("Delivered", adminReread["status"]!.GetValue<string>());
        Assert.Equal(125m, adminReread["totals"]!["total"]!.GetValue<decimal>());
        guestCookies.Remove("access_token"); guestCookies.Remove("refresh_token"); guestCookies.Remove("csrf_token");
        http.DefaultRequestHeaders.Remove("X-CSRF-Token");
        RefreshCookieHeader();
        var guestDelivery = await Send(HttpMethod.Get, $"/api/storefront/orders/{orderId}/track-guest");
        Assert.Equal("Delivered", guestDelivery["shippingStatus"]!.GetValue<string>());
        Assert.Equal("Pending", guestDelivery["paymentStatus"]!.GetValue<string>());
        Assert.Equal("COD", guestDelivery["paymentMethodCode"]!.GetValue<string>());
        Assert.Equal(125m, guestDelivery["total"]!.GetValue<decimal>());
        db.ChangeTracker.Clear();
        order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(FulfillmentStatus.Delivered, order.FulfillmentStatus);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus); Assert.False(order.IsPaid);
        var storedShipment = await db.Shipments.SingleAsync(s => s.Id == shipmentId);
        Assert.Equal(25m, storedShipment.ShippingCost); Assert.Equal("manual", storedShipment.CarrierCode);
        Assert.Equal("20 Synthetic Street", storedShipment.DestinationStreet); Assert.Equal(shipping.email, storedShipment.RecipientEmail);
        Assert.False(storedShipment.CodCollected); Assert.Equal(0, storedShipment.ExternalRegistrationAttemptCount);
        Assert.Equal(ShipmentStatus.Delivered, storedShipment.Status);
        Assert.Equal(delivery.proofOfDeliveryUrl, order.ProofOfDeliveryUrl);
        Assert.Equal(1, await db.Shipments.CountAsync(s => s.OrderId == orderId));
        Assert.Empty(await db.AccountingOutboxMessages.Where(m => m.TenantId == order.TenantId && m.BusinessEntityId == orderId).ToListAsync());
        Assert.Empty(await db.JournalEntries.Where(j => j.TenantId == order.TenantId && j.SourceId == orderId).ToListAsync());

        async Task<JsonObject> Send(HttpMethod method, string path, object? payload = null, bool accepted = true)
        {
            using var message = new HttpRequestMessage(method, path);
            if (payload != null) message.Content = JsonContent.Create(payload);
            using var result = await http.SendAsync(message);
            // Test uses isolated loopback HTTP rather than production TLS. Carry
            // only real server-issued HttpOnly cookies; do not create a token or
            // remove the production cookie's Secure flag to make this test work.
            if (result.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';')[0].Split('=', 2);
                    if (pair.Length != 2 || pair[0] is not ("storefront_guest_tracking_token" or "storefront_guest_checkout_token" or "access_token" or "refresh_token" or "csrf_token")) continue;
                    if (pair[0] != "csrf_token") Assert.Contains("httponly", cookie.ToLowerInvariant());
                    Assert.Contains("secure", cookie.ToLowerInvariant());
                    guestCookies[pair[0]] = pair[1];
                }
                RefreshCookieHeader();
                if (guestCookies.TryGetValue("csrf_token", out var csrf))
                {
                    http.DefaultRequestHeaders.Remove("X-CSRF-Token");
                    http.DefaultRequestHeaders.Add("X-CSRF-Token", Uri.UnescapeDataString(csrf));
                }
            }
            var content = await result.Content.ReadAsStringAsync();
            var json = content.Length == 0 ? new JsonObject() : JsonNode.Parse(content)!.AsObject();
            Assert.True(accepted == result.IsSuccessStatusCode, $"{method} {path}: HTTP{(int)result.StatusCode}; code={json["code"] ?? json["errorCode"] ?? json["error"]}");
            if (!accepted) Assert.InRange((int)result.StatusCode, 400, 499);
            return json;
        }

        void RefreshCookieHeader()
        {
            http.DefaultRequestHeaders.Remove("Cookie");
            if (guestCookies.Count > 0) http.DefaultRequestHeaders.Add("Cookie", string.Join("; ", guestCookies.Select(p => $"{p.Key}={p.Value}")));
        }
    }
}
