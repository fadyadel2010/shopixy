using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services.Affiliate;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class ProductAuthoringTransactionCompletenessIntegrationTests
{
    [Fact]
    public async Task OrdinaryProductUpdate_PreservesPersistedAffiliateConfiguration()
    {
        await using var harness = await Harness.CreateAsync();
        var createResult = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            idempotencyKey: "affiliate-preservation-create"));
        Assert.Equal(ProductAuthoringStatus.Accepted, createResult.Status);

        var productId = Assert.IsType<Guid>(createResult.ProductId);
        var affiliateCategory = AffiliateCategory.CreateMainCategory("Electronics", "electronics");
        harness.Context.AffiliateCategories.Add(affiliateCategory);
        await harness.Context.SaveChangesAsync();
        var affiliatePolicy = AffiliateProductPolicy.Create(
            harness.TenantContext.TenantId!.Value,
            productId,
            commissionRate: 0.10m,
            maxPerItem: 25m,
            categoryId: affiliateCategory.Id);
        harness.Context.AffiliateProductPolicies.Add(affiliatePolicy);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var updateResult = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Update,
            idempotencyKey: "affiliate-preservation-update",
            productId: productId,
            productUnitsSubmitted: false,
            name: "Renamed product"));

        Assert.Equal(ProductAuthoringStatus.Accepted, updateResult.Status);
        harness.Context.ChangeTracker.Clear();

        Assert.Equal(
            "Renamed product",
            await harness.Context.Products
                .Where(product => product.Id == productId)
                .Select(product => product.Name)
                .SingleAsync());

        var persistedPolicy = await harness.Context.AffiliateProductPolicies
            .SingleAsync(policy => policy.ProductId == productId);
        Assert.Equal(affiliatePolicy.Id, persistedPolicy.Id);
        Assert.True(persistedPolicy.IsEnabled);
        Assert.Equal(affiliateCategory.Id, persistedPolicy.AffiliateCategoryId);
        Assert.Equal(0.10m, persistedPolicy.CommissionRate);
        Assert.Equal(25m, persistedPolicy.MaxCommissionPerItem);
    }

    [Fact]
    public async Task AffiliatePolicyMutations_PreserveEntireProductDomainSnapshot()
    {
        await using var harness = await Harness.CreateAsync();
        var createResult = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            idempotencyKey: "affiliate-isolation-create"));
        Assert.Equal(ProductAuthoringStatus.Accepted, createResult.Status);

        var productId = Assert.IsType<Guid>(createResult.ProductId);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var variant = await harness.Context.ProductVariants.SingleAsync(item => item.ProductId == productId);
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Affiliate isolation warehouse",
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };
        var stock = new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variant.Id,
            WarehouseId = warehouse.Id,
            Quantity = 8m,
            ReservedQuantity = 1m,
            DamagedQuantity = 2m,
            CreatedAt = DateTime.UtcNow
        };
        var media = new ProductMedia
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = productId,
            MediaType = "image",
            Url = "https://example.invalid/product.jpg",
            Position = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
        var firstCategory = AffiliateCategory.CreateMainCategory("Electronics", "affiliate-electronics");
        var secondCategory = AffiliateCategory.CreateMainCategory("Books & Media", "affiliate-books-media");
        harness.Context.Warehouses.Add(warehouse);
        harness.Context.ProductVariantStocks.Add(stock);
        harness.Context.ProductMedia.Add(media);
        harness.Context.AffiliateCategories.AddRange(firstCategory, secondCategory);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var productBefore = await harness.Context.Products
            .Where(item => item.Id == productId)
            .Select(item => new
            {
                item.IsActive,
                item.PublishStatus,
                item.IsOnlineVisible,
                item.RowVersion,
                item.ProjectionMutationVersion,
                item.UpdatedAt,
                item.PurchasePrice,
                item.SalePrice,
                item.OnlinePrice,
                item.Images
            })
            .SingleAsync();
        var variantsBefore = await harness.Context.ProductVariants
            .Where(item => item.ProductId == productId)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.IsActive,
                item.RowVersion,
                item.ProjectionMutationVersion,
                item.Price,
                item.OnlinePrice
            })
            .ToArrayAsync();
        var stocksBefore = await harness.Context.ProductVariantStocks
            .Where(item => item.Variant.ProductId == productId)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.VariantId,
                item.WarehouseId,
                item.Quantity,
                item.ReservedQuantity,
                item.DamagedQuantity,
                item.ProjectionMutationVersion
            })
            .ToArrayAsync();
        var mediaBefore = await harness.Context.ProductMedia
            .Where(item => item.ProductId == productId)
            .OrderBy(item => item.Position)
            .Select(item => item.Id)
            .ToArrayAsync();
        var outboxCountBefore = await harness.Context.OutboxMessages.CountAsync();

        var service = new AffiliateService(
            harness.Context,
            harness.TenantContext,
            new PersistedOutboxEventPublisher(harness.Context),
            new AffiliateTelemetry(),
            NullLogger<AffiliateService>.Instance,
            new Mock<IAffiliatePayoutService>().Object,
            new Mock<IAffiliateSettlementOrchestrator>().Object,
            Options.Create(new AffiliateSettlementOptions()));

        await service.EnableAffiliateWithCategoryAsync(productId, 0.10m, firstCategory.Id, 25m);
        await service.UpdateCategoryForProductAsync(productId, secondCategory.Id);
        await service.EnableAffiliateWithCategoryAsync(productId, 0.12m, secondCategory.Id, 25m);
        await service.DisableAffiliateForProductAsync(productId);

        Assert.DoesNotContain(
            harness.Context.ChangeTracker.Entries<Product>(),
            entry => entry.State == EntityState.Modified);
        harness.Context.ChangeTracker.Clear();

        var productAfter = await harness.Context.Products
            .Where(item => item.Id == productId)
            .Select(item => new
            {
                item.IsActive,
                item.PublishStatus,
                item.IsOnlineVisible,
                item.RowVersion,
                item.ProjectionMutationVersion,
                item.UpdatedAt,
                item.PurchasePrice,
                item.SalePrice,
                item.OnlinePrice,
                item.Images
            })
            .SingleAsync();
        var variantsAfter = await harness.Context.ProductVariants
            .Where(item => item.ProductId == productId)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.IsActive,
                item.RowVersion,
                item.ProjectionMutationVersion,
                item.Price,
                item.OnlinePrice
            })
            .ToArrayAsync();
        var stocksAfter = await harness.Context.ProductVariantStocks
            .Where(item => item.Variant.ProductId == productId)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.VariantId,
                item.WarehouseId,
                item.Quantity,
                item.ReservedQuantity,
                item.DamagedQuantity,
                item.ProjectionMutationVersion
            })
            .ToArrayAsync();
        var mediaAfter = await harness.Context.ProductMedia
            .Where(item => item.ProductId == productId)
            .OrderBy(item => item.Position)
            .Select(item => item.Id)
            .ToArrayAsync();

        Assert.Equal(productBefore, productAfter);
        Assert.Equal(variantsBefore, variantsAfter);
        Assert.Equal(stocksBefore, stocksAfter);
        Assert.Equal(mediaBefore, mediaAfter);

        var policy = await harness.Context.AffiliateProductPolicies.SingleAsync(item => item.ProductId == productId);
        Assert.False(policy.IsEnabled);
        Assert.Equal(secondCategory.Id, policy.AffiliateCategoryId);
        Assert.Equal(0.12m, policy.CommissionRate);
        Assert.Equal(25m, policy.MaxCommissionPerItem);
        Assert.Equal(outboxCountBefore + 4, await harness.Context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task SaveProduct_PersistsProductUnitsVariantsOutboxAndAuditAtomically()
    {
        await using var harness = await Harness.CreateAsync();
        var command = harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "complete-save",
            variants:
            [
                new ProductAuthoringVariant(null, "VAR-RED", "VAR-RED-BC", 15m, 15m, 7m, true, true, new Dictionary<string, string>
                {
                    ["Color"] = "Red"
                })
            ]);

        var result = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, result.Status);
        Assert.Single(await harness.Context.Products.ToListAsync());
        Assert.Single(await harness.Context.ProductUnits.ToListAsync());
        Assert.Single(await harness.Context.ProductVariants.Where(variant => !variant.IsDeleted).ToListAsync());
        Assert.NotEmpty(await harness.Context.OutboxMessages.ToListAsync());
        var audit = Assert.Single(await harness.Context.AuditLogs.ToListAsync());
        Assert.Equal("ProductAuthoring", audit.Source);
        Assert.Equal("Create", audit.ActionType);

        var marketplaceEvent = Assert.IsType<PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent>(
            Assert.Single(harness.MarketplaceEventOutbox.Events));
        Assert.Equal(command.Metadata.CorrelationId, marketplaceEvent.CorrelationId);
        Assert.Equal(result.ProductId, marketplaceEvent.EntityId);
        Assert.Equal(result.ProductId, marketplaceEvent.AggregateRootId);
        Assert.Equal(harness.TenantContext.TenantId, marketplaceEvent.TenantId);
        Assert.Equal(ProductEventVersionHelper.ToProductEventVersion(result.MutationVersion!.Value), marketplaceEvent.EventVersion);
        Assert.Equal("Complete product", marketplaceEvent.Name);
        Assert.Equal(12m, marketplaceEvent.SalePrice);
        Assert.Null(marketplaceEvent.OnlinePrice);
        Assert.True(marketplaceEvent.HasVariants);
    }

    [Fact]
    public async Task Create_WithDuplicateVariantSku_ReturnsConflictWithoutMutation()
    {
        await using var harness = await Harness.CreateAsync();
        var result = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "duplicate-variant-sku",
            variants:
            [
                new ProductAuthoringVariant(null, "DUPLICATE-SKU", null, 10m, null, 5m, true, true,
                    new Dictionary<string, string> { ["Style"] = "Plain" }),
                new ProductAuthoringVariant(null, "DUPLICATE-SKU", null, 10m, null, 5m, true, false,
                    new Dictionary<string, string> { ["Style"] = "Dark" })
            ]));

        Assert.Equal(ProductAuthoringStatus.Conflict, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_SKU_CONFLICT");
        Assert.Empty(await harness.Context.Products.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await harness.Context.ProductVariants.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Create_WithSkuOwnedByAnotherVariant_ReturnsConflictWithoutSecondMutation()
    {
        await using var harness = await Harness.CreateAsync();
        var first = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "owned-variant-sku-first",
            name: "First product",
            variants:
            [
                new ProductAuthoringVariant(null, "OWNED-SKU", null, 10m, null, 5m, true, true,
                    new Dictionary<string, string> { ["Style"] = "Plain" })
            ]));
        Assert.Equal(ProductAuthoringStatus.Accepted, first.Status);
        harness.Context.ChangeTracker.Clear();

        var conflict = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "owned-variant-sku-second",
            name: "Second product",
            variants:
            [
                new ProductAuthoringVariant(null, "OWNED-SKU", null, 10m, null, 5m, true, true,
                    new Dictionary<string, string> { ["Style"] = "Medium" })
            ]));

        Assert.Equal(ProductAuthoringStatus.Conflict, conflict.Status);
        Assert.Contains(conflict.ValidationErrors, error => error.Code == "PRODUCT_SKU_CONFLICT");
        Assert.Single(await harness.Context.Products.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await harness.Context.ProductVariants.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Remove_WithProtectedVariantStock_ReturnsValidationFailureWithoutMutation()
    {
        await using var harness = await Harness.CreateAsync();
        var create = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "protected-remove-create",
            variants:
            [
                new ProductAuthoringVariant(null, "PROTECTED-REMOVE", null, 10m, null, 5m, true, true,
                    new Dictionary<string, string> { ["Style"] = "Plain" })
            ]));
        Assert.Equal(ProductAuthoringStatus.Accepted, create.Status);

        var variantId = await harness.Context.ProductVariants
            .Where(variant => variant.ProductId == create.ProductId)
            .Select(variant => variant.Id)
            .SingleAsync();
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = harness.TenantContext.TenantId!.Value,
            Name = "Protected remove warehouse"
        };
        harness.Context.AddRange(
            warehouse,
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = harness.TenantContext.TenantId.Value,
                VariantId = variantId,
                WarehouseId = warehouse.Id,
                Quantity = 1m
            });
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var result = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Remove,
            hasVariants: true,
            idempotencyKey: "protected-remove-attempt",
            productId: create.ProductId,
            variantsSubmitted: false,
            productUnitsSubmitted: false,
            publishStatus: PublishStatus.Archived,
            isOnlineVisible: false));

        Assert.Equal(ProductAuthoringStatus.ValidationFailed, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_HAS_STOCK");
        harness.Context.ChangeTracker.Clear();
        Assert.False((await harness.Context.Products.IgnoreQueryFilters()
            .SingleAsync(product => product.Id == create.ProductId)).IsDeleted);
        Assert.False((await harness.Context.ProductVariants.IgnoreQueryFilters()
            .SingleAsync(variant => variant.Id == variantId)).IsDeleted);
    }

    [Fact]
    public async Task Update_OmittingProtectedVariant_BlocksBeforeCreatingReplacement()
    {
        await using var harness = await Harness.CreateAsync();
        var create = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "protected-omission-create",
            variants:
            [
                new ProductAuthoringVariant(null, "STYLE-PLAIN", null, 10m, null, 5m, true, true,
                    new Dictionary<string, string> { ["Style"] = "Plain" }),
                new ProductAuthoringVariant(null, "STYLE-DARK", null, 10m, null, 5m, true, false,
                    new Dictionary<string, string> { ["Style"] = "Dark" })
            ]));
        Assert.Equal(ProductAuthoringStatus.Accepted, create.Status);

        var persisted = await harness.Context.ProductVariants
            .Where(variant => variant.ProductId == create.ProductId)
            .Include(variant => variant.Attributes)
            .ToListAsync();
        var plain = persisted.Single(variant => variant.SKU == "STYLE-PLAIN");
        var dark = persisted.Single(variant => variant.SKU == "STYLE-DARK");
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = harness.TenantContext.TenantId!.Value,
            Name = "Protected omission warehouse"
        };
        harness.Context.AddRange(
            warehouse,
            new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = harness.TenantContext.TenantId.Value,
                VariantId = dark.Id,
                WarehouseId = warehouse.Id,
                Quantity = 1m
            });
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var result = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Update,
            hasVariants: true,
            idempotencyKey: "protected-omission-attempt",
            productId: create.ProductId,
            productUnitsSubmitted: false,
            variants:
            [
                new ProductAuthoringVariant(plain.Id, "STYLE-PLAIN", null, 10m, null, 5m, true, true,
                    new Dictionary<string, string> { ["Style"] = "Plain" }),
                new ProductAuthoringVariant(null, "STYLE-MOJAHED", null, 10m, null, 5m, true, false,
                    new Dictionary<string, string> { ["Style"] = "Mojahed" })
            ]));

        Assert.Equal(ProductAuthoringStatus.ValidationFailed, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "VARIANT_HAS_STOCK");
        harness.Context.ChangeTracker.Clear();
        var after = await harness.Context.ProductVariants.IgnoreQueryFilters()
            .Where(variant => variant.ProductId == create.ProductId)
            .ToListAsync();
        Assert.Equal(2, after.Count);
        Assert.Contains(after, variant => variant.Id == dark.Id && !variant.IsDeleted);
        Assert.DoesNotContain(after, variant => variant.SKU == "STYLE-MOJAHED");
    }

    [Fact]
    public async Task UpdateVariantTemplate_ReusesExistingDefaultVariant_AndPersistsNewAttributes()
    {
        await using var harness = await Harness.CreateAsync();
        var createResult = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "template-default-create",
            variants: [],
            variantsSubmitted: false,
            productUnitsSubmitted: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false));

        Assert.Equal(ProductAuthoringStatus.Accepted, createResult.Status);
        var productId = Assert.IsType<Guid>(createResult.ProductId);
        var defaultVariant = await harness.Context.ProductVariants
            .SingleAsync(variant => variant.ProductId == productId && variant.IsDefault);
        Assert.Empty(await harness.Context.ProductVariantAttributes
            .Where(attribute => attribute.VariantId == defaultVariant.Id)
            .ToListAsync());
        harness.Context.ChangeTracker.Clear();
        IReadOnlyList<EntityState>? observedAttributeStates = null;
        harness.Observer.ObserveAfterVariantSynchronization(() =>
        {
            observedAttributeStates = harness.Context.ChangeTracker
                .Entries<ProductVariantAttribute>()
                .Where(entry => entry.Entity.VariantId == defaultVariant.Id)
                .OrderBy(entry => entry.Entity.Name)
                .Select(entry => entry.State)
                .ToArray();
        });

        var updateResult = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Update,
            hasVariants: true,
            idempotencyKey: "template-default-update",
            productId: productId,
            productUnitsSubmitted: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false,
            variants:
            [
                new ProductAuthoringVariant(
                    defaultVariant.Id,
                    "TEMPLATE-RED-M",
                    "9310731213001",
                    40m,
                    40m,
                    25m,
                    IsActive: true,
                    IsDefault: true,
                    new Dictionary<string, string>
                    {
                        ["Color"] = "Red",
                        ["Size"] = "M"
                    },
                    Weight: 0.5m)
            ]));

        Assert.Equal(ProductAuthoringStatus.Accepted, updateResult.Status);
        Assert.Equal(
            new[] { EntityState.Added, EntityState.Added },
            observedAttributeStates);
        harness.Context.ChangeTracker.Clear();

        var persistedVariant = await harness.Context.ProductVariants
            .Include(variant => variant.Attributes)
            .SingleAsync(variant => variant.ProductId == productId && !variant.IsDeleted);
        Assert.Equal(defaultVariant.Id, persistedVariant.Id);
        Assert.Equal("TEMPLATE-RED-M", persistedVariant.SKU);
        Assert.Equal("9310731213001", persistedVariant.Barcode);
        Assert.Equal(25m, persistedVariant.CostPrice);
        Assert.Equal(40m, persistedVariant.Price);
        Assert.Equal(0.5m, persistedVariant.Weight);
        Assert.Equal(
            new[] { (Name: "Color", Value: "Red"), (Name: "Size", Value: "M") },
            persistedVariant.Attributes
                .OrderBy(attribute => attribute.Name)
                .Select(attribute => (attribute.Name, attribute.Value))
                .ToArray());
    }

    [Fact]
    public async Task CreateVariant_IgnoresClientTemporaryId_AndAssignsServerIdentity()
    {
        await using var harness = await Harness.CreateAsync();
        var clientTemporaryId = Guid.NewGuid();
        var result = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "server-owned-variant-id",
            variants:
            [
                new ProductAuthoringVariant(
                    clientTemporaryId,
                    "SERVER-ID",
                    "SERVER-ID-BC",
                    10m,
                    null,
                    0m,
                    IsActive: true,
                    IsDefault: true,
                    new Dictionary<string, string> { ["Color"] = "Blue" })
            ]));

        Assert.Equal(ProductAuthoringStatus.Accepted, result.Status);
        harness.Context.ChangeTracker.Clear();

        var persisted = await harness.Context.ProductVariants
            .SingleAsync(variant => variant.ProductId == result.ProductId && !variant.IsDeleted);
        Assert.NotEqual(clientTemporaryId, persisted.Id);
        Assert.Equal("SERVER-ID", persisted.SKU);
    }

    [Fact]
    public async Task UpdateVariant_StatusAndPrices_RoundTripExactlyThroughAuthoringPersistence()
    {
        await using var harness = await Harness.CreateAsync();
        var createResult = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "variant-state-price-create",
            variants:
            [
                new ProductAuthoringVariant(
                    null,
                    "ROUNDTRIP",
                    "ROUNDTRIP-BC",
                    400m,
                    375m,
                    350m,
                    IsActive: true,
                    IsDefault: true,
                    new Dictionary<string, string> { ["Color"] = "Black" })
            ]));
        Assert.Equal(ProductAuthoringStatus.Accepted, createResult.Status);

        var productId = createResult.ProductId!.Value;
        var persistedId = await harness.Context.ProductVariants
            .Where(variant => variant.ProductId == productId && !variant.IsDeleted)
            .Select(variant => variant.Id)
            .SingleAsync();
        harness.Context.ChangeTracker.Clear();

        var updateCommand = harness.CreateCommand(
            ProductAuthoringOperation.Update,
            hasVariants: true,
            idempotencyKey: "variant-state-price-update",
            productId: productId,
            productUnitsSubmitted: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false,
            variants:
            [
                new ProductAuthoringVariant(
                    persistedId,
                    "ROUNDTRIP",
                    "ROUNDTRIP-BC",
                    500.25m,
                    425.75m,
                    350m,
                    IsActive: false,
                    IsDefault: true,
                    new Dictionary<string, string> { ["Color"] = "Black" })
            ]);
        var updateResult = await harness.Orchestrator.AuthorAsync(updateCommand);
        Assert.Equal(ProductAuthoringStatus.Accepted, updateResult.Status);
        harness.Context.ChangeTracker.Clear();

        var reopened = await harness.Context.ProductVariants
            .SingleAsync(variant => variant.Id == persistedId && !variant.IsDeleted);
        Assert.False(reopened.IsActive);
        Assert.Equal(500.25m, reopened.Price);
        Assert.Equal(425.75m, reopened.OnlinePrice);
        Assert.Equal(350m, reopened.CostPrice);

        var expectedEventVersion = ProductEventVersionHelper.ToProductEventVersion(reopened.ProjectionMutationVersion);
        var costOnlyResult = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Update,
            hasVariants: true,
            idempotencyKey: "variant-state-cost-only-update",
            productId: productId,
            productUnitsSubmitted: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false,
            variants:
            [
                new ProductAuthoringVariant(
                    persistedId,
                    "ROUNDTRIP",
                    "ROUNDTRIP-BC",
                    500.25m,
                    425.75m,
                    360m,
                    IsActive: false,
                    IsDefault: true,
                    new Dictionary<string, string> { ["Color"] = "Black" })
            ]));
        Assert.Equal(ProductAuthoringStatus.Accepted, costOnlyResult.Status);
        harness.Context.ChangeTracker.Clear();

        var priceOutboxMessage = Assert.Single(await harness.Context.OutboxMessages
            .Where(message => message.EventType == typeof(VariantPriceUpdatedEvent).FullName)
            .ToListAsync());
        var priceUpdatedEvent = JsonSerializer.Deserialize<VariantPriceUpdatedEvent>(priceOutboxMessage.EventPayload)!;

        Assert.Equal(harness.TenantContext.TenantId, priceOutboxMessage.TenantId);
        Assert.Equal(persistedId, priceOutboxMessage.AggregateId);
        Assert.Equal(expectedEventVersion, priceOutboxMessage.EventVersion);
        Assert.Equal(harness.TenantContext.TenantId, priceUpdatedEvent.TenantId);
        Assert.Equal(productId, priceUpdatedEvent.ProductId);
        Assert.Equal(persistedId, priceUpdatedEvent.VariantId);
        Assert.Equal(425.75m, priceUpdatedEvent.NewPrice);
        Assert.Equal(expectedEventVersion, priceUpdatedEvent.EventVersion);
        Assert.Equal(updateCommand.Metadata.CorrelationId, priceUpdatedEvent.CorrelationId);
        Assert.Equal(
            $"product-authoring:{harness.TenantContext.TenantId!.Value:N}:variant-state-price-update:variant-price-updated:{persistedId:N}:{expectedEventVersion}",
            priceUpdatedEvent.IdempotencyKey);
    }

    [Fact]
    public async Task FailureAfterUnitSynchronization_RollsBackAllBusinessOutboxAndAuditRows()
    {
        await using var harness = await Harness.CreateAsync(
            new ThrowingTransactionObserver(ProductAuthoringFailurePoint.AfterUnitSynchronization));
        var command = harness.CreateCommand(ProductAuthoringOperation.Create, idempotencyKey: "fail-after-units");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Orchestrator.AuthorAsync(command));

        await AssertNothingPersistedAsync(harness);
    }

    [Fact]
    public async Task Update_SimpleToVariantFlip_IsRejectedWithoutMutation()
    {
        await using var harness = await Harness.CreateAsync();
        var create = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            idempotencyKey: "type-flip-simple-create",
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false));
        var productId = Assert.IsType<Guid>(create.ProductId);
        harness.Context.ChangeTracker.Clear();

        var before = await harness.Context.ProductVariants
            .Where(variant => variant.ProductId == productId)
            .Select(variant => new { variant.Id, variant.Price, variant.OnlinePrice, variant.CostPrice })
            .SingleAsync();
        var result = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Update,
            hasVariants: true,
            idempotencyKey: "type-flip-simple-update",
            productId: productId,
            productUnitsSubmitted: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false));
        harness.Context.ChangeTracker.Clear();

        Assert.Equal(ProductAuthoringStatus.ValidationFailed, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_TYPE_CONVERSION_UNSUPPORTED");
        Assert.False(await harness.Context.Products.Where(product => product.Id == productId).Select(product => product.HasVariants).SingleAsync());
        var after = await harness.Context.ProductVariants
            .Where(variant => variant.ProductId == productId)
            .Select(variant => new { variant.Id, variant.Price, variant.OnlinePrice, variant.CostPrice })
            .SingleAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Update_VariantToSimpleFlip_IsRejectedWithoutMutation()
    {
        await using var harness = await Harness.CreateAsync();
        var authoredVariant = new ProductAuthoringVariant(
            null,
            "TYPE-FLIP-VARIANT",
            null,
            19m,
            23m,
            7m,
            true,
            true,
            new Dictionary<string, string> { ["Style"] = "Default" });
        var create = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Create,
            hasVariants: true,
            idempotencyKey: "type-flip-variant-create",
            variants: [authoredVariant],
            productUnitsSubmitted: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false));
        var productId = Assert.IsType<Guid>(create.ProductId);
        harness.Context.ChangeTracker.Clear();

        var before = await harness.Context.ProductVariants
            .Where(variant => variant.ProductId == productId)
            .Select(variant => new { variant.Id, variant.Price, variant.OnlinePrice, variant.CostPrice })
            .SingleAsync();
        var result = await harness.Orchestrator.AuthorAsync(harness.CreateCommand(
            ProductAuthoringOperation.Update,
            hasVariants: false,
            idempotencyKey: "type-flip-variant-update",
            productId: productId,
            variantsSubmitted: false,
            publishStatus: PublishStatus.Draft,
            isOnlineVisible: false));
        harness.Context.ChangeTracker.Clear();

        Assert.Equal(ProductAuthoringStatus.ValidationFailed, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_TYPE_CONVERSION_UNSUPPORTED");
        Assert.True(await harness.Context.Products.Where(product => product.Id == productId).Select(product => product.HasVariants).SingleAsync());
        var after = await harness.Context.ProductVariants
            .Where(variant => variant.ProductId == productId)
            .Select(variant => new { variant.Id, variant.Price, variant.OnlinePrice, variant.CostPrice })
            .SingleAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task FailureAfterVariantSynchronization_RollsBackAllBusinessOutboxAndAuditRows()
    {
        await using var harness = await Harness.CreateAsync(
            new ThrowingTransactionObserver(ProductAuthoringFailurePoint.AfterVariantSynchronization));
        var command = harness.CreateCommand(ProductAuthoringOperation.Create, idempotencyKey: "fail-after-variants");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Orchestrator.AuthorAsync(command));

        await AssertNothingPersistedAsync(harness);
    }

    [Fact]
    public async Task FailureBeforeCommit_RollsBackAllBusinessOutboxAndAuditRows()
    {
        await using var harness = await Harness.CreateAsync(
            new ThrowingTransactionObserver(ProductAuthoringFailurePoint.BeforeCommit));
        var command = harness.CreateCommand(ProductAuthoringOperation.Create, idempotencyKey: "fail-before-commit");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Orchestrator.AuthorAsync(command));

        await AssertNothingPersistedAsync(harness);
    }

    [Fact]
    public async Task FailureAfterCommit_RetryWithSameIdempotencyKeyDoesNotDuplicateAuditOrOutbox()
    {
        await using var harness = await Harness.CreateAsync(
            new ThrowingTransactionObserver(ProductAuthoringFailurePoint.AfterCommit));
        var command = harness.CreateCommand(ProductAuthoringOperation.Create, idempotencyKey: "fail-after-commit");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Orchestrator.AuthorAsync(command));
        var auditCountAfterFailure = await harness.Context.AuditLogs.CountAsync();
        var outboxCountAfterFailure = await harness.Context.OutboxMessages.CountAsync();
        harness.Observer.Disable();
        harness.Context.ChangeTracker.Clear();

        var retry = await harness.Orchestrator.AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, retry.Status);
        Assert.Equal(1, auditCountAfterFailure);
        Assert.True(outboxCountAfterFailure > 0);
        Assert.Equal(auditCountAfterFailure, await harness.Context.AuditLogs.CountAsync());
        Assert.Equal(outboxCountAfterFailure, await harness.Context.OutboxMessages.CountAsync());
        Assert.Single(await harness.Context.Products.ToListAsync());
    }

    private static async Task AssertNothingPersistedAsync(Harness harness)
    {
        Assert.Empty(await harness.Context.Products.ToListAsync());
        Assert.Empty(await harness.Context.ProductUnits.ToListAsync());
        Assert.Empty(await harness.Context.ProductVariants.ToListAsync());
        Assert.Empty(await harness.Context.OutboxMessages.ToListAsync());
        Assert.Empty(await harness.Context.AuditLogs.ToListAsync());
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Harness(
            SqliteConnection connection,
            ProductAuthoringCompletenessDbContext context,
            TestTenantContext tenantContext,
            ThrowingTransactionObserver observer,
            Guid categoryId,
            Guid unitId)
        {
            _connection = connection;
            Context = context;
            TenantContext = tenantContext;
            Observer = observer;
            CategoryId = categoryId;
            UnitId = unitId;

            var eventPublisher = new PersistedOutboxEventPublisher(context);
            MarketplaceEventOutbox = new RecordingMarketplaceEventOutbox();
            var variantService = new ProductVariantService(
                context,
                eventPublisher,
                NullLogger<ProductVariantService>.Instance);
            Orchestrator = new ProductAuthoringOrchestrator(
                new ProductAuthoringPolicy(new ProductAuthoringValidator()),
                context,
                tenantContext,
                variantService,
                eventPublisher,
                MarketplaceEventOutbox,
                NullLogger<ProductAuthoringOrchestrator>.Instance,
                new HttpContextAccessor(),
                new ProductMutationIdempotencyService(context),
                observer,
                new ProductAuthoringReconstructionService(context, tenantContext));
        }

        public ProductAuthoringCompletenessDbContext Context { get; }
        public TestTenantContext TenantContext { get; }
        public ProductAuthoringOrchestrator Orchestrator { get; }
        public ThrowingTransactionObserver Observer { get; }
        public RecordingMarketplaceEventOutbox MarketplaceEventOutbox { get; }
        private Guid CategoryId { get; }
        private Guid UnitId { get; }

        public static async Task<Harness> CreateAsync(ThrowingTransactionObserver? observer = null)
        {
            var tenantId = Guid.NewGuid();
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .EnableDetailedErrors()
                .Options;
            var tenantContext = new TestTenantContext(tenantId);
            var context = new ProductAuthoringCompletenessDbContext(options, tenantContext);
            await context.Database.EnsureCreatedAsync();

            var category = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Completeness"
            };
            var unit = new PointOfSale.Core.Entities.Unit
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Piece",
                Symbol = "pc",
                IsActive = true
            };

            context.Categories.Add(category);
            context.Units.Add(unit);
            await context.SaveChangesAsync();

            return new Harness(
                connection,
                context,
                tenantContext,
                observer ?? new ThrowingTransactionObserver(null),
                category.Id,
                unit.Id);
        }

        public ProductAuthoringCommand CreateCommand(
            ProductAuthoringOperation operation,
            bool hasVariants = false,
            string idempotencyKey = "authoring-command",
            IReadOnlyList<ProductAuthoringVariant>? variants = null,
            Guid? productId = null,
            bool? variantsSubmitted = null,
            bool productUnitsSubmitted = true,
            PublishStatus publishStatus = PublishStatus.Published,
            bool isOnlineVisible = true,
            string name = "Complete product")
        {
            IReadOnlyList<ProductAuthoringUnit> units = productUnitsSubmitted
                ? [new ProductAuthoringUnit(null, UnitId, 1m, 5m, 12m, null, true, true)]
                : Array.Empty<ProductAuthoringUnit>();

            return new ProductAuthoringCommand(
                operation,
                new ProductAuthoringIdentity(TenantContext.TenantId!.Value, productId, CategoryId, hasVariants ? null : "SKU-1", hasVariants ? null : "BAR-1"),
                new ProductAuthoringContent(name, null, null, null, "Brand", "Maker", [], [], []),
                new ProductAuthoringPricing(5m, 12m, 12m, false, 0m),
                new ProductAuthoringPublication(publishStatus, true, isOnlineVisible, true, false),
                new ProductAuthoringSeo(null, null, null, null),
                new ProductAuthoringShippingProfile(0m, 0m, 0m, 0m, "kg", "cm"),
                new ProductAuthoringInventoryProfile("Piece", 0m, true, hasVariants, 10, false),
                new ProductAuthoringUnits(
                    productUnitsSubmitted ? UnitId : null,
                    productUnitsSubmitted ? UnitId : null,
                    units),
                variants ?? [],
                new ProductAuthoringMetadata(
                    false,
                    null,
                    null,
                    "default",
                    Guid.NewGuid().ToString("N"),
                    idempotencyKey,
                    null,
                    new Dictionary<string, string>
                    {
                        ["productUnitsSubmitted"] = productUnitsSubmitted ? "true" : "false",
                        ["variantsSubmitted"] = (variantsSubmitted ?? hasVariants) ? "true" : "false"
                    }));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ProductAuthoringCompletenessDbContext : ApplicationDbContext
    {
        public ProductAuthoringCompletenessDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var keepTypes = new HashSet<Type>
            {
                typeof(Category),
                typeof(Product),
                typeof(PointOfSale.Core.Entities.Unit),
                typeof(ProductUnit),
                typeof(ProductVariant),
                typeof(ProductVariantAttribute),
                typeof(ProductVariantStock),
                typeof(ProductMedia),
                typeof(Warehouse),
                typeof(AffiliateCategory),
                typeof(AffiliateProductPolicy),
                typeof(AuditLog),
                typeof(PointOfSale.Core.Entities.OutboxMessage),
                typeof(ProductRequestIdempotency)
            };

            var discoveredEntityTypes = typeof(ApplicationDbContext)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.PropertyType.IsGenericType &&
                    property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(property => property.PropertyType.GetGenericArguments()[0])
                .Where(entityType => !keepTypes.Contains(entityType))
                .Distinct()
                .ToArray();

            foreach (var entityType in discoveredEntityTypes)
            {
                modelBuilder.Ignore(entityType);
            }

            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("Categories");
                entity.HasKey(category => category.Id);
                entity.Property(category => category.Name).IsRequired();
                entity.Ignore(category => category.ParentCategory);
                entity.Ignore(category => category.SubCategories);
                entity.Ignore(category => category.Products);
            });

            modelBuilder.Entity<PointOfSale.Core.Entities.Unit>(entity =>
            {
                entity.ToTable("Units");
                entity.HasKey(unit => unit.Id);
                entity.Property(unit => unit.Name).IsRequired();
                entity.Property(unit => unit.Symbol).IsRequired();
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("Products");
                entity.HasKey(product => product.Id);
                entity.Property(product => product.Name).IsRequired();
                entity.Property(product => product.Unit).IsRequired();
                entity.Property(product => product.WeightUnit).IsRequired();
                entity.Property(product => product.DimensionUnit).IsRequired();
                entity.HasOne(product => product.Category)
                    .WithMany()
                    .HasForeignKey(product => product.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Ignore(product => product.WarehouseStocks);
            });

            modelBuilder.Entity<ProductUnit>(entity =>
            {
                entity.ToTable("ProductUnits");
                entity.HasKey(productUnit => productUnit.Id);
                entity.HasOne(productUnit => productUnit.Product)
                    .WithMany(product => product.ProductUnits)
                    .HasForeignKey(productUnit => productUnit.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(productUnit => productUnit.Unit)
                    .WithMany()
                    .HasForeignKey(productUnit => productUnit.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductVariant>(entity =>
            {
                entity.ToTable("ProductVariants");
                entity.HasKey(variant => variant.Id);
                entity.HasOne(variant => variant.Product)
                    .WithMany(product => product.Variants)
                    .HasForeignKey(variant => variant.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.Ignore(variant => variant.Media);
            });

            modelBuilder.Entity<Warehouse>(entity =>
            {
                entity.ToTable("Warehouses");
                entity.HasKey(warehouse => warehouse.Id);
                entity.Property(warehouse => warehouse.Name).IsRequired();
                entity.Ignore(warehouse => warehouse.Stock);
                entity.Ignore(warehouse => warehouse.StockMovements);
            });

            modelBuilder.Entity<ProductVariantStock>(entity =>
            {
                entity.ToTable("ProductVariantStocks");
                entity.HasKey(stock => stock.Id);
                entity.Ignore(stock => stock.AvailableQuantity);
                entity.Ignore(stock => stock.TotalQuantity);
                entity.HasOne(stock => stock.Variant)
                    .WithMany(variant => variant.Stocks)
                    .HasForeignKey(stock => stock.VariantId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(stock => stock.Warehouse)
                    .WithMany()
                    .HasForeignKey(stock => stock.WarehouseId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductMedia>(entity =>
            {
                entity.ToTable("ProductMedia");
                entity.HasKey(media => media.Id);
                entity.Property(media => media.MediaType).IsRequired();
                entity.Property(media => media.Url).IsRequired();
                entity.HasOne(media => media.Product)
                    .WithMany(product => product.Media)
                    .HasForeignKey(media => media.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProductVariantAttribute>(entity =>
            {
                entity.ToTable("ProductVariantAttributes");
                entity.HasKey(attribute => attribute.Id);
                entity.HasOne(attribute => attribute.Variant)
                    .WithMany(variant => variant.Attributes)
                    .HasForeignKey(attribute => attribute.VariantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AffiliateCategory>(entity =>
            {
                entity.ToTable("AffiliateCategories");
                entity.HasKey(category => category.Id);
                entity.Property(category => category.Name).IsRequired();
                entity.Property(category => category.Slug).IsRequired();
            });

            modelBuilder.Entity<AffiliateProductPolicy>(entity =>
            {
                entity.ToTable("AffiliateProductPolicies");
                entity.HasKey(policy => policy.Id);
                entity.HasIndex(policy => new { policy.TenantId, policy.ProductId }).IsUnique();
                entity.Property(policy => policy.CommissionRate).HasPrecision(8, 6);
                entity.Property(policy => policy.MaxCommissionPerItem).HasPrecision(18, 4);
                entity.Property(policy => policy.TemplateType).IsRequired();
                entity.HasOne<Product>()
                    .WithMany()
                    .HasForeignKey(policy => policy.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<AffiliateCategory>()
                    .WithMany()
                    .HasForeignKey(policy => policy.AffiliateCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(audit => audit.LogId);
                entity.Property(audit => audit.LogId).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<PointOfSale.Core.Entities.OutboxMessage>(entity =>
            {
                entity.ToTable("OutboxMessages");
                entity.HasKey(message => message.Id);
                entity.Property(message => message.EventType).IsRequired();
                entity.Property(message => message.EventPayload).IsRequired();
            });

            modelBuilder.Entity<ProductRequestIdempotency>(entity =>
            {
                entity.ToTable("ProductRequestIdempotency");
                entity.HasKey(record => record.Id);
                entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation }).IsUnique();
                entity.Property(record => record.Key).IsRequired();
                entity.Property(record => record.Operation).IsRequired();
                entity.Property(record => record.RequestFingerprint).IsRequired();
            });
        }
    }

    private enum ProductAuthoringFailurePoint
    {
        AfterUnitSynchronization,
        AfterVariantSynchronization,
        BeforeCommit,
        AfterCommit
    }

    private sealed class ThrowingTransactionObserver(ProductAuthoringFailurePoint? failurePoint) : IProductAuthoringTransactionObserver
    {
        private ProductAuthoringFailurePoint? _failurePoint = failurePoint;
        private Action? _afterVariantSynchronization;

        public void Disable() => _failurePoint = null;

        public void ObserveAfterVariantSynchronization(Action observer) =>
            _afterVariantSynchronization = observer;

        public Task AfterUnitSynchronizationAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            ThrowIf(ProductAuthoringFailurePoint.AfterUnitSynchronization);

        public Task AfterVariantSynchronizationAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken)
        {
            _afterVariantSynchronization?.Invoke();
            return ThrowIf(ProductAuthoringFailurePoint.AfterVariantSynchronization);
        }

        public Task BeforeCommitAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            ThrowIf(ProductAuthoringFailurePoint.BeforeCommit);

        public Task AfterCommitAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            ThrowIf(ProductAuthoringFailurePoint.AfterCommit);

        private Task ThrowIf(ProductAuthoringFailurePoint candidate)
        {
            if (_failurePoint == candidate)
            {
                throw new InvalidOperationException($"Injected product authoring failure at {candidate}.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class PersistedOutboxEventPublisher(ApplicationDbContext context) : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Persist(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                Persist(@event);
            }

            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            PublishAsync(@event, cancellationToken);

        private void Persist(IntegrationEvent @event)
        {
            var type = @event.GetType();
            context.OutboxMessages.Add(new PointOfSale.Core.Entities.OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = type.FullName ?? type.Name,
                EventPayload = JsonSerializer.Serialize(@event, type),
                TenantId = ReadGuid(@event, "TenantId"),
                AggregateId = @event is IAggregateScopedIntegrationEvent aggregateScopedEvent
                    ? aggregateScopedEvent.GetAuthoritativeAggregateId()
                    : ReadGuid(@event, "ProductId") ?? ReadGuid(@event, "VariantId"),
                EventVersion = ReadInt(@event, "EventVersion"),
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        private static Guid? ReadGuid(object value, string propertyName) =>
            value.GetType().GetProperty(propertyName)?.GetValue(value) is Guid guid ? guid : null;

        private static int ReadInt(object value, string propertyName) =>
            value.GetType().GetProperty(propertyName)?.GetValue(value) is int intValue ? intValue : 0;
    }

    private sealed class RecordingMarketplaceEventOutbox : IMarketplaceEventOutboxService
    {
        public List<PointOfSale.Core.Events.Marketplace.IMarketplaceDomainEvent> Events { get; } = [];

        public Task EnqueueAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, PointOfSale.Core.Events.Marketplace.IMarketplaceDomainEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}
