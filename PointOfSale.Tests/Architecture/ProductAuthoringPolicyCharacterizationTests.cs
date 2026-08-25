using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.ProductAuthoring;

namespace PointOfSale.Tests.Architecture;

public sealed class ProductAuthoringPolicyCharacterizationTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid UnitId = Guid.NewGuid();

    [Fact]
    public async Task ProductAuthoringPolicy_MatchesLegacyAdmissionDecisions()
    {
        var cases = new[]
        {
            new PolicyCase(
                "draft product without publication readiness is allowed",
                Command(PublishStatus.Draft, hasVariants: false, isOnlineVisible: true, units: []),
                CategoryIsLive: true),
            new PolicyCase(
                "published simple product requires units",
                Command(PublishStatus.Published, hasVariants: false, isOnlineVisible: true, units: []),
                CategoryIsLive: true),
            new PolicyCase(
                "published simple product requires a priced default unit",
                Command(PublishStatus.Published, hasVariants: false, isOnlineVisible: true, units: [Unit(sellingPrice: 0m)]),
                CategoryIsLive: true),
            new PolicyCase(
                "published variant product accepts active priced variants",
                Command(PublishStatus.Published, hasVariants: true, isOnlineVisible: true, variants: [Variant(price: 15m, onlinePrice: 14m)]),
                CategoryIsLive: true),
            new PolicyCase(
                "published variant product requires variant data before publication",
                Command(PublishStatus.Published, hasVariants: true, isOnlineVisible: true, units: [Unit(sellingPrice: 12m)], variants: []),
                CategoryIsLive: true),
            new PolicyCase(
                "simple product rejects multiple authored variants",
                Command(PublishStatus.Draft, hasVariants: false, isOnlineVisible: true, variants: [Variant(), Variant(sku: "SKU-2")]),
                CategoryIsLive: true),
            new PolicyCase(
                "marketplace hidden product bypasses publication readiness",
                Command(PublishStatus.Published, hasVariants: false, isOnlineVisible: false, units: []),
                CategoryIsLive: true),
            new PolicyCase(
                "category removal is rejected",
                Command(PublishStatus.Draft, hasVariants: false, isOnlineVisible: true, categoryId: Guid.Empty),
                CategoryIsLive: false),
            new PolicyCase(
                "category changes must target live category lineage",
                Command(PublishStatus.Published, hasVariants: false, isOnlineVisible: true, units: [Unit()], categoryId: CategoryId),
                CategoryIsLive: false),
            new PolicyCase(
                "duplicate default units remain structural validation failures",
                Command(PublishStatus.Draft, hasVariants: false, isOnlineVisible: true, units: [Unit(), Unit(unitId: Guid.NewGuid())]),
                CategoryIsLive: true)
        };

        var policy = new ProductAuthoringPolicy(new ProductAuthoringValidator());

        foreach (var item in cases)
        {
            var expected = await LegacyEvaluateAdmissionAsync(item.Command, item.CategoryIsLive);
            var actual = await policy.EvaluateAdmissionAsync(
                item.Command,
                new ProductAuthoringAdmissionFacts(item.CategoryIsLive));

            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Errors.Select(error => error.Code), actual.Errors.Select(error => error.Code));
        }
    }

    [Fact]
    public async Task ProductAuthoringPolicy_MatchesLegacyIdentityUnitAndLifecycleDecisions()
    {
        var command = Command(PublishStatus.Published, hasVariants: false, isOnlineVisible: true, units: [Unit()]);
        var policy = new ProductAuthoringPolicy(new ProductAuthoringValidator());

        var identityDecision = await policy.EvaluateSimpleProductIdentityAsync(
            command,
            new ProductAuthoringSimpleProductIdentityFacts(BarcodeIsOwnedByAnotherSemanticEntity: true));
        Assert.Equal(ProductAuthoringStatus.ValidationFailed, identityDecision.Status);
        Assert.Contains(identityDecision.Errors, error => error.Code == "PRODUCT_BARCODE_OWNERSHIP_CONFLICT");

        var unitDecision = await policy.EvaluateUnitReferencesAsync(
            command,
            new ProductAuthoringUnitReferenceFacts(AllAuthoredUnitsReferenceActiveTenantUnits: false));
        Assert.Equal(ProductAuthoringStatus.ValidationFailed, unitDecision.Status);
        Assert.Contains(unitDecision.Errors, error => error.Code == "PRODUCT_UNIT_NOT_FOUND");

        var archiveDecision = await policy.EvaluateLifecycleAsync(
            command with { Operation = ProductAuthoringOperation.Archive },
            new ProductAuthoringLifecycleFacts(ProductExists: true));
        Assert.Equal(ProductAuthoringStatus.Accepted, archiveDecision.Status);

        var missingLifecycleDecision = await policy.EvaluateLifecycleAsync(
            command with { Operation = ProductAuthoringOperation.Remove },
            new ProductAuthoringLifecycleFacts(ProductExists: false));
        Assert.Equal(ProductAuthoringStatus.NotFound, missingLifecycleDecision.Status);
    }

    [Fact]
    public async Task ProductAuthoringValidator_RejectsInvalidLifecycleAndDbBoundValuesBeforePersistence()
    {
        var baseline = Command(PublishStatus.Draft, hasVariants: false, isOnlineVisible: false);
        var invalid = baseline with
        {
            Content = baseline.Content with { Name = new string('x', 301) },
            Pricing = baseline.Pricing with { TaxRate = -1m },
            Publication = baseline.Publication with { PublishStatus = (PublishStatus)999 },
            Metadata = baseline.Metadata with { ProductTemplate = new string('t', 101) }
        };

        var errors = await new ProductAuthoringValidator().ValidateAsync(invalid);

        Assert.Contains(errors, error => error.Code == "PRODUCT_NAME_TOO_LONG");
        Assert.Contains(errors, error => error.Code == "PRODUCT_TAX_RATE_INVALID");
        Assert.Contains(errors, error => error.Code == "PRODUCT_PUBLISH_STATUS_INVALID");
        Assert.Contains(errors, error => error.Code == "PRODUCT_TEMPLATE_TOO_LONG");
    }

    [Fact]
    public async Task VariantPublicationReadiness_RequiresOneActiveVariantWithPositivePosOrOnlinePrice()
    {
        var policy = new ProductAuthoringPolicy(new ProductAuthoringValidator());
        var cases = new[]
        {
            (Name: "active POS-priced variant", Variants: new[] { Variant(price: 15m, onlinePrice: null) }, Ready: true),
            (Name: "active online-priced variant", Variants: new[] { Variant(price: 0m, onlinePrice: 14m) }, Ready: true),
            (Name: "inactive priced variant", Variants: new[] { Variant(price: 15m, onlinePrice: 14m, isActive: false) }, Ready: false),
            (Name: "active zero-price variant", Variants: new[] { Variant(price: 0m, onlinePrice: 0m) }, Ready: false),
            (Name: "no variants", Variants: Array.Empty<ProductAuthoringVariant>(), Ready: false)
        };

        foreach (var item in cases)
        {
            var decision = await policy.EvaluateAdmissionAsync(
                Command(PublishStatus.Published, hasVariants: true, isOnlineVisible: true, variants: item.Variants),
                new ProductAuthoringAdmissionFacts(CategoryIsLive: true));

            Assert.Equal(item.Ready ? ProductAuthoringStatus.Accepted : ProductAuthoringStatus.NotReadyForPublication, decision.Status);
            Assert.Equal(
                item.Ready,
                decision.Errors.All(error => error.Code != "PRODUCT_ACTIVE_PRICED_VARIANT_REQUIRED"));
        }
    }

    [Fact]
    public async Task ProductAuthoringValidator_RejectsNegativeVariantMoneyAndWeightButPreservesNullAndZero()
    {
        var baseline = Command(
            PublishStatus.Draft,
            hasVariants: true,
            isOnlineVisible: false,
            variants: [Variant(price: 0m, onlinePrice: null)]);
        var baselineErrors = await new ProductAuthoringValidator().ValidateAsync(baseline);
        Assert.DoesNotContain(baselineErrors, error => error.Field.StartsWith("variants", StringComparison.Ordinal));

        var invalid = baseline with
        {
            Variants =
            [
                Variant(price: -1m, onlinePrice: -2m) with
                {
                    CostPrice = -3m,
                    Weight = -0.1m
                }
            ]
        };

        var errors = await new ProductAuthoringValidator().ValidateAsync(invalid);

        Assert.Contains(errors, error => error.Code == "PRODUCT_VARIANT_PRICE_INVALID");
        Assert.Contains(errors, error => error.Code == "PRODUCT_VARIANT_ONLINE_PRICE_INVALID");
        Assert.Contains(errors, error => error.Code == "PRODUCT_VARIANT_COST_PRICE_INVALID");
        Assert.Contains(errors, error => error.Code == "PRODUCT_VARIANT_WEIGHT_INVALID");
    }

    private static async Task<ProductAuthoringPolicyDecision> LegacyEvaluateAdmissionAsync(
        ProductAuthoringCommand command,
        bool categoryIsLive)
    {
        var validationErrors = await new ProductAuthoringValidator().ValidateAsync(command);
        if (validationErrors.Count > 0)
        {
            return new ProductAuthoringPolicyDecision(ProductAuthoringStatus.ValidationFailed, validationErrors);
        }

        var requiredCoreErrors = LegacyValidateRequiredCore(command, categoryIsLive);
        if (requiredCoreErrors.Count > 0)
        {
            return new ProductAuthoringPolicyDecision(ProductAuthoringStatus.ValidationFailed, requiredCoreErrors);
        }

        var productTypeErrors = LegacyValidateProductType(command);
        if (productTypeErrors.Count > 0)
        {
            return new ProductAuthoringPolicyDecision(ProductAuthoringStatus.RejectedByProductTypePolicy, productTypeErrors);
        }

        var publicationErrors = LegacyValidatePublicationReadiness(command);
        return publicationErrors.Count == 0
            ? ProductAuthoringPolicyDecision.Allowed()
            : new ProductAuthoringPolicyDecision(ProductAuthoringStatus.NotReadyForPublication, publicationErrors);
    }

    private static IReadOnlyList<ProductAuthoringValidationError> LegacyValidateRequiredCore(
        ProductAuthoringCommand command,
        bool categoryIsLive)
    {
        var errors = new List<ProductAuthoringValidationError>();
        var isDraft = command.Publication.PublishStatus == PublishStatus.Draft;

        if (!isDraft)
        {
            if (string.IsNullOrWhiteSpace(command.Content.Name))
            {
                errors.Add(new ProductAuthoringValidationError("PRODUCT_NAME_REQUIRED", "name", "Product name is required."));
            }

            if (command.Pricing.TaxRate < 0m)
            {
                errors.Add(new ProductAuthoringValidationError("PRODUCT_TAX_RATE_INVALID", "taxRate", "Tax rate cannot be negative."));
            }
        }

        if (command.Operation is ProductAuthoringOperation.Create or ProductAuthoringOperation.Update &&
            command.Identity.CategoryId == Guid.Empty)
        {
            errors.Add(new ProductAuthoringValidationError("PRODUCT_CATEGORY_REQUIRED", "categoryId", "Product category is required."));
        }

        if (command.Operation is ProductAuthoringOperation.Create or ProductAuthoringOperation.Update &&
            command.Identity.CategoryId != Guid.Empty &&
            !categoryIsLive)
        {
            errors.Add(new ProductAuthoringValidationError("PRODUCT_CATEGORY_NOT_LIVE", "categoryId", "Product category must reference a live category with a valid SQL parent lineage."));
        }

        return errors;
    }

    private static IReadOnlyList<ProductAuthoringValidationError> LegacyValidateProductType(ProductAuthoringCommand command)
    {
        var errors = new List<ProductAuthoringValidationError>();

        if (!command.Inventory.HasVariants && command.Variants.Count > 1)
        {
            errors.Add(new ProductAuthoringValidationError("SIMPLE_PRODUCT_VARIANT_STATE_INVALID", "variants", "Simple products may only author their default variant."));
        }

        if (command.Inventory.HasVariants &&
            command.Units.Items.Any(unit => unit.IsDefault && (unit.SellingPrice ?? 0m) > 0m) &&
            command.Variants.Count == 0 &&
            command.Publication.PublishStatus != PublishStatus.Draft)
        {
            errors.Add(new ProductAuthoringValidationError("VARIANT_PRODUCT_VARIANT_STATE_REQUIRED", "variants", "Variant products must author variant data before publication."));
        }

        return errors;
    }

    private static IReadOnlyList<ProductAuthoringValidationError> LegacyValidatePublicationReadiness(ProductAuthoringCommand command)
    {
        if (command.Publication.PublishStatus == PublishStatus.Draft ||
            !command.Publication.IsActive ||
            !command.Publication.IsOnlineVisible)
        {
            return Array.Empty<ProductAuthoringValidationError>();
        }

        if (command.Inventory.HasVariants)
        {
            return command.Variants.Any(variant => variant.IsActive && (variant.OnlinePrice.GetValueOrDefault() > 0m || variant.Price > 0m))
                ? Array.Empty<ProductAuthoringValidationError>()
                : [new ProductAuthoringValidationError("PRODUCT_ACTIVE_PRICED_VARIANT_REQUIRED", "variants", "At least one active priced variant is required before publication.")];
        }

        var errors = new List<ProductAuthoringValidationError>();
        var units = command.Units.Items.Where(unit => unit.UnitId != Guid.Empty).ToList();
        var baseUnit = command.Units.BaseUnitId.HasValue ? units.FirstOrDefault(unit => unit.UnitId == command.Units.BaseUnitId.Value) : null;
        var defaultUnit = command.Units.DefaultUnitId.HasValue ? units.FirstOrDefault(unit => unit.UnitId == command.Units.DefaultUnitId.Value) : null;

        if (units.Count == 0)
        {
            errors.Add(new ProductAuthoringValidationError("PRODUCT_UNITS_REQUIRED", "units", "At least one product unit is required before publication."));
        }

        if (command.Units.BaseUnitId is null || baseUnit is null || !baseUnit.IsBaseUnit)
        {
            errors.Add(new ProductAuthoringValidationError("PRODUCT_BASE_UNIT_REQUIRED", "baseUnitId", "A base unit is required before publication."));
        }

        if (command.Units.DefaultUnitId is null || defaultUnit is null || !defaultUnit.IsDefault)
        {
            errors.Add(new ProductAuthoringValidationError("PRODUCT_DEFAULT_UNIT_REQUIRED", "defaultUnitId", "A default unit is required before publication."));
        }

        if (defaultUnit is not null && (defaultUnit.SellingPrice ?? 0m) <= 0m)
        {
            errors.Add(new ProductAuthoringValidationError("PRODUCT_DEFAULT_UNIT_PRICE_REQUIRED", "units.sellingPrice", "The default unit must have a positive selling price before publication."));
        }

        if (command.Units.Items.Any(unit => unit.UnitId != Guid.Empty && unit.ConversionFactor <= 0m))
        {
            errors.Add(new ProductAuthoringValidationError("PRODUCT_UNIT_CONVERSION_FACTORS_INVALID", "units", "All authored product units must have valid conversion factors before publication."));
        }

        return errors;
    }

    private static ProductAuthoringCommand Command(
        PublishStatus publishStatus,
        bool hasVariants,
        bool isOnlineVisible,
        Guid? categoryId = null,
        IReadOnlyList<ProductAuthoringUnit>? units = null,
        IReadOnlyList<ProductAuthoringVariant>? variants = null)
    {
        var unitItems = units ?? Array.Empty<ProductAuthoringUnit>();

        return new ProductAuthoringCommand(
            ProductAuthoringOperation.Update,
            new ProductAuthoringIdentity(Guid.NewGuid(), Guid.NewGuid(), categoryId ?? CategoryId, "SKU", "BAR"),
            new ProductAuthoringContent("Test product", null, null, null, null, null, [], [], []),
            new ProductAuthoringPricing(1m, 10m, 9m, false, 0m),
            new ProductAuthoringPublication(publishStatus, true, isOnlineVisible, true, false),
            new ProductAuthoringSeo(null, null, null, null),
            new ProductAuthoringShippingProfile(0m, 0m, 0m, 0m, "kg", "cm"),
            new ProductAuthoringInventoryProfile("Piece", 0m, true, hasVariants, 10, false),
            new ProductAuthoringUnits(
                unitItems.FirstOrDefault(unit => unit.IsBaseUnit)?.UnitId,
                unitItems.FirstOrDefault(unit => unit.IsDefault)?.UnitId,
                unitItems),
            variants ?? Array.Empty<ProductAuthoringVariant>(),
            new ProductAuthoringMetadata(false, null, null, "default", "test-correlation", $"policy-{Guid.NewGuid():N}", null, new Dictionary<string, string>()));
    }

    private static ProductAuthoringUnit Unit(Guid? unitId = null, decimal sellingPrice = 12m) =>
        new(null, unitId ?? UnitId, 1m, 5m, sellingPrice, null, IsDefault: true, IsBaseUnit: true);

    private static ProductAuthoringVariant Variant(
        string sku = "SKU-V",
        decimal price = 12m,
        decimal? onlinePrice = 11m,
        bool isActive = true) =>
        new(Guid.NewGuid(), sku, null, price, onlinePrice, 5m, IsActive: isActive, IsDefault: true, new Dictionary<string, string>());

    private sealed record PolicyCase(
        string Name,
        ProductAuthoringCommand Command,
        bool CategoryIsLive);
}
