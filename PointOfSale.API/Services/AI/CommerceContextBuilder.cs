using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.Core.Helpers;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Services.AI;

/// <summary>
/// Builds the commerce context for the AI assistant.
/// This assembles ALL data the AI is allowed to use - nothing else.
/// 
/// ARCHITECTURAL BOUNDARY:
/// - This builder is the ONLY source of context for the AI
/// - The AI MUST NOT invent any data outside this context
/// - All data comes from existing services (cart, products, shipping, store config)
/// </summary>
public class CommerceContextBuilder : ICommerceContextBuilder
{
    private readonly IProductReadRepository _productRepository;
    private readonly IShippingMethodService _shippingMethodService;
    private readonly ICartService _cartService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoDatabase _mongoDatabase;
    private readonly CommerceAIOptions _options;
    private readonly ILogger<CommerceContextBuilder> _logger;

    public CommerceContextBuilder(
        IProductReadRepository productRepository,
        IShippingMethodService shippingMethodService,
        ICartService cartService,
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        IOptions<CommerceAIOptions> options,
        ILogger<CommerceContextBuilder> logger)
    {
        _productRepository = productRepository;
        _shippingMethodService = shippingMethodService;
        _cartService = cartService;
        _dbContext = dbContext;
        _mongoDatabase = mongoDatabase;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommerceContext> BuildAsync(
        Guid tenantId,
        string message,
        Guid? customerId = null,
        List<Guid>? productIdsInView = null,
        string? cartSessionId = null)
    {
        var correlationId = Guid.NewGuid().ToString();
        var sanitizedMessage = SanitizePromptText(message);

        _logger.LogDebug(
            "Building commerce context | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductsInView: {ProductCount} | CorrelationId: {CorrelationId}",
            tenantId, customerId, productIdsInView?.Count ?? 0, correlationId);

        var context = new CommerceContext
        {
            TenantId = tenantId,
            Message = sanitizedMessage
        };

        // PHASE 6.5: Detect if this is a post-purchase inquiry
        var isPostPurchaseInquiry = DetectPostPurchaseIntent(sanitizedMessage);

        // Kick off storefront config fetch as a fire-and-forget task to run in parallel
        var storefrontTask = GetStorefrontConfigAsync(tenantId);

        // Build all context parts in parallel
        var tasks = new List<Task>();

        // 1. Shop Context (will await storefrontTask internally)
        var shopTask = BuildShopContextAsync(tenantId, storefrontTask, correlationId);
        tasks.Add(shopTask);

        // 2. Products Context (from cart + visible products)
        var productsTask = BuildProductsContextAsync(storefrontTask, tenantId, customerId, productIdsInView, cartSessionId, correlationId);
        tasks.Add(productsTask);

        // 3. Shipping Context (needs cart for weight/subtotal)
        var shippingTask = BuildShippingContextAsync(tenantId, customerId, cartSessionId, correlationId);
        tasks.Add(shippingTask);

        // 4. Order Draft Context (current cart)
        var orderDraftTask = BuildOrderDraftContextAsync(tenantId, customerId, cartSessionId, correlationId);
        tasks.Add(orderDraftTask);

        // 5. Customer Context
        var customerTask = BuildCustomerContextAsync(tenantId, customerId);
        tasks.Add(customerTask);

        // PHASE 6.5: 6. Order & Shipment Context (if post-purchase inquiry)
        Task<OrderTrackingContext?>? orderTask = null;
        Task<ShipmentTrackingContext?>? shipmentTask = null;
        Task<HistoricalContext?>? historicalTask = null;

        if (isPostPurchaseInquiry && customerId.HasValue)
        {
            orderTask = BuildOrderTrackingContextAsync(tenantId, customerId.Value, sanitizedMessage, correlationId);
            tasks.Add(orderTask);
        }

        await Task.WhenAll(tasks);

        context.Shop = await shopTask;
        context.Products = await productsTask;
        context.Shipping = await shippingTask;
        context.OrderDraft = await orderDraftTask;
        context.Customer = await customerTask;
        
        // Phase 8: Set personality from the storefront config (await the task with error handling)
        try
        {
            var storefrontConfig = await storefrontTask;
            if (storefrontConfig != null && !string.IsNullOrWhiteSpace(storefrontConfig.CommerceAiPersonality))
            {
                context.Personality = storefrontConfig.CommerceAiPersonality;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load storefront config for personality | TenantId: {TenantId}", tenantId);
            // Continue with default personality - best-effort behavior
        }

        // PHASE 6.5: Populate order/shipment/historical context
        if (orderTask != null)
        {
            context.Order = await orderTask;

            // If order found, load shipment
            if (context.Order != null)
            {
                shipmentTask = BuildShipmentTrackingContextAsync(tenantId, context.Order.Id, correlationId);
                context.Shipment = await shipmentTask;

                // Build historical context with confidence
                historicalTask = BuildHistoricalContextAsync(context.Order, context.Shipment);
                context.Historical = await historicalTask;
            }
        }

        _logger.LogInformation(
            "Commerce context built | TenantId: {TenantId} | Personality: {Personality} | Products: {ProductCount} | ShippingMethods: {ShippingCount} | HasDraft: {HasDraft} | HasOrder: {HasOrder} | CorrelationId: {CorrelationId}",
            tenantId, context.Personality, context.Products.Count, context.Shipping.Methods.Count, context.OrderDraft.Exists, context.Order != null, correlationId);

        return context;
    }

    private static string SanitizePromptText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var index = 0;

        foreach (var character in value)
        {
            if (char.IsControl(character) && character != '\n' && character != '\r' && character != '\t')
            {
                continue;
            }

            buffer[index++] = character;
        }

        return new string(buffer, 0, index).Trim();
    }

    /// <summary>
    /// PHASE 6.5: Detect if message is a post-purchase inquiry.
    /// </summary>
    private bool DetectPostPurchaseIntent(string message)
    {
        var lowerMessage = message.ToLowerInvariant();

        var postPurchaseKeywords = new[]
        {
            "order", "track", "delivery", "shipped", "shipment",
            "where is", "when will", "status", "arrive",
            "طلب", "تتبع", "توصيل", "شحن", "وصول" // Arabic keywords
        };

        return postPurchaseKeywords.Any(keyword => lowerMessage.Contains(keyword));
    }

    /// <summary>
    /// Build shop context from store config.
    /// Awaits the storefront task with error handling to ensure transient failures don't break the entire context build.
    /// </summary>
    private async Task<ShopContext> BuildShopContextAsync(Guid tenantId, Task<StorefrontConfigDocument?> storefrontTask, string correlationId)
    {
        StorefrontConfigDocument? storeConfig = null;
        
        try
        {
            storeConfig = await storefrontTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load storefront config for shop context | CorrelationId: {CorrelationId}", correlationId);
            // Continue with defaults - best-effort behavior
        }

        // Best-effort tenant country lookup
        string? tenantCountry = null;
        try
        {
            tenantCountry = await GetTenantCountryCodeAsync(tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tenant country code for shop context | TenantId: {TenantId} | CorrelationId: {CorrelationId}", tenantId, correlationId);
            // Fall back to safe default via CountryCodeHelper
            tenantCountry = CountryCodeHelper.NormalizeOrDefault(null);
        }

        var shopContext = new ShopContext
        {
            Name = storeConfig?.StoreName ?? "Store",
            Currency = !string.IsNullOrWhiteSpace(storeConfig?.Currency) ? storeConfig.Currency : null,
            Country = tenantCountry,
            Policies = new PoliciesContext
            {
                // These would come from CMS pages or separate config
                Returns = null,
                Delivery = null
            }
        };

        return shopContext;
    }

    private async Task<string> GetTenantCountryCodeAsync(Guid tenantId)
    {
        var tenantCountryCode = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Country)
            .FirstOrDefaultAsync();

        return CountryCodeHelper.NormalizeOrDefault(tenantCountryCode);
    }

    private async Task<StorefrontConfigDocument?> GetStorefrontConfigAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.StoreConfigs
            .AsNoTracking()
            .Where(config => config.TenantId == tenantId)
            .Join(
                _dbContext.Tenants.AsNoTracking(),
                config => config.TenantId,
                tenant => tenant.Id,
                (config, tenant) => new StorefrontConfigDocument
                {
                    Id = config.Id.ToString(),
                    TenantId = config.TenantId,
                    StoreName = config.StoreName,
                    Currency = tenant.Currency,
                    CommerceAiPersonality = config.CommerceAiPersonality
                })
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Build products context from cart items + visible products.
    /// </summary>
    private async Task<List<ProductContext>> BuildProductsContextAsync(
        Task<StorefrontConfigDocument?> storefrontTask,
        Guid tenantId,
        Guid? customerId,
        List<Guid>? productIdsInView,
        string? cartSessionId,
        string correlationId)
    {
        var products = new List<ProductContext>();
        var productIdsToLoad = new HashSet<Guid>();

        // Get currency from storefront (non-blocking - failure doesn't prevent product loading)
        string? currency = null;
        try
        {
            var storefront = await storefrontTask;
            currency = !string.IsNullOrWhiteSpace(storefront?.Currency) ? storefront.Currency : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load storefront for currency | TenantId: {TenantId} | Continuing without currency", tenantId);
        }

        try
        {
            // Get product IDs from cart
            var cart = await GetCartAsync(tenantId, customerId, cartSessionId, correlationId);
            if (cart?.Items != null)
            {
                foreach (var item in cart.Items)
                {
                    productIdsToLoad.Add(item.ProductId);
                }
            }

            // Add visible product IDs
            if (productIdsInView != null)
            {
                foreach (var id in productIdsInView.Take(_options.MaxProductsInContext))
                {
                    productIdsToLoad.Add(id);
                }
            }

            // Load products from MongoDB
            if (productIdsToLoad.Count > 0)
            {
                var productDocs = await _productRepository.GetByProductIdsAsync(
                    tenantId, 
                    productIdsToLoad.Take(_options.MaxProductsInContext).ToList());

                foreach (var doc in productDocs)
                {
                    products.Add(MapProductToContext(doc, currency));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load products for context | TenantId: {TenantId} | CorrelationId: {CorrelationId}", tenantId, correlationId);
        }

        return products;
    }

    /// <summary>
    /// Map a product document to context model.
    /// </summary>
    private ProductContext MapProductToContext(ProductDocument doc, string? currency)
    {
        var context = new ProductContext
        {
            Id = doc.ProductId,
            Name = doc.Name,
            NameAr = doc.NameAr,
            Price = doc.HasVariants ? null : doc.EffectivePrice,
            Currency = currency, // Use shop-level currency (null if not configured)
            InStock = doc.InStock,
            Description = doc.Description,
            ShippingEligible = true, // Default to true
            HasVariants = doc.HasVariants
        };

        // Map variants if present
        if (doc.Variants != null && doc.Variants.Count > 0)
        {
            context.Variants = doc.Variants
                .Where(v => v.IsActive)
                .Select(v => new VariantContext
                {
                    Id = v.VariantId,
                    // Build name from attributes (e.g., "Red / XL")
                    Name = v.Attributes?.Count > 0 
                        ? string.Join(" / ", v.Attributes.Select(a => a.Value))
                        : v.SKU ?? string.Empty,
                    Sku = v.SKU,
                    Price = v.EffectivePrice,
                    InStock = v.TotalStock > 0,
                    Attributes = v.Attributes?.ToDictionary(a => a.Name, a => a.Value) ?? new()
                })
                .ToList();
        }

        return context;
    }

    /// <summary>
    /// Build shipping context from available methods.
    /// Uses actual cart weight and subtotal for accurate shipping quotes.
    /// </summary>
    private async Task<ShippingContext> BuildShippingContextAsync(
        Guid tenantId, 
        Guid? customerId, 
        string? cartSessionId, 
        string correlationId)
    {
        var shippingContext = new ShippingContext();

        try
        {
            // Get cart to extract weight and subtotal for accurate shipping quotes
            var cart = await GetCartAsync(tenantId, customerId, cartSessionId, correlationId);
            
            // Use actual cart weight and subtotal if available, otherwise use 0
            // When cart is empty or unavailable, we still show methods but prices may be base rates
            var totalWeight = cart?.TotalWeight ?? 0m;
            var cartSubtotal = cart?.Subtotal ?? 0m;

            var methods = await _shippingMethodService.GetShippingMethodsAsync(
                tenantId, 
                cartSubtotal, 
                totalWeight, 
                correlationId,
                warehouseId: null);

            shippingContext.Methods = methods
                .Take(_options.MaxShippingMethodsInContext)
                .Select(m => new ShippingMethodContext
                {
                    Id = m.Id,
                    Name = m.Name,
                    Carrier = m.CarrierName,
                    Price = m.Price > 0 ? m.Price : m.BaseRate,
                    PricePerKg = m.RatePerKg,
                    EstimatedDays = m.EstimatedDays.HasValue 
                        ? $"{m.EstimatedDays} days" 
                        : m.DeliveryDays
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load shipping methods for context | TenantId: {TenantId}", tenantId);
        }

        return shippingContext;
    }

    /// <summary>
    /// Build order draft context from current cart.
    /// </summary>
    private async Task<OrderDraftContext> BuildOrderDraftContextAsync(
        Guid tenantId,
        Guid? customerId,
        string? cartSessionId,
        string correlationId)
    {
        var draftContext = new OrderDraftContext();

        try
        {
            var cart = await GetCartAsync(tenantId, customerId, cartSessionId, correlationId);

            if (cart != null && cart.Items.Count > 0)
            {
                draftContext.Exists = true;
                draftContext.Items = cart.Items.Select(item => new OrderDraftItemContext
                {
                    ProductId = item.ProductId,
                    ProductName = item.Name,
                    VariantId = item.VariantId,
                    VariantName = item.VariantAttributes?.FirstOrDefault()?.Value,
                    Quantity = item.Quantity,
                    Price = item.DiscountedUnitPrice ?? item.UnitPrice,
                    LineTotal = item.Quantity * (item.DiscountedUnitPrice ?? item.UnitPrice)
                }).ToList();

                draftContext.Subtotal = cart.Subtotal;
                draftContext.Total = cart.GrandTotal;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cart for context | TenantId: {TenantId}", tenantId);
        }

        return draftContext;
    }

    /// <summary>
    /// Build customer context from authenticated user.
    /// </summary>
    private async Task<CustomerContext> BuildCustomerContextAsync(Guid tenantId, Guid? customerId)
    {
        var customerContext = new CustomerContext
        {
            IsAuthenticated = customerId.HasValue
        };

        if (customerId.HasValue)
        {
            try
            {
                // Load customer details from MongoDB
                var collection = _mongoDatabase.GetCollection<OnlineCustomerDocument>("online_customers");
                var customer = await collection
                    .Find(c => c.TenantId == tenantId && c.CustomerId == customerId.Value)
                    .FirstOrDefaultAsync();

                if (customer != null)
                {
                    customerContext.Name = $"{customer.FirstName} {customer.LastName}".Trim();
                    customerContext.Email = customer.Email;
                    customerContext.Phone = customer.Phone;
                    // Location could be derived from default address if available
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load customer for context | CustomerId: {CustomerId}", customerId);
            }
        }

        return customerContext;
    }

    /// <summary>
    /// Get cart for the current user/session.
    /// </summary>
    private async Task<CartDto?> GetCartAsync(
        Guid tenantId,
        Guid? customerId,
        string? sessionId,
        string correlationId)
    {
        try
        {
            CartOwner owner;
            
            if (customerId.HasValue)
            {
                owner = new CartOwner { OwnerType = "customer", OwnerId = customerId.Value.ToString("N") };
            }
            else if (!string.IsNullOrEmpty(sessionId))
            {
                owner = new CartOwner { OwnerType = "session", OwnerId = sessionId };
            }
            else
            {
                return null;
            }

            return await _cartService.GetCartAsync(tenantId, owner, correlationId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// PHASE 6.5: Build order tracking context for post-purchase inquiries.
    /// </summary>
    private async Task<OrderTrackingContext?> BuildOrderTrackingContextAsync(
        Guid tenantId,
        Guid customerId,
        string message,
        string correlationId)
    {
        try
        {
            // Try to extract order number from message
            var orderNumber = ExtractOrderNumber(message);

            // Load order from MongoDB (read model)
            var ordersCollection = _mongoDatabase.GetCollection<OrderDocument>("orders");

            OrderDocument? order = null;

            if (!string.IsNullOrEmpty(orderNumber))
            {
                // Search by order number
                order = await ordersCollection
                    .Find(o => o.TenantId == tenantId && o.OnlineCustomerId == customerId && o.OrderNumber == orderNumber)
                    .FirstOrDefaultAsync();
            }
            else
            {
                // Get most recent order for customer
                order = await ordersCollection
                    .Find(o => o.TenantId == tenantId && o.OnlineCustomerId == customerId)
                    .SortByDescending(o => o.CreatedAtUtc)
                    .FirstOrDefaultAsync();
            }

            if (order == null)
            {
                return null;
            }

            // Map to tracking context
            return new OrderTrackingContext
            {
                Id = order.OrderId,
                Number = order.OrderNumber,
                Status = order.Status,
                CreatedAt = order.CreatedAtUtc,
                Items = order.Items?.Select(i => new OrderItemTrackingContext
                {
                    Name = i.ProductName,
                    Quantity = i.Quantity,
                    Price = i.UnitPrice
                }).ToList() ?? new(),
                Total = order.GrandTotal,
                Payment = new PaymentTrackingContext
                {
                    Status = order.PaymentStatus ?? "Unknown",
                    Method = order.PaymentMethod ?? "Unknown",
                    Amount = order.GrandTotal
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load order tracking context | TenantId: {TenantId} | CustomerId: {CustomerId}", tenantId, customerId);
            return null;
        }
    }

    /// <summary>
    /// PHASE 6.5: Build shipment tracking context.
    /// </summary>
    private async Task<ShipmentTrackingContext?> BuildShipmentTrackingContextAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId)
    {
        try
        {
            // Load shipment from MongoDB (read model)
            var shipmentsCollection = _mongoDatabase.GetCollection<ShipmentDocument>("shipments");

            var shipment = await shipmentsCollection
                .Find(s => s.TenantId == tenantId && s.OrderId == orderId)
                .FirstOrDefaultAsync();

            if (shipment == null)
            {
                return null;
            }

            // Map to tracking context
            return new ShipmentTrackingContext
            {
                Exists = true,
                Carrier = shipment.CarrierName,
                TrackingNumber = shipment.TrackingNumber,
                Status = shipment.Status,
                EstimatedDelivery = shipment.EstimatedDeliveryDate,
                Timeline = shipment.Timeline?.Select(t => new ShipmentEventContext
                {
                    Status = t.Status,
                    At = t.Timestamp,
                    Location = t.Location,
                    Notes = t.Description
                }).ToList() ?? new()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load shipment tracking context | TenantId: {TenantId} | OrderId: {OrderId}", tenantId, orderId);
            return null;
        }
    }

    /// <summary>
    /// PHASE 6.5: Build historical context with confidence level.
    /// </summary>
    private Task<HistoricalContext?> BuildHistoricalContextAsync(
        OrderTrackingContext? order,
        ShipmentTrackingContext? shipment)
    {
        if (order == null)
        {
            return Task.FromResult<HistoricalContext?>(null);
        }

        var historical = new HistoricalContext
        {
            OrderCreatedAt = order.CreatedAt,
            IsLegacyOrder = false,
            ShippingIntegrationEnabledAtCreation = true,
            PaymentIntegrationEnabledAtCreation = true,
            EventTrackingEnabledAtCreation = true,
            EventsIncomplete = false
        };

        // Determine confidence based on data completeness
        var confidence = DataConfidenceLevel.High;
        var confidenceNote = (string?)null;

        // Check if order is old (> 6 months)
        var orderAge = DateTime.UtcNow - order.CreatedAt;
        if (orderAge.TotalDays > 180)
        {
            historical.IsLegacyOrder = true;
            confidence = DataConfidenceLevel.Medium;
            confidenceNote = "This order was placed several months ago. Some tracking details may not be available.";
        }

        // Check if shipment data is missing
        if (shipment == null || !shipment.Exists)
        {
            if (order.Status == "Delivered" || order.Status == "Completed")
            {
                // Order delivered but no shipment tracking
                historical.ShippingIntegrationEnabledAtCreation = false;
                confidence = DataConfidenceLevel.Low;
                confidenceNote = "This order was delivered before automated tracking was enabled. Detailed shipment information is not available.";
            }
            else if (order.Status == "Processing" || order.Status == "Pending")
            {
                // Order not yet shipped
                confidence = DataConfidenceLevel.High;
                confidenceNote = null;
            }
            else
            {
                // Shipment should exist but doesn't
                historical.EventsIncomplete = true;
                confidence = DataConfidenceLevel.Medium;
                confidenceNote = "Some shipment details may not be recorded.";
            }
        }

        // Check if shipment timeline is empty
        if (shipment != null && shipment.Exists && (shipment.Timeline == null || !shipment.Timeline.Any()))
        {
            historical.EventTrackingEnabledAtCreation = false;
            confidence = DataConfidenceLevel.Medium;
            confidenceNote = "Detailed tracking events were not recorded for this shipment.";
        }

        historical.Confidence = confidence;
        historical.ConfidenceNote = confidenceNote;

        return Task.FromResult<HistoricalContext?>(historical);
    }

    /// <summary>
    /// PHASE 6.5: Extract order number from message.
    /// </summary>
    private string? ExtractOrderNumber(string message)
    {
        // Look for patterns like: #12345, ORD-12345, order 12345
        var patterns = new[]
        {
            @"#(\d+)",
            @"ORD-(\d+)",
            @"order\s+(\d+)",
            @"رقم\s+(\d+)" // Arabic: "number 12345"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }
}

/// <summary>
/// Order document from MongoDB (read model).
/// PHASE 6.5: Minimal projection for AI context.
/// </summary>
internal class OrderDocument
{
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<OrderItemDocument>? Items { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal GrandTotal { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentStatus { get; set; }
}

/// <summary>
/// Order item document.
/// </summary>
internal class OrderItemDocument
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Shipment document from MongoDB (read model).
/// PHASE 6.5: Minimal projection for AI context.
/// </summary>
internal class ShipmentDocument
{
    public Guid ShipmentId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? EstimatedDeliveryDate { get; set; }
    public List<ShipmentTimelineDocument>? Timeline { get; set; }
}

/// <summary>
/// Shipment timeline event document.
/// </summary>
internal class ShipmentTimelineDocument
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Online customer document for MongoDB.
/// </summary>
internal class OnlineCustomerDocument
{
    public Guid CustomerId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
}
