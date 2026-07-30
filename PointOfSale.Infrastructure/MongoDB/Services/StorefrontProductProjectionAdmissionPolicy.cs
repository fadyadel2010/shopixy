using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public interface IStorefrontProductProjectionAdmissionPolicy
{
    void Validate(StorefrontProductProjectionMutation mutation);
}

public sealed class StorefrontProductProjectionAdmissionPolicy : IStorefrontProductProjectionAdmissionPolicy
{
    public void Validate(StorefrontProductProjectionMutation mutation)
    {
        if (mutation.Kind != StorefrontProductProjectionMutationKind.ReviewedFullDocumentUpsert)
        {
            return;
        }

        var projection = mutation.Projection ??
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_REQUIRES_PROJECTION");

        RequireComputedStorefrontEffectivePrice();
        RequireAuthorityIdentity(mutation, projection);
        RequireCurrentProjectionContract(projection);
        RequireValidPublicationState(projection);
        RequireDefaultUnitAdmission(projection);
        RequireVariantAdmission(projection);
    }

    private static void RequireComputedStorefrontEffectivePrice()
    {
        var property = typeof(ProductDocument).GetProperty(nameof(ProductDocument.StorefrontEffectivePrice)) ??
            throw new InvalidOperationException("STOREFRONT_EFFECTIVE_PRICE_COMPUTED_ACCESSOR_REQUIRED");

        if (property.SetMethod != null)
        {
            throw new InvalidOperationException("STOREFRONT_EFFECTIVE_PRICE_MUST_NOT_BE_PERSISTABLE");
        }

        if (!Attribute.IsDefined(property, typeof(BsonIgnoreAttribute)))
        {
            throw new InvalidOperationException("STOREFRONT_EFFECTIVE_PRICE_MUST_BE_BSON_IGNORED");
        }
    }

    private static void RequireAuthorityIdentity(
        StorefrontProductProjectionMutation mutation,
        ProductDocument projection)
    {
        if (projection.TenantId == Guid.Empty ||
            projection.TenantId != mutation.TenantId)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_TENANT_MISMATCH");
        }

        if (projection.ProductId == Guid.Empty ||
            projection.ProductId != mutation.ProductId)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_PRODUCT_MISMATCH");
        }
    }

    private static void RequireCurrentProjectionContract(ProductDocument projection)
    {
        if (!string.Equals(
                projection.ReadModelName,
                ReadModelEvolutionRegistry.StorefrontProduct,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_READ_MODEL_MISMATCH");
        }

        if (projection.ReadModelSchemaVersion < ProductDocument.CurrentReadModelSchemaVersion)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_SCHEMA_VERSION_STALE");
        }

        if (projection.ProjectionVersion < ProductDocument.CurrentProjectionVersion)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_PROJECTION_VERSION_STALE");
        }

        if (projection.LastEventId is null || projection.LastEventId == Guid.Empty)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_EVENT_ID_REQUIRED");
        }

        if (projection.LastEventOccurredAtUtc is null)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_EVENT_TIMESTAMP_REQUIRED");
        }

        if (projection.LastEventVersion <= 0)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_EVENT_VERSION_REQUIRED");
        }
    }

    private static void RequireValidPublicationState(ProductDocument projection)
    {
        if (!projection.IsActive || !projection.IsOnlineVisible)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_REQUIRES_PUBLICATION_STATE");
        }
    }

    private static void RequireDefaultUnitAdmission(ProductDocument projection)
    {
        if (projection.HasVariants)
        {
            return;
        }

        var defaultUnit = projection.DefaultUnit ??
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_REQUIRED");

        if (defaultUnit.UnitId == Guid.Empty)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_ID_REQUIRED");
        }

        if (defaultUnit.SalePrice <= 0m)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_PRICE_INVALID");
        }

        if (defaultUnit.ConversionFactor <= 0m)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_CONVERSION_INVALID");
        }

        if (defaultUnit.IsBaseUnit && defaultUnit.ConversionFactor != 1m)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_BASE_UNIT_CONVERSION_INVALID");
        }

        if (projection.Units is null ||
            projection.Units.All(unit => unit.UnitId != defaultUnit.UnitId))
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_DEFAULT_UNIT_NOT_IN_UNITS");
        }
    }

    private static void RequireVariantAdmission(ProductDocument projection)
    {
        if (!projection.HasVariants)
        {
            return;
        }

        if (projection.Variants is null || projection.Variants.Count == 0)
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_VARIANT_REQUIRED");
        }

        foreach (var variant in projection.Variants)
        {
            if (variant.VariantId == Guid.Empty)
            {
                throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_VARIANT_ID_REQUIRED");
            }
        }

        if (!projection.Variants.Any(variant => variant.IsActive && variant.EffectivePrice > 0m))
        {
            throw new InvalidOperationException("STOREFRONT_PRODUCT_ADMISSION_PRICED_VARIANT_REQUIRED");
        }
    }
}
