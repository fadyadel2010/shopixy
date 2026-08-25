using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Interfaces.ProductAuthoring;

namespace PointOfSale.API.Services.ProductAuthoring;

public sealed class ProductAuthoringValidator : IProductAuthoringValidator
{
    public Task<IReadOnlyList<ProductAuthoringValidationError>> ValidateAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ProductAuthoringValidationError>();

        if (!Enum.IsDefined(command.Publication.PublishStatus))
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_PUBLISH_STATUS_INVALID",
                "publishStatus",
                "Publish status must be Draft, Published, or Archived."));
        }

        if (command.Content.Name?.Length > 300)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_NAME_TOO_LONG",
                "name",
                "Product name must not exceed 300 characters."));
        }

        if (command.Identity.Sku?.Length > 100)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_SKU_TOO_LONG",
                "sku",
                "Product SKU must not exceed 100 characters."));
        }

        if (command.Identity.Barcode?.Length > 100)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_BARCODE_TOO_LONG",
                "barcode",
                "Product barcode must not exceed 100 characters."));
        }

        if (command.Inventory.Unit?.Length > 50)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_UNIT_NAME_TOO_LONG",
                "unit",
                "Product unit must not exceed 50 characters."));
        }

        if (command.Metadata.ProductTemplate?.Length > 100)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_TEMPLATE_TOO_LONG",
                "productTemplate",
                "Product template must not exceed 100 characters."));
        }

        if (command.Pricing.TaxRate is < 0m or > 100m)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_TAX_RATE_INVALID",
                "taxRate",
                "Tax rate must be between zero and 100."));
        }

        foreach (var variant in command.Variants)
        {
            if (variant.Sku?.Length > 100)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_VARIANT_SKU_TOO_LONG",
                    "variants.sku",
                    "Variant SKU must not exceed 100 characters."));
            }

            if (variant.Barcode?.Length > 100)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_VARIANT_BARCODE_TOO_LONG",
                    "variants.barcode",
                    "Variant barcode must not exceed 100 characters."));
            }

            if (variant.Price < 0m)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_VARIANT_PRICE_INVALID",
                    "variants.price",
                    "Variant POS price cannot be negative."));
            }

            if (variant.OnlinePrice is < 0m)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_VARIANT_ONLINE_PRICE_INVALID",
                    "variants.onlinePrice",
                    "Variant online price cannot be negative."));
            }

            if (variant.CostPrice < 0m)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_VARIANT_COST_PRICE_INVALID",
                    "variants.costPrice",
                    "Variant cost price cannot be negative."));
            }

            if (variant.Weight is < 0m)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_VARIANT_WEIGHT_INVALID",
                    "variants.weight",
                    "Variant weight cannot be negative."));
            }

            if (variant.ImageUrl?.Length > 500)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_VARIANT_IMAGE_URL_TOO_LONG",
                    "variants.imageUrl",
                    "Variant image URL must not exceed 500 characters."));
            }

            foreach (var attribute in variant.Attributes)
            {
                if (attribute.Key?.Length > 100)
                {
                    errors.Add(new ProductAuthoringValidationError(
                        "PRODUCT_VARIANT_ATTRIBUTE_NAME_TOO_LONG",
                        "variants.attributes",
                        "Variant attribute names must not exceed 100 characters."));
                }

                if (attribute.Value?.Length > 255)
                {
                    errors.Add(new ProductAuthoringValidationError(
                        "PRODUCT_VARIANT_ATTRIBUTE_VALUE_TOO_LONG",
                        "variants.attributes",
                        "Variant attribute values must not exceed 255 characters."));
                }
            }
        }

        foreach (var unit in command.Units.Items.Where(unit => unit.UnitId != Guid.Empty))
        {
            if (unit.ConversionFactor <= 0m)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_UNIT_CONVERSION_FACTOR_REQUIRED",
                    "units.conversionFactor",
                    "Product unit conversion factors must be greater than zero."));
            }

            if (unit.IsBaseUnit && unit.ConversionFactor != 1m)
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_BASE_UNIT_CONVERSION_FACTOR_INVALID",
                    "units.baseUnit",
                    "The base unit conversion factor must be one."));
            }
        }

        if (command.Units.Items
            .Where(unit => unit.UnitId != Guid.Empty)
            .GroupBy(unit => unit.UnitId)
            .Any(group => group.Count() > 1))
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_UNIT_DUPLICATE",
                "units",
                "Each product unit may only be authored once."));
        }

        if (command.Units.Items.Count(unit => unit.IsBaseUnit) > 1)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_BASE_UNIT_DUPLICATE",
                "baseUnitId",
                "Only one base unit may be selected."));
        }

        if (command.Units.Items.Count(unit => unit.IsDefault) > 1)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_DEFAULT_UNIT_DUPLICATE",
                "defaultUnitId",
                "Only one default unit may be selected."));
        }

        return Task.FromResult<IReadOnlyList<ProductAuthoringValidationError>>(errors);
    }
}

public sealed class ProductAuthoringPolicy : IProductAuthoringPolicy
{
    private readonly IProductAuthoringValidator _validator;

    public ProductAuthoringPolicy(IProductAuthoringValidator validator)
    {
        _validator = validator;
    }

    public async Task<ProductAuthoringPolicyDecision> EvaluateAdmissionAsync(
        ProductAuthoringCommand command,
        ProductAuthoringAdmissionFacts facts,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.Operation))
        {
            return new ProductAuthoringPolicyDecision(
                ProductAuthoringStatus.ValidationFailed,
                [new ProductAuthoringValidationError("PRODUCT_AUTHORING_OPERATION_INVALID", "operation", "Unsupported product authoring operation.")]);
        }

        var structuralErrors = await _validator.ValidateAsync(command, cancellationToken);
        if (structuralErrors.Count > 0)
        {
            return new ProductAuthoringPolicyDecision(ProductAuthoringStatus.ValidationFailed, structuralErrors);
        }

        var requiredCoreErrors = ValidateRequiredCore(command, facts);
        if (requiredCoreErrors.Count > 0)
        {
            return new ProductAuthoringPolicyDecision(ProductAuthoringStatus.ValidationFailed, requiredCoreErrors);
        }

        var typeErrors = ValidateProductType(command);
        if (typeErrors.Count > 0)
        {
            return new ProductAuthoringPolicyDecision(ProductAuthoringStatus.RejectedByProductTypePolicy, typeErrors);
        }

        var publicationErrors = ValidatePublicationReadiness(command);
        if (publicationErrors.Count > 0)
        {
            return new ProductAuthoringPolicyDecision(ProductAuthoringStatus.NotReadyForPublication, publicationErrors);
        }

        return ProductAuthoringPolicyDecision.Allowed();
    }

    public Task<ProductAuthoringPolicyDecision> EvaluateSimpleProductIdentityAsync(
        ProductAuthoringCommand command,
        ProductAuthoringSimpleProductIdentityFacts facts,
        CancellationToken cancellationToken = default)
    {
        if (!command.Inventory.HasVariants && facts.BarcodeIsOwnedByAnotherSemanticEntity)
        {
            return Task.FromResult(new ProductAuthoringPolicyDecision(
                ProductAuthoringStatus.ValidationFailed,
                [new ProductAuthoringValidationError("PRODUCT_BARCODE_OWNERSHIP_CONFLICT", "barcode", "Barcode already belongs to another live product or variant.")]));
        }

        return Task.FromResult(ProductAuthoringPolicyDecision.Allowed());
    }

    public Task<ProductAuthoringPolicyDecision> EvaluateUnitReferencesAsync(
        ProductAuthoringCommand command,
        ProductAuthoringUnitReferenceFacts facts,
        CancellationToken cancellationToken = default)
    {
        if (!facts.AllAuthoredUnitsReferenceActiveTenantUnits)
        {
            return Task.FromResult(new ProductAuthoringPolicyDecision(
                ProductAuthoringStatus.ValidationFailed,
                [new ProductAuthoringValidationError("PRODUCT_UNIT_NOT_FOUND", "units", "All authored product units must reference active units owned by this tenant.")]));
        }

        return Task.FromResult(ProductAuthoringPolicyDecision.Allowed());
    }

    public Task<ProductAuthoringPolicyDecision> EvaluateLifecycleAsync(
        ProductAuthoringCommand command,
        ProductAuthoringLifecycleFacts facts,
        CancellationToken cancellationToken = default)
    {
        if (!facts.ProductExists)
        {
            return Task.FromResult(new ProductAuthoringPolicyDecision(
                ProductAuthoringStatus.NotFound,
                Array.Empty<ProductAuthoringValidationError>()));
        }

        return Task.FromResult(ProductAuthoringPolicyDecision.Allowed());
    }

    private static IReadOnlyList<ProductAuthoringValidationError> ValidateRequiredCore(
        ProductAuthoringCommand command,
        ProductAuthoringAdmissionFacts facts)
    {
        var errors = new List<ProductAuthoringValidationError>();
        var isDraft = command.Publication.PublishStatus == PointOfSale.Core.Enums.PublishStatus.Draft;

        if (!isDraft)
        {
            if (string.IsNullOrWhiteSpace(command.Content.Name))
            {
                errors.Add(new ProductAuthoringValidationError(
                    "PRODUCT_NAME_REQUIRED",
                    "name",
                    "Product name is required."));
            }

        }

        if (command.Operation is ProductAuthoringOperation.Create or ProductAuthoringOperation.Update &&
            command.Identity.CategoryId == Guid.Empty)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_CATEGORY_REQUIRED",
                "categoryId",
                "Product category is required."));
        }

        if (command.Operation is ProductAuthoringOperation.Create or ProductAuthoringOperation.Update &&
            command.Identity.CategoryId != Guid.Empty &&
            !facts.CategoryIsLive)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_CATEGORY_NOT_LIVE",
                "categoryId",
                "Product category must reference a live category with a valid SQL parent lineage."));
        }

        return errors;
    }

    private static IReadOnlyList<ProductAuthoringValidationError> ValidatePublicationReadiness(
        ProductAuthoringCommand command)
    {
        if (command.Publication.PublishStatus == PointOfSale.Core.Enums.PublishStatus.Draft ||
            !command.Publication.IsActive ||
            !command.Publication.IsOnlineVisible)
        {
            return Array.Empty<ProductAuthoringValidationError>();
        }

        return command.Inventory.HasVariants
            ? ValidateVariantPublication(command)
            : ValidateSimplePublication(command);
    }

    private static IReadOnlyList<ProductAuthoringValidationError> ValidateSimplePublication(
        ProductAuthoringCommand command)
    {
        var errors = new List<ProductAuthoringValidationError>();

        var units = command.Units.Items
            .Where(unit => unit.UnitId != Guid.Empty)
            .ToList();
        var baseUnit = command.Units.BaseUnitId.HasValue
            ? units.FirstOrDefault(unit => unit.UnitId == command.Units.BaseUnitId.Value)
            : null;
        var defaultUnit = command.Units.DefaultUnitId.HasValue
            ? units.FirstOrDefault(unit => unit.UnitId == command.Units.DefaultUnitId.Value)
            : null;

        if (units.Count == 0)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_UNITS_REQUIRED",
                "units",
                "At least one product unit is required before publication."));
        }

        if (command.Units.BaseUnitId is null || baseUnit is null || !baseUnit.IsBaseUnit)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_BASE_UNIT_REQUIRED",
                "baseUnitId",
                "A base unit is required before publication."));
        }

        if (command.Units.DefaultUnitId is null || defaultUnit is null || !defaultUnit.IsDefault)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_DEFAULT_UNIT_REQUIRED",
                "defaultUnitId",
                "A default unit is required before publication."));
        }

        if (defaultUnit is not null && (defaultUnit.SellingPrice ?? 0m) <= 0m)
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_DEFAULT_UNIT_PRICE_REQUIRED",
                "units.sellingPrice",
                "The default unit must have a positive selling price before publication."));
        }

        if (command.Units.Items.Any(unit => unit.UnitId != Guid.Empty && unit.ConversionFactor <= 0m))
        {
            errors.Add(new ProductAuthoringValidationError(
                "PRODUCT_UNIT_CONVERSION_FACTORS_INVALID",
                "units",
                "All authored product units must have valid conversion factors before publication."));
        }

        return errors;
    }

    private static IReadOnlyList<ProductAuthoringValidationError> ValidateVariantPublication(
        ProductAuthoringCommand command)
    {
        var hasActivePricedVariant = command.Variants.Any(variant =>
            variant.IsActive &&
            (variant.OnlinePrice.GetValueOrDefault() > 0m || variant.Price > 0m));

        if (hasActivePricedVariant)
        {
            return Array.Empty<ProductAuthoringValidationError>();
        }

        return new[]
        {
            new ProductAuthoringValidationError(
                "PRODUCT_ACTIVE_PRICED_VARIANT_REQUIRED",
                "variants",
                "At least one active priced variant is required before publication.")
        };
    }

    private static IReadOnlyList<ProductAuthoringValidationError> ValidateProductType(
        ProductAuthoringCommand command)
    {
        var errors = new List<ProductAuthoringValidationError>();

        if (!command.Inventory.HasVariants && command.Variants.Count > 1)
        {
            errors.Add(new ProductAuthoringValidationError(
                "SIMPLE_PRODUCT_VARIANT_STATE_INVALID",
                "variants",
                "Simple products may only author their default variant."));
        }

        if (command.Inventory.HasVariants &&
            command.Units.Items.Any(unit => unit.IsDefault && (unit.SellingPrice ?? 0m) > 0m) &&
            command.Variants.Count == 0 &&
            command.Publication.PublishStatus != PointOfSale.Core.Enums.PublishStatus.Draft)
        {
            errors.Add(new ProductAuthoringValidationError(
                "VARIANT_PRODUCT_VARIANT_STATE_REQUIRED",
                "variants",
                "Variant products must author variant data before publication."));
        }

        return errors;
    }
}
