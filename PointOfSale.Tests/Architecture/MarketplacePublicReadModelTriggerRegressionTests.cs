using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.CommerceHub.Handlers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.ReadModels.Marketplace;

namespace PointOfSale.Tests.Architecture;

public sealed class MarketplacePublicReadModelTriggerRegressionTests
{
    [Fact]
    public async Task ListingTrigger_StagesProductScopedMarketplaceEventWithProductAuthorityVersion()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId
        };
        product.AdvanceProjectionMutationVersion(17);
        await using var context = CreateDbContext();
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            commerceHubPrice: null);
        var outbox = new Mock<IMarketplaceEventOutboxService>();
        ProductUpdatedEvent? stagedEvent = null;
        outbox
            .Setup(service => service.EnqueueAsync(
                It.IsAny<ProductUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProductUpdatedEvent, CancellationToken>((@event, _) => stagedEvent = @event)
            .Returns(Task.CompletedTask);

        await PublishToCommerceHubHandler.StageMarketplaceProductReadModelRebuildAsync(
            context,
            outbox.Object,
            product,
            listing,
            "listing-correlation",
            "CommerceHubListingCreated",
            CancellationToken.None);

        Assert.NotNull(stagedEvent);
        Assert.Equal(tenantId, stagedEvent.TenantId);
        Assert.Equal(productId, stagedEvent.EntityId);
        Assert.Equal(productId, stagedEvent.AggregateRootId);
        Assert.Equal(18, stagedEvent.EventVersion);
        Assert.Equal(18, product.ProjectionMutationVersion);
        Assert.Equal("listing-correlation", stagedEvent.CorrelationId);
        Assert.Equal(["CommerceHubListingCreated"], stagedEvent.ChangedFields);
    }

    [Fact]
    public async Task ListingUnpublish_AfterHigherStockVersion_AdvancesProductStreamAndDeletesReadModel()
    {
        await using var context = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Versioned Vendor",
            Currency = "EGP",
            Status = TenantStatus.Active
        };
        var warehouse = new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Versioned Warehouse",
            CreatedAt = occurredAtUtc
        };
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Versioned Product",
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            HasVariants = true,
            CreatedAt = occurredAtUtc
        };
        product.AdvanceProjectionMutationVersion(3);
        var variant = new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            Product = product,
            IsActive = true,
            IsDefault = true,
            CreatedAt = occurredAtUtc
        };
        variant.AdvanceProjectionMutationVersion(12);
        var stock = new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            Variant = variant,
            WarehouseId = warehouseId,
            Quantity = 1,
            CreatedAt = occurredAtUtc
        };
        stock.AdvanceProjectionMutationVersion(30);
        product.Variants = [variant];
        variant.Stocks = [stock];
        var listing = CommerceHubListing.Create(
            tenantId,
            productId,
            variantId,
            Guid.NewGuid(),
            commerceHubPrice: null);

        context.AddRange(tenant, warehouse, product, listing);
        await context.SaveChangesAsync();
        Assert.True(listing.Unpublish());

        ProductUpdatedEvent? stagedEvent = null;
        var outbox = new Mock<IMarketplaceEventOutboxService>();
        outbox
            .Setup(service => service.EnqueueAsync(
                It.IsAny<ProductUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<ProductUpdatedEvent, CancellationToken>((@event, _) => stagedEvent = @event)
            .Returns(Task.CompletedTask);

        await PublishToCommerceHubHandler.StageMarketplaceProductReadModelRebuildAsync(
            context,
            outbox.Object,
            product,
            listing,
            "unpublish-after-stock",
            "CommerceHubListingUnpublished",
            CancellationToken.None);
        await context.SaveChangesAsync();

        Assert.NotNull(stagedEvent);
        Assert.Equal(31, stagedEvent.EventVersion);
        Assert.Equal(31, product.ProjectionMutationVersion);

        var repository = new Mock<IMarketplaceProductReadRepository>();
        repository
            .Setup(candidate => candidate.DeleteIfFreshAsync(
                tenantId,
                productId,
                stagedEvent.OccurredAtUtc,
                31,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionWriteResult.Applied);
        var projection = new MarketplaceProductReadModelProjectionService(
            context,
            repository.Object,
            NullLogger<MarketplaceProductReadModelProjectionService>.Instance);

        var result = await projection.RebuildAsync(
            productId,
            tenantId,
            stagedEvent.EventId,
            stagedEvent.OccurredAtUtc,
            stagedEvent.EventVersion);

        Assert.Equal(MarketplaceProjectionResult.Deleted, result);
        repository.Verify(candidate => candidate.DeleteIfFreshAsync(
            tenantId,
            productId,
            stagedEvent.OccurredAtUtc,
            31,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AuthoritativeMutationBoundaries_MustStageMarketplaceRebuildBeforeCommit()
    {
        var handlers = ReadRepoFile("PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs");
        var bulk = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");
        var governance = ReadRepoFile("PointOfSale.API/SuperAdmin/Controllers/SuperAdminMarketplaceGovernanceController.cs");
        var vendorVisibility = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubVendorVisibilityService.cs");
        var media = ReadRepoFile("PointOfSale.API/Services/ProductMediaService.cs");
        var stock = ReadRepoFile("PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs");

        Assert.Equal(5, Count(handlers, "StageMarketplaceProductReadModelRebuildAsync("));
        Assert.Contains("CommerceHubListingCreated", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingPublished", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingUpdated", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingUnpublished", handlers, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingBulk:{operation}", bulk, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingGovernanceSuppressed", governance, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingGovernanceRestored", governance, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingGovernanceHidden", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("CommerceHubListingGovernanceRestored", vendorVisibility, StringComparison.Ordinal);
        Assert.Contains("_marketplaceEventOutbox.EnqueueAsync", media, StringComparison.Ordinal);
        Assert.Contains("ChangedFields = [\"Media\"]", media, StringComparison.Ordinal);
        Assert.Contains("MarketplaceEventOutboxMessage.Create(marketplaceEvent, marketplacePayload)", stock, StringComparison.Ordinal);
        Assert.Contains("ChangedFields = [\"Stock\"]", stock, StringComparison.Ordinal);

        Assert.True(
            handlers.LastIndexOf("StageMarketplaceProductReadModelRebuildAsync(", StringComparison.Ordinal) <
            handlers.LastIndexOf("_dbContext.SaveChangesAsync", StringComparison.Ordinal));
        AssertCallsPrecedeNextSave(
            media,
            "await PublishMediaChangedEventAsync(",
            "_context.SaveChangesAsync",
            expectedCalls: 3);
        Assert.True(
            stock.LastIndexOf("MarketplaceEventOutboxMessage.Create(marketplaceEvent, marketplacePayload)", StringComparison.Ordinal) <
            stock.LastIndexOf("context.SaveChangesAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void MediaConsumer_MustRecomposeWhenSqlHasAdvancedBeyondTheMediaTrigger()
    {
        var consumer = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs");

        Assert.Contains("ProjectionAuthorityValidator.ValidateSqlVersion", consumer, StringComparison.Ordinal);
        Assert.Contains("requireExactVersion: false", consumer, StringComparison.Ordinal);
        Assert.Contains("@event.Media = sqlAuthority.Media", consumer, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkListingRows_MustCommitOrRollbackTheirMutationAndOutboxTogether()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/AdminCommerceHubController.cs");

        Assert.Contains("BeginTransactionAsync(cancellationToken)", controller, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(cancellationToken)", controller, StringComparison.Ordinal);
        Assert.Contains("await transaction.RollbackAsync(cancellationToken)", controller, StringComparison.Ordinal);
        Assert.Contains("_dbContext.ChangeTracker.Clear()", controller, StringComparison.Ordinal);
        Assert.True(
            controller.IndexOf("StageMarketplaceProductReadModelRebuildAsync(", StringComparison.Ordinal) <
            controller.IndexOf("await transaction.CommitAsync(cancellationToken)", StringComparison.Ordinal));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void AssertCallsPrecedeNextSave(
        string source,
        string call,
        string save,
        int expectedCalls)
    {
        var calls = 0;
        var index = 0;
        while ((index = source.IndexOf(call, index, StringComparison.Ordinal)) >= 0)
        {
            var saveIndex = source.IndexOf(save, index, StringComparison.Ordinal);
            Assert.True(saveIndex > index, $"Expected {save} after {call}.");
            calls++;
            index += call.Length;
        }

        Assert.Equal(expectedCalls, calls);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return File.ReadAllText(
                    Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
