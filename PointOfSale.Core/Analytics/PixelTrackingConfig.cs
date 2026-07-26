using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Analytics;

/// <summary>
/// Configuration for Pixel Conversion Value Override (Phase M1).
/// Allows merchants to send modified values to ad platforms for privacy or strategy.
/// 
/// PRINCIPLES:
/// - Single Source of Truth: This config determines if/how values are modified.
/// - Isolation: Affects ONLY analytics dispatch, not orders/finance.
/// </summary>
public class PixelTrackingConfig : ITenantOwned
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Tenant that owns this configuration.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Whether value override is enabled.
    /// If false, actual order values are sent.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Strategy for calculating the override value.
    /// </summary>
    public PixelOverrideStrategy Strategy { get; set; } = PixelOverrideStrategy.Percentage;

    /// <summary>
    /// The value parameter for the strategy.
    /// - For Percentage: The percentage to KEEP (e.g., 70 = 70% of order total).
    /// - For Fixed: The absolute value to send (e.g., 10.00).
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// List of providers this override applies to.
    /// Serialized as JSON in database.
    /// </summary>
    [NotMapped]
    public PixelProvider[] AppliesTo { get; set; } = Array.Empty<PixelProvider>();
    
    /// <summary>
    /// Helper property for EF Core storage (JSON).
    /// Using a simple string property for simplicity in this phase, 
    /// handled by the service layer or EF Core ValueConversion.
    /// </summary>
    public string AppliesToJson { get; set; } = "[]";

    /// <summary>
    /// When the config was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who updated the config (Admin ID/Name).
    /// </summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Strategy for calculating the pixel value.
/// </summary>
public enum PixelOverrideStrategy
{
    /// <summary>
    /// Send a percentage of the actual order total.
    /// Formula: Actual * (Value / 100)
    /// </summary>
    Percentage = 1,

    /// <summary>
    /// Send a fixed constant value regardless of order size.
    /// Formula: Value
    /// </summary>
    Fixed = 2
}

/// <summary>
/// Providers supported by the pixel override system.
/// </summary>
public enum PixelProvider
{
    Google = 1,
    Meta = 2,
    TikTok = 3
}
