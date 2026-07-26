using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Onboarding checklist for new tenants.
/// Tracks setup progress to guide merchants through essential configuration.
/// Phase 8.0: Tenant Registration System
/// </summary>
public class OnboardingChecklist : ITenantOwned
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Tenant that owns this checklist.
    /// One checklist per tenant (unique constraint).
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// JSON-serialized checklist items and their statuses.
    /// Format: [{ "key": "store_profile", "status": "completed" }, ...]
    /// </summary>
    public string ItemsJson { get; set; } = "[]";
    
    /// <summary>
    /// Timestamp when all required items were completed.
    /// Null until completion.
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }
    
    /// <summary>
    /// Timestamp when the user dismissed/hid the checklist.
    /// Null if not dismissed.
    /// </summary>
    public DateTime? DismissedAtUtc { get; set; }
    
    /// <summary>
    /// When the checklist was created
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    
    // =========================================================================
    // Default checklist items (static definition)
    // =========================================================================
    
    /// <summary>
    /// Returns the default checklist items for a new tenant.
    /// Called during tenant bootstrap.
    /// </summary>
    public static string GetDefaultItemsJson()
    {
        return """
        [
            { "key": "store_profile", "status": "pending", "required": true },
            { "key": "first_category", "status": "pending", "required": false },
            { "key": "first_product", "status": "pending", "required": true },
            { "key": "inventory_setup", "status": "pending", "required": true },
            { "key": "shipping_config", "status": "pending", "required": true },
            { "key": "payment_review", "status": "completed", "required": false },
            { "key": "enable_storefront", "status": "pending", "required": true }
        ]
        """;
    }
}
