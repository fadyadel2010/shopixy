using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Write-side listing record for publishing a tenant-owned variant to the global CommerceHub.
/// Variants are the sellable unit; products are only catalog parents.
/// </summary>
public class CommerceHubListing : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid VariantId { get; private set; }
    public bool IsPublished { get; private set; }
    public bool RestorePublicationOnActivate { get; private set; }
    public Guid CommerceHubCategoryId { get; private set; }
    public decimal? CommerceHubPrice { get; private set; }
    public DateTime? CommerceHubPriceOverrideUpdatedAtUtc { get; private set; }
    public Guid? CommerceHubPriceOverrideUpdatedBy { get; private set; }
    public string? CommerceHubPriceOverrideReason { get; private set; }
    public int CommerceHubPriceOverrideMutationVersion { get; private set; }

    /// <summary>
    /// Dynamic category attributes stored as normalized JSON key-value map.
    /// Keys are snake_case, values are lowercased for search consistency.
    /// Example: {"brand":"samsung","screen_size":55,"resolution":"4k"}
    /// </summary>
    public string? AttributesJson { get; private set; }

    /// <summary>
    /// Frozen snapshot of the category's attribute schema at publish time.
    /// Ensures old listings remain valid even if category attributes change.
    /// </summary>
    public string? AttributeSchemaSnapshotJson { get; private set; }

    private CommerceHubListing()
    {
    }

    Guid ITenantOwned.TenantId
    {
        get => TenantId;
        set => TenantId = value;
    }

    public static CommerceHubListing Create(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        Guid commerceHubCategoryId,
        decimal? commerceHubPrice,
        string? attributesJson = null,
        string? attributeSchemaSnapshotJson = null,
        Guid? actorUserId = null,
        string? overrideReason = null)
    {
        ValidateIdentifiers(tenantId, productId, variantId, commerceHubCategoryId);
        ValidateCommerceHubPriceOverride(commerceHubPrice);
        EnsureActorForOverrideChange(commerceHubPrice, actorUserId);
        var now = DateTime.UtcNow;

        return new CommerceHubListing
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = productId,
            VariantId = variantId,
            IsPublished = true,
            RestorePublicationOnActivate = false,
            CommerceHubCategoryId = commerceHubCategoryId,
            CommerceHubPrice = commerceHubPrice,
            CommerceHubPriceOverrideUpdatedAtUtc = commerceHubPrice.HasValue ? now : null,
            CommerceHubPriceOverrideUpdatedBy = commerceHubPrice.HasValue ? actorUserId : null,
            CommerceHubPriceOverrideReason = commerceHubPrice.HasValue ? NormalizeReason(overrideReason) : null,
            CommerceHubPriceOverrideMutationVersion = commerceHubPrice.HasValue ? 1 : 0,
            AttributesJson = attributesJson,
            AttributeSchemaSnapshotJson = attributeSchemaSnapshotJson,
            CreatedAt = now
        };
    }

    public bool Publish(Guid commerceHubCategoryId, string? attributesJson = null, string? attributeSchemaSnapshotJson = null)
    {
        ValidateIdentifiers(TenantId, ProductId, VariantId, commerceHubCategoryId);

        var changed =
            !IsPublished ||
            CommerceHubCategoryId != commerceHubCategoryId ||
            AttributesJson != attributesJson ||
            AttributeSchemaSnapshotJson != attributeSchemaSnapshotJson;

        if (!changed)
        {
            return false;
        }

        IsPublished = true;
        RestorePublicationOnActivate = false;
        CommerceHubCategoryId = commerceHubCategoryId;
        AttributesJson = attributesJson;
        AttributeSchemaSnapshotJson = attributeSchemaSnapshotJson;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool Update(
        Guid commerceHubCategoryId,
        decimal? commerceHubPrice,
        string? attributesJson = null,
        string? attributeSchemaSnapshotJson = null,
        Guid? actorUserId = null,
        string? overrideReason = null,
        int? expectedPriceOverrideMutationVersion = null)
    {
        ValidateIdentifiers(TenantId, ProductId, VariantId, commerceHubCategoryId);
        ValidateCommerceHubPriceOverride(commerceHubPrice);

        var changed =
            CommerceHubCategoryId != commerceHubCategoryId ||
            CommerceHubPrice != commerceHubPrice ||
            AttributesJson != attributesJson ||
            AttributeSchemaSnapshotJson != attributeSchemaSnapshotJson;

        if (!changed)
        {
            return false;
        }

        CommerceHubCategoryId = commerceHubCategoryId;
        ApplyCommerceHubPriceOverride(
            commerceHubPrice,
            actorUserId,
            overrideReason,
            expectedPriceOverrideMutationVersion);
        AttributesJson = attributesJson;
        AttributeSchemaSnapshotJson = attributeSchemaSnapshotJson;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool Unpublish()
    {
        if (!IsPublished && !RestorePublicationOnActivate)
        {
            return false;
        }

        IsPublished = false;
        RestorePublicationOnActivate = false;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool HideByGovernance()
    {
        if (!IsPublished)
        {
            return false;
        }

        IsPublished = false;
        RestorePublicationOnActivate = true;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool RestoreAfterGovernance()
    {
        if (!RestorePublicationOnActivate)
        {
            return false;
        }

        IsPublished = true;
        RestorePublicationOnActivate = false;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool ClearGovernanceRestoreState()
    {
        if (!RestorePublicationOnActivate)
        {
            return false;
        }

        RestorePublicationOnActivate = false;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private static void ValidateIdentifiers(
        Guid tenantId,
        Guid productId,
        Guid variantId,
        Guid commerceHubCategoryId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (productId == Guid.Empty) throw new ArgumentException("ProductId is required.", nameof(productId));
        if (variantId == Guid.Empty) throw new ArgumentException("VariantId is required.", nameof(variantId));
        if (commerceHubCategoryId == Guid.Empty) throw new ArgumentException("CommerceHubCategoryId is required.", nameof(commerceHubCategoryId));
    }

    private static void ValidateCommerceHubPriceOverride(decimal? commerceHubPrice)
    {
        if (commerceHubPrice is <= 0m)
        {
            throw new ArgumentException(
                "CommerceHubPrice override must be greater than zero.",
                nameof(commerceHubPrice));
        }
    }

    private static void EnsureActorForOverrideChange(decimal? commerceHubPrice, Guid? actorUserId)
    {
        if (commerceHubPrice.HasValue && (!actorUserId.HasValue || actorUserId.Value == Guid.Empty))
        {
            throw new ArgumentException("Merchant actor is required for CommerceHub price override changes.", nameof(actorUserId));
        }
    }

    private void ApplyCommerceHubPriceOverride(
        decimal? commerceHubPrice,
        Guid? actorUserId,
        string? overrideReason,
        int? expectedMutationVersion)
    {
        if (CommerceHubPrice == commerceHubPrice)
        {
            return;
        }

        if (expectedMutationVersion != CommerceHubPriceOverrideMutationVersion)
        {
            throw new InvalidOperationException("CommerceHub price override mutation version is stale.");
        }

        EnsureActorForOverrideChange(commerceHubPrice ?? CommerceHubPrice, actorUserId);
        CommerceHubPrice = commerceHubPrice;
        CommerceHubPriceOverrideUpdatedAtUtc = DateTime.UtcNow;
        CommerceHubPriceOverrideUpdatedBy = actorUserId;
        CommerceHubPriceOverrideReason = NormalizeReason(overrideReason);
        CommerceHubPriceOverrideMutationVersion++;
    }

    private static string NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "merchant_settings_update"
            : reason.Trim();
}
