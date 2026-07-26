using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Tenant-scoped payment method configuration for E-commerce.
/// 
/// PHASE 10.4.D1: Payment Methods Management
/// 
/// ARCHITECTURAL RULES:
/// - Each tenant has their own payment method configurations
/// - POS Admin is the ONLY controller (enable/disable, configuration)
/// - Storefront is READ-ONLY (displays enabled methods only)
/// - Secrets are encrypted using Data Protection API
/// - NO payment provider integration in this entity
/// 
/// Code values:
/// - COD: Cash on Delivery (always available, no config needed)
/// - STRIPE: Stripe integration (requires PublishableKey, SecretKey)
/// - PAYMOB: Paymob integration (requires ApiKey, IntegrationId)
/// </summary>
public class TenantPaymentMethod : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this payment method configuration.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Unique code identifying the payment method type.
    /// Examples: "COD", "STRIPE", "PAYMOB"
    /// UNIQUE per tenant (composite key: TenantId + Code)
    /// </summary>
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name (English).
    /// Example: "Cash on Delivery", "Credit/Debit Card"
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name (Arabic).
    /// Example: "الدفع عند الاستلام", "بطاقة ائتمان"
    /// </summary>
    public string? DisplayNameAr { get; set; }
    
    /// <summary>
    /// Description for customers.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Arabic description for customers.
    /// </summary>
    public string? DescriptionAr { get; set; }
    
    /// <summary>
    /// Whether this payment method is enabled for customers.
    /// Disabled methods are hidden from storefront.
    /// </summary>
    public bool IsEnabled { get; set; }
    
    /// <summary>
    /// Display order in payment method selection.
    /// Lower values appear first.
    /// </summary>
    public int SortOrder { get; set; }
    
    /// <summary>
    /// Whether this method supports online payments (card, gateway).
    /// True for: STRIPE, PAYMOB
    /// False for: COD
    /// </summary>
    public bool SupportsOnline { get; set; }
    
    /// <summary>
    /// Whether this method supports offline payments.
    /// True for: COD
    /// False for: STRIPE, PAYMOB
    /// </summary>
    public bool SupportsOffline { get; set; }
    
    /// <summary>
    /// Whether this is in sandbox/test mode.
    /// Affects which API endpoints are used.
    /// </summary>
    public bool IsSandbox { get; set; } = true;
    
    /// <summary>
    /// Encrypted JSON configuration containing secrets.
    /// 
    /// SECURITY: This field is encrypted using ASP.NET Core Data Protection API.
    /// Structure varies by provider:
    /// 
    /// STRIPE: { "publishableKey": "pk_...", "secretKey": "sk_...", "webhookSecret": "whsec_..." }
    /// PAYMOB: { "apiKey": "...", "integrationId": "...", "iframeId": "..." }
    /// COD: null (no configuration needed)
    /// 
    /// ❌ NEVER return to frontend
    /// ❌ NEVER log
    /// </summary>
    public string? ConfigEncrypted { get; set; }
    
    /// <summary>
    /// Icon identifier for display.
    /// Examples: "cash", "credit-card", "stripe", "paymob"
    /// </summary>
    public string? Icon { get; set; }
    
    /// <summary>
    /// Minimum order amount for this payment method.
    /// NULL = no minimum.
    /// </summary>
    public decimal? MinOrderAmount { get; set; }
    
    /// <summary>
    /// Maximum order amount for this payment method.
    /// NULL = no maximum.
    /// </summary>
    public decimal? MaxOrderAmount { get; set; }
    
    /// <summary>
    /// Additional fee for using this payment method (e.g., COD surcharge).
    /// </summary>
    public decimal Fee { get; set; }
    
    /// <summary>
    /// Fee type: "fixed" or "percentage"
    /// </summary>
    public string FeeType { get; set; } = "fixed";
    
    /// <summary>
    /// Updated timestamp.
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }
    
    /// <summary>
    /// Event version counter for projection ordering.
    /// Incremented on every mutation that publishes an event.
    /// Used by projections to detect and drop stale/out-of-order events.
    /// </summary>
    public int Version { get; set; } = 1;
}
