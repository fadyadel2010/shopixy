using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Analytics;

/// <summary>
/// Configuration for Per-Product Pixel Conversion Value Override (Phase M2).
/// Allows merchants to override pixel-reported values at the product level.
/// 
/// PRECEDENCE RULES:
/// 1. Product Override (if enabled) wins
/// 2. Global Override (Phase M1) applies if NO product override exists for the item
/// 3. Actual Price is used if neither applies
/// </summary>
public class ProductPixelOverride : ITenantOwned
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Tenant constraint.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The product being overridden.
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Whether this specific override is active.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Strategy: Percentage of product price, or Fixed value.
    /// </summary>
    public PixelOverrideStrategy Strategy { get; set; } = PixelOverrideStrategy.Percentage;

    /// <summary>
    /// The value parameter (e.g., 50.00 for 50%, or 10.00 for fixed $10).
    /// </summary>
    public decimal Value { get; set; }

    // ==========================================
    // PER-PLATFORM GRANULARITY
    // ==========================================

    /// <summary>
    /// Apply to Meta/Facebook Pixel?
    /// </summary>
    public bool ApplyToMeta { get; set; } = true;

    /// <summary>
    /// Apply to Google Analytics/Ads?
    /// </summary>
    public bool ApplyToGoogle { get; set; } = true;

    /// <summary>
    /// Apply to TikTok Pixel?
    /// </summary>
    public bool ApplyToTikTok { get; set; } = true;

    // ==========================================
    // AUDIT
    // ==========================================

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    
    public Guid UpdatedBy { get; set; }

    // Navigation
    [ForeignKey("ProductId")]
    [System.Text.Json.Serialization.JsonIgnore]
    public Product Product { get; set; } = null!;
}
