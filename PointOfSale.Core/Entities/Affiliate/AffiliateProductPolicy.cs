using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// Aggregate root: Commission policy for a specific product within a tenant.
///
/// INVARIANTS:
/// - One active policy per (TenantId, ProductId) — enforced DB unique constraint
/// - CommissionRate: [0.0001, 0.50] (0.01% – 50%) — enforced DB CHECK + domain
/// - Only tenant admin can create/update — affiliate NEVER controls rate
/// - IsEnabled can be toggled; disabled policies produce zero commission
/// </summary>
public class AffiliateProductPolicy : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// FK → Products.Id. The product this policy applies to.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Commission rate as a decimal fraction.
    /// 0.10 = 10%. Range: [0.0001, 0.50].
    /// </summary>
    public decimal CommissionRate { get; private set; }

    /// <summary>
    /// Whether this policy is active.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// Optional cap on commission per order-item. Null = no cap.
    /// </summary>
    public decimal? MaxCommissionPerItem { get; private set; }

    /// <summary>
    /// AF-28: Product-level template assignment. Default: Classic:v1.
    /// Dictates structural rendering engine in Storefront Landing Pages.
    /// </summary>
    public string TemplateType { get; private set; } = PointOfSale.Core.Constants.LandingTemplates.ClassicV1;

    /// <summary>
    /// AF-30: Global marketplace category assignment.
    /// Required when affiliate is enabled. Must be active category.
    /// </summary>
    public Guid? AffiliateCategoryId { get; private set; }

    private AffiliateProductPolicy() { }

    public static AffiliateProductPolicy Create(
        Guid tenantId, Guid productId, decimal commissionRate, decimal? maxPerItem = null, Guid? categoryId = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId required", nameof(tenantId));
        if (productId == Guid.Empty) throw new ArgumentException("ProductId required", nameof(productId));
        ValidateRate(commissionRate);
        ValidateMaxCap(maxPerItem);

        return new AffiliateProductPolicy
        {
            TenantId = tenantId,
            ProductId = productId,
            CommissionRate = commissionRate,
            MaxCommissionPerItem = maxPerItem,
            IsEnabled = true,
            TemplateType = PointOfSale.Core.Constants.LandingTemplates.ClassicV1,
            AffiliateCategoryId = categoryId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateRate(decimal newRate, decimal? newMaxPerItem)
    {
        ValidateRate(newRate);
        ValidateMaxCap(newMaxPerItem);
        CommissionRate = newRate;
        MaxCommissionPerItem = newMaxPerItem;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateTemplate(string templateType)
    {
        if (string.IsNullOrWhiteSpace(templateType))
            templateType = PointOfSale.Core.Constants.LandingTemplates.ClassicV1;
            
        if (!PointOfSale.Core.Constants.LandingTemplates.Allowed.Contains(templateType))
            templateType = PointOfSale.Core.Constants.LandingTemplates.ClassicV1;

        TemplateType = templateType;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateCategory(Guid? categoryId)
    {
        // AF-30A: Enforce category requirement when enabled
        if (IsEnabled && categoryId == null)
            throw new InvalidOperationException(
                "Affiliate category is required when affiliate marketing is enabled. " +
                "Disable affiliate first or provide a valid category.");

        AffiliateCategoryId = categoryId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Enable()
    {
        // AF-30A: Enforce category requirement when enabling
        if (AffiliateCategoryId == null)
            throw new InvalidOperationException(
                "Cannot enable affiliate marketing without assigning a category. " +
                "Assign a category first using UpdateCategory().");

        IsEnabled = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Disable() { IsEnabled = false; UpdatedAt = DateTime.UtcNow; }

    private static void ValidateRate(decimal rate)
    {
        if (rate < 0.0001m || rate > 0.50m)
            throw new ArgumentException("CommissionRate must be between 0.01% (0.0001) and 50% (0.50)");
    }

    private static void ValidateMaxCap(decimal? maxCap)
    {
        if (maxCap.HasValue && maxCap.Value <= 0)
            throw new ArgumentException("MaxCommissionPerItem must be positive when set");
    }

    // AF-30A FIX D-1: EF Core setter methods REMOVED.
    // EF Core materializes entities via reflection on `private set` properties.
    // Public setter methods bypass domain validation:
    // - SetIsEnabled(true) without category violates AF-30A invariant
    // - SetCommissionRate(-1) bypasses rate range validation
    // DB CHECK constraints provide a backstop, but no bypass path should exist.
}
