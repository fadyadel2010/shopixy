using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PointOfSale.API.Services.AI.Models;

/// <summary>
/// Complete commerce context provided to the AI assistant.
/// This is the ONLY data the AI can use - it must never invent data.
/// 
/// PHASE 1: Pre-purchase (shop, products, shipping, orderDraft, customer)
/// PHASE 3: Post-purchase (order, shipment)
/// PHASE 5: Historical context (legacy orders, confidence)
/// </summary>
public class CommerceContext
{
    /// <summary>
    /// Tenant ID for rate limiting and isolation.
    /// Not serialized to the AI prompt.
    /// </summary>
    [JsonIgnore]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Tone/Personality configuration (Phase 8).
    /// Determines the voice of the AI (friendly, professional, sales).
    /// </summary>
    [JsonPropertyName("personality")]
    public string Personality { get; set; } = "friendly";

    [JsonPropertyName("shop")]
    public ShopContext Shop { get; set; } = new();

    [JsonPropertyName("products")]
    public List<ProductContext> Products { get; set; } = new();

    [JsonPropertyName("shipping")]
    public ShippingContext Shipping { get; set; } = new();

    [JsonPropertyName("orderDraft")]
    public OrderDraftContext OrderDraft { get; set; } = new();

    [JsonPropertyName("customer")]
    public CustomerContext Customer { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    // =========================================================================
    // PHASE 3: Post-Purchase Context (Order Tracking)
    // =========================================================================

    /// <summary>
    /// Existing order context for post-purchase inquiries.
    /// PHASE 3: Event-Aware, Order-Context Intelligence.
    /// </summary>
    [JsonPropertyName("order")]
    public OrderTrackingContext? Order { get; set; }

    /// <summary>
    /// Shipment tracking context for delivery inquiries.
    /// PHASE 3: Event-Aware, Order-Context Intelligence.
    /// </summary>
    [JsonPropertyName("shipment")]
    public ShipmentTrackingContext? Shipment { get; set; }

    // =========================================================================
    // PHASE 5: Historical Consistency & Confidence Layer
    // =========================================================================

    /// <summary>
    /// Historical context for legacy order understanding.
    /// PHASE 5: Graceful handling of incomplete data.
    /// </summary>
    [JsonPropertyName("historical")]
    public HistoricalContext? Historical { get; set; }
}

/// <summary>
/// Shop/Store metadata and policies.
/// </summary>
public class ShopContext
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("policies")]
    public PoliciesContext Policies { get; set; } = new();
}

/// <summary>
/// Store policies (returns, delivery, etc.)
/// </summary>
public class PoliciesContext
{
    [JsonPropertyName("returns")]
    public string? Returns { get; set; }

    [JsonPropertyName("delivery")]
    public string? Delivery { get; set; }
}

/// <summary>
/// Product information for AI context.
/// </summary>
public class ProductContext
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nameAr")]
    public string? NameAr { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("inStock")]
    public bool InStock { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("descriptionAr")]
    public string? DescriptionAr { get; set; }

    [JsonPropertyName("variants")]
    public List<VariantContext> Variants { get; set; } = new();

    [JsonPropertyName("shippingEligible")]
    public bool ShippingEligible { get; set; } = true;

    [JsonPropertyName("hasVariants")]
    public bool HasVariants { get; set; }
}

/// <summary>
/// Product variant information.
/// </summary>
public class VariantContext
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("inStock")]
    public bool InStock { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string> Attributes { get; set; } = new();
}

/// <summary>
/// Available shipping methods.
/// </summary>
public class ShippingContext
{
    [JsonPropertyName("methods")]
    public List<ShippingMethodContext> Methods { get; set; } = new();
}

/// <summary>
/// Shipping method details.
/// </summary>
public class ShippingMethodContext
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("carrier")]
    public string? Carrier { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("pricePerKg")]
    public decimal? PricePerKg { get; set; }

    [JsonPropertyName("estimatedDays")]
    public string? EstimatedDays { get; set; }
}

/// <summary>
/// Current order draft state (from cart).
/// </summary>
public class OrderDraftContext
{
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    [JsonPropertyName("items")]
    public List<OrderDraftItemContext> Items { get; set; } = new();

    [JsonPropertyName("shippingMethodId")]
    public Guid? ShippingMethodId { get; set; }

    [JsonPropertyName("subtotal")]
    public decimal? Subtotal { get; set; }

    [JsonPropertyName("shippingCost")]
    public decimal? ShippingCost { get; set; }

    [JsonPropertyName("total")]
    public decimal? Total { get; set; }
}

/// <summary>
/// Item in the order draft.
/// </summary>
public class OrderDraftItemContext
{
    [JsonPropertyName("productId")]
    public Guid ProductId { get; set; }

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("variantId")]
    public Guid? VariantId { get; set; }

    [JsonPropertyName("variantName")]
    public string? VariantName { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("lineTotal")]
    public decimal LineTotal { get; set; }
}

/// <summary>
/// Customer information (if authenticated).
/// </summary>
public class CustomerContext
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("isAuthenticated")]
    public bool IsAuthenticated { get; set; }
}
