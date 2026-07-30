using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.ProductAuthoring;

namespace PointOfSale.Tests.Architecture;

public sealed class ProductAuthoringRestorationTests
{
    [Fact]
    public async Task PublicSimpleProductAuthoring_ProducesBaseAndDefaultUnits()
    {
        var unitId = Guid.NewGuid();
        var command = CreateCommand(
            hasVariants: false,
            publishStatus: PublishStatus.Published,
            isOnlineVisible: true,
            units:
            [
                new ProductAuthoringUnit(null, unitId, 1m, 5m, 12m, null, IsDefault: true, IsBaseUnit: true)
            ]);

        var admission = await EvaluateAdmissionAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, admission.Status);
        Assert.Equal(unitId, command.Units.BaseUnitId);
        Assert.Equal(unitId, command.Units.DefaultUnitId);
        Assert.True(command.Units.Items.Single().IsBaseUnit);
        Assert.True(command.Units.Items.Single().IsDefault);
    }

    [Fact]
    public async Task VariantAuthoring_ProducesRequiredVariantData()
    {
        var variantId = Guid.NewGuid();
        var command = CreateCommand(
            hasVariants: true,
            publishStatus: PublishStatus.Published,
            isOnlineVisible: true,
            variants:
            [
                new ProductAuthoringVariant(
                    variantId,
                    "TSHIRT-RED-M",
                    "1234567890123",
                    15m,
                    14m,
                    8m,
                    IsActive: true,
                    IsDefault: true,
                    new Dictionary<string, string> { ["Color"] = "Red", ["Size"] = "M" })
            ]);

        var admission = await EvaluateAdmissionAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, admission.Status);
        var variant = Assert.Single(command.Variants);
        Assert.Equal(variantId, variant.VariantId);
        Assert.True(variant.IsActive);
        Assert.True(variant.OnlinePrice > 0);
        Assert.Equal("Red", variant.Attributes["Color"]);
    }

    [Fact]
    public async Task DraftProducts_RemainAllowedWithoutPublicationReadiness()
    {
        var command = CreateCommand(
            hasVariants: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: true,
            units: []);

        var admission = await EvaluateAdmissionAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, admission.Status);
        Assert.Empty(admission.Errors);
    }

    [Fact]
    public async Task PublicSimpleProductAuthoring_RequiresPositiveDefaultUnitPrice()
    {
        var unitId = Guid.NewGuid();
        var command = CreateCommand(
            hasVariants: false,
            publishStatus: PublishStatus.Published,
            isOnlineVisible: true,
            units:
            [
                new ProductAuthoringUnit(null, unitId, 1m, 5m, 0m, null, IsDefault: true, IsBaseUnit: true)
            ]);

        var admission = await EvaluateAdmissionAsync(command);

        Assert.Equal(ProductAuthoringStatus.NotReadyForPublication, admission.Status);
        Assert.Contains(admission.Errors, error => error.Code == "PRODUCT_DEFAULT_UNIT_PRICE_REQUIRED");
    }

    [Fact]
    public void ProductAuthoringCommand_ContainsCompleteAuthoringState()
    {
        var unitId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var command = CreateCommand(
            hasVariants: true,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false,
            units:
            [
                new ProductAuthoringUnit(Guid.NewGuid(), unitId, 1m, 9m, 19m, "UNIT-1", true, true)
            ],
            variants:
            [
                new ProductAuthoringVariant(variantId, "SKU-1", "BAR-1", 19m, 18m, 9m, true, true, new Dictionary<string, string>())
            ]);

        Assert.Equal(ProductAuthoringOperation.Create, command.Operation);
        Assert.Equal(unitId, command.Units.BaseUnitId);
        Assert.Equal(unitId, command.Units.DefaultUnitId);
        Assert.Equal(19m, command.Units.Items.Single().SellingPrice);
        Assert.Equal(variantId, command.Variants.Single().VariantId);
        Assert.True(command.Inventory.HasVariants);
        Assert.Equal(PublishStatus.Draft, command.Publication.PublishStatus);
    }

    private static async Task<(ProductAuthoringStatus Status, IReadOnlyList<ProductAuthoringValidationError> Errors)> EvaluateAdmissionAsync(
        ProductAuthoringCommand command)
    {
        var decision = await new ProductAuthoringPolicy(new ProductAuthoringValidator()).EvaluateAdmissionAsync(
            command,
            new ProductAuthoringAdmissionFacts(CategoryIsLive: true));

        return (decision.Status, decision.Errors);
    }

    private static ProductAuthoringCommand CreateCommand(
        bool hasVariants,
        PublishStatus publishStatus,
        bool isOnlineVisible,
        IReadOnlyList<ProductAuthoringUnit>? units = null,
        IReadOnlyList<ProductAuthoringVariant>? variants = null)
    {
        var unitItems = units ?? Array.Empty<ProductAuthoringUnit>();

        return new ProductAuthoringCommand(
            ProductAuthoringOperation.Create,
            new ProductAuthoringIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU", "BAR"),
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
            new ProductAuthoringMetadata(false, null, null, "default", "test-correlation", $"restoration-{Guid.NewGuid():N}", null, new Dictionary<string, string>()));
    }
}
