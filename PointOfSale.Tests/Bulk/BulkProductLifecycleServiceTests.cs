using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.API.Services.ProductAuthoring.Bulk;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Core.Interfaces.ProductAuthoring;

namespace PointOfSale.Tests.Bulk;

public sealed class BulkProductLifecycleServiceTests
{
    [Fact]
    public async Task Activate_AlreadyActiveProduct_ReturnsAlreadyActiveWithoutOrchestratorCall()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);

        var productId = harness.PreparedProductId;
        var request = new BulkProductRequest([
            new BulkProductRequestItem(productId.ToString(), "7")
        ]);

        var (service, orchestratorMock) = CreateService(harness, out var orchMock);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "activate-already-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.OperationResult);
        Assert.Equal(1, outcome.OperationResult.Accepted);
        Assert.Equal(1, outcome.OperationResult.Unchanged);
        Assert.Equal(0, outcome.OperationResult.Processed);
        Assert.Equal(0, outcome.OperationResult.Failed);

        var itemResult = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Unchanged, itemResult.Status);
        Assert.Equal(BulkErrorCodes.AlreadyActive, itemResult.Code);

        orchMock.Verify(x => x.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactiveProduct_ReturnsAlreadyInactiveWithoutOrchestratorCall()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);

        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        var rowVersion = 4u;
        // After SaveChanges, RowVersion is auto-incremented to 5 for non-PostgreSQL
        var expectedVersion = "5";

        harness.Context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Already Inactive Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = false,
            PublishStatus = PublishStatus.Archived,
            IsOnlineVisible = false,
            IsDeleted = false,
            RowVersion = rowVersion
        });
        await harness.Context.SaveChangesAsync();

        var request = new BulkProductRequest([
            new BulkProductRequestItem(productId.ToString(), expectedVersion)
        ]);

        var (service, orchMock) = CreateService(harness, out var orchestratorMock);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            "deactivate-already-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.OperationResult);
        Assert.Equal(1, outcome.OperationResult.Unchanged);

        var itemResult = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Unchanged, itemResult.Status);
        Assert.Equal(BulkErrorCodes.AlreadyInactive, itemResult.Code);

        orchestratorMock.Verify(x => x.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Activate_PreparedProduct_CallsOrchestratorAndReturnsProcessed()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);

        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        var rowVersion = 3u;
        // After SaveChanges, RowVersion is auto-incremented to 4 for non-PostgreSQL
        var expectedVersion = "4";

        harness.Context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Inactive Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = false,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            IsDeleted = false,
            RowVersion = rowVersion
        });
        await harness.Context.SaveChangesAsync();

        var request = new BulkProductRequest([
            new BulkProductRequestItem(productId.ToString(), expectedVersion)
        ]);

        var (service, orchestratorMock) = CreateService(
            harness,
            out var orchMock,
            authoringResult: ProductAuthoringResult.Accepted(
                productId,
                PublishStatus.Published,
                "corr-1",
                mutationVersion: 42L));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "activate-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.OperationResult);
        Assert.Equal(1, outcome.OperationResult.Processed);

        var itemResult = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Processed, itemResult.Status);
        Assert.Equal(expectedVersion, itemResult.PreviousVersion);
        Assert.Equal(expectedVersion, itemResult.NewVersion);

        orchestratorMock.Verify(x => x.AuthorAsync(
                It.Is<ProductAuthoringCommand>(cmd =>
                    cmd.Operation == ProductAuthoringOperation.Update &&
                    cmd.Publication.IsActive == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Deactivate_PreparedProduct_CallsOrchestratorWithArchive()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);

        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        var rowVersion = 5u;
        // After SaveChanges, RowVersion is auto-incremented to 6 for non-PostgreSQL
        var expectedVersion = "6";

        harness.Context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Active Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = true,
            PublishStatus = PublishStatus.Published,
            IsOnlineVisible = true,
            IsDeleted = false,
            RowVersion = rowVersion
        });
        await harness.Context.SaveChangesAsync();

        var request = new BulkProductRequest([
            new BulkProductRequestItem(productId.ToString(), expectedVersion)
        ]);

        var (service, orchestratorMock) = CreateService(
            harness,
            out var orchMock,
            authoringResult: ProductAuthoringResult.Accepted(
                productId,
                PublishStatus.Archived,
                "corr-2"));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            "deactivate-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.OperationResult);
        Assert.Equal(1, outcome.OperationResult.Processed);

        orchestratorMock.Verify(x => x.AuthorAsync(
                It.Is<ProductAuthoringCommand>(cmd =>
                    cmd.Operation == ProductAuthoringOperation.Archive),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Delete_ValidProduct_ValidatesAndAuthorsRemove()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var validation = CreateValidationMock();
        var accepted = ProductAuthoringResult.Accepted(
            harness.PreparedProductId,
            PublishStatus.Archived,
            "delete-correlation");
        var (service, orchestrator) = CreateService(
            harness,
            out _,
            accepted,
            validation);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            new BulkProductRequest([
                new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
            ]),
            "delete-valid-key");

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        Assert.Equal(1, harness.QueryCounter.ProductSelectCount);
        validation.Verify(service => service.CanDeleteProductAsync(
            harness.PreparedProductId,
            It.IsAny<CancellationToken>()), Times.Once);
        orchestrator.Verify(service => service.AuthorAsync(
            It.Is<ProductAuthoringCommand>(command =>
                command.Operation == ProductAuthoringOperation.Remove &&
                command.Metadata.ExpectedVersion == 7 &&
                command.Metadata.IdempotencyKey!.StartsWith("bulk-item:", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("PRODUCT_HAS_STOCK")]
    [InlineData("PRODUCT_USED_IN_PURCHASES")]
    [InlineData("PRODUCT_USED_IN_SALES")]
    [InlineData("PRODUCT_USED_IN_RETURNS")]
    [InlineData("PRODUCT_USED_IN_TRANSFERS")]
    [InlineData("PRODUCT_HAS_MOVEMENTS")]
    public async Task Delete_AuthoritativeDependencyFailure_IsStableAndDoesNotAuthor(string authorityCode)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var validation = CreateValidationMock(ValidationResult.Failure(
            authorityCode,
            "Internal authority detail.",
            "Internal authority detail."));
        var (service, orchestrator) = CreateService(
            harness,
            out _,
            validationMock: validation);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            new BulkProductRequest([
                new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
            ]),
            $"delete-blocked-{authorityCode}");

        var result = Assert.Single(outcome.OperationResult!.Results);
        Assert.Equal(BulkItemStatus.Failed, result.Status);
        Assert.Equal(BulkErrorCodes.DependencyBlocked, result.Code);
        Assert.Equal("Product cannot be deleted because it has blocking dependencies.", result.Message);
        Assert.DoesNotContain("Internal", result.Message, StringComparison.Ordinal);
        orchestrator.Verify(service => service.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_MixedBatch_ProcessesValidItemsAndClassifiesFailuresIndependently()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var validProducts = new[]
        {
            new Product
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CategoryId = Guid.NewGuid(),
                Name = "Bulk delete valid one", Unit = "Piece", WeightUnit = "kg",
                DimensionUnit = "cm", IsActive = true, RowVersion = 20
            },
            new Product
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CategoryId = Guid.NewGuid(),
                Name = "Bulk delete valid two", Unit = "Piece", WeightUnit = "kg",
                DimensionUnit = "cm", IsActive = true, RowVersion = 30
            }
        };
        harness.Context.Products.AddRange(validProducts);
        await harness.Context.SaveChangesAsync();
        var validVersions = validProducts.ToDictionary(product => product.Id, product => product.RowVersion);
        harness.Context.ChangeTracker.Clear();

        var validation = CreateValidationMock();
        validation.Setup(service => service.CanDeleteProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid productId, CancellationToken _) =>
                productId == harness.PreparedProductId
                    ? ValidationResult.Failure("PRODUCT_HAS_STOCK", "internal", "internal")
                    : ValidationResult.Success());
        var (service, orchestrator) = CreateService(
            harness,
            out _,
            ProductAuthoringResult.Accepted(Guid.NewGuid(), PublishStatus.Archived, "mixed-delete"),
            validation);
        var request = new BulkProductRequest([
            new BulkProductRequestItem(validProducts[0].Id.ToString(), validVersions[validProducts[0].Id].ToString()),
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7"),
            new BulkProductRequestItem(harness.ConflictProductId.ToString(), "7"),
            new BulkProductRequestItem(harness.ForeignProductId.ToString(), "11"),
            new BulkProductRequestItem(validProducts[1].Id.ToString(), validVersions[validProducts[1].Id].ToString())
        ]);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            "delete-mixed-key");

        Assert.Equal(2, outcome.OperationResult!.Processed);
        Assert.Equal(3, outcome.OperationResult.Failed);
        Assert.Contains(outcome.OperationResult.Results, result =>
            result.ProductId == harness.PreparedProductId && result.Code == BulkErrorCodes.DependencyBlocked);
        Assert.Contains(outcome.OperationResult.Results, result =>
            result.ProductId == harness.ConflictProductId && result.Code == BulkErrorCodes.ConcurrencyConflict);
        Assert.Contains(outcome.OperationResult.Results, result =>
            result.ProductId == harness.ForeignProductId && result.Code == BulkErrorCodes.NotFound);
        orchestrator.Verify(service => service.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_NullIdempotencyKey_ReturnsRequestValidationFailed()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        // After SaveChanges, RowVersion is auto-incremented from 6 to 7
        var request = new BulkProductRequest([
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
        ]);

        var (service, _) = CreateService(harness, out _);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            null!,
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.RequestValidationFailed, outcome.Kind);
        Assert.NotNull(outcome.ValidationErrors);
        Assert.Contains(outcome.ValidationErrors, e => e.Code == BulkErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task ExecuteLifecycleAsync_UnsupportedOperation_ThrowsNotSupportedException()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var request = new BulkProductRequest([
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
        ]);

        var (service, _) = CreateService(harness, out _);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await service.ExecuteLifecycleAsync(
                (BulkOperationType)999,
                request,
                "delete-key",
                default));
    }

    [Fact]
    public async Task Activate_OrchestratorReturnsConcurrencyConflict_MapsToConcurrencyConflictItem()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);

        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        var rowVersion = 2u;
        // After SaveChanges, RowVersion is auto-incremented to 3 for non-PostgreSQL
        var expectedVersion = "3";

        harness.Context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Concurrency Test Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = false,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            IsDeleted = false,
            RowVersion = rowVersion
        });
        await harness.Context.SaveChangesAsync();

        var request = new BulkProductRequest([
            new BulkProductRequestItem(productId.ToString(), expectedVersion)
        ]);

        var (service, orchestratorMock) = CreateService(
            harness,
            out var orchMock,
            authoringResult: ProductAuthoringResult.InfrastructurePrepared("corr-3"));

        orchestratorMock.Setup(x => x.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductAuthoringResult(
                ProductAuthoringStatus.ConcurrencyConflict,
                productId,
                null,
                PublishStatus.Draft,
                Array.Empty<ProductAuthoringValidationError>(),
                "corr-3",
                new Dictionary<string, string>()));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "concurrency-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.NotNull(outcome.OperationResult);

        var itemResult = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Failed, itemResult.Status);
        Assert.Equal(BulkErrorCodes.ConcurrencyConflict, itemResult.Code);
    }

    [Fact]
    public async Task Activate_OrchestratorReturnsNotFound_MapsToNotFoundItem()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);

        var tenantId = harness.TenantContext.TenantId!.Value;
        var productId = Guid.NewGuid();
        var rowVersion = 1u;
        // After SaveChanges, RowVersion is auto-incremented to 2 for non-PostgreSQL
        var expectedVersion = "2";

        harness.Context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "NotFound Test Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = false,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            IsDeleted = false,
            RowVersion = rowVersion
        });
        await harness.Context.SaveChangesAsync();

        var request = new BulkProductRequest([
            new BulkProductRequestItem(productId.ToString(), expectedVersion)
        ]);

        var (service, orchestratorMock) = CreateService(
            harness,
            out var orchMock,
            authoringResult: ProductAuthoringResult.InfrastructurePrepared("corr-4"));

        var notFoundResult = new ProductAuthoringResult(
            ProductAuthoringStatus.NotFound,
            productId,
            null,
            null,
            Array.Empty<ProductAuthoringValidationError>(),
            "corr-4",
            new Dictionary<string, string>());

        orchestratorMock.Setup(x => x.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFoundResult);

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "notfound-key",
            default);

        var itemResult = Assert.Single(outcome.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Failed, itemResult.Status);
        Assert.Equal(BulkErrorCodes.NotFound, itemResult.Code);
    }

    [Fact]
    public async Task Activate_ArchivedProduct_UsesUpdateAndRestoresDraftHiddenState()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var product = await SeedProductAsync(
            harness,
            tenantId,
            isActive: false,
            PublishStatus.Archived,
            isOnlineVisible: false);
        ProductAuthoringCommand? captured = null;
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAuthoringCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(ProductAuthoringResult.Accepted(product.Id, PublishStatus.Draft, "archived-activate", 9));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(product),
            "activate-archived-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(ProductAuthoringOperation.Update, captured!.Operation);
        Assert.True(captured.Publication.IsActive);
        Assert.Equal(PublishStatus.Draft, captured.Publication.PublishStatus);
        Assert.False(captured.Publication.IsOnlineVisible);
        Assert.Equal(product.RowVersion, captured.Metadata.ExpectedVersion);
        Assert.StartsWith("bulk-item:", captured.Metadata.IdempotencyKey, StringComparison.Ordinal);
        Assert.DoesNotContain("activate-archived-key", captured.Metadata.IdempotencyKey, StringComparison.Ordinal);
        Assert.True(captured.Metadata.IdempotencyKey!.Length <= 200);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activate_InactivePublishedProduct_PreservesExistingVisibility(bool isOnlineVisible)
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness,
            harness.TenantContext.TenantId!.Value,
            isActive: false,
            PublishStatus.Published,
            isOnlineVisible);
        ProductAuthoringCommand? captured = null;
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ProductAuthoringCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(ProductAuthoringResult.Accepted(product.Id, PublishStatus.Published, "published-activate", 10));

        await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(product),
            "activate-published-key",
            default);

        Assert.Equal(PublishStatus.Published, captured!.Publication.PublishStatus);
        Assert.Equal(isOnlineVisible, captured.Publication.IsOnlineVisible);
        Assert.True(captured.Publication.IsActive);
    }

    [Fact]
    public async Task Deactivate_StaleAAndCurrentB_ProcessesBWithoutCallingA()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var request = new BulkProductRequest(
        [
            new BulkProductRequestItem(harness.ConflictProductId.ToString(), "7"),
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
        ]);
        var (service, orchestrator) = CreateService(
            harness,
            out _,
            ProductAuthoringResult.Accepted(
                harness.PreparedProductId,
                PublishStatus.Archived,
                "mixed-deactivate",
                11));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            "mixed-deactivate-key",
            default);

        Assert.Equal(1, outcome.OperationResult!.Processed);
        Assert.Equal(1, outcome.OperationResult.Failed);
        Assert.Equal(BulkErrorCodes.ConcurrencyConflict, outcome.OperationResult.Results[0].Code);
        Assert.Equal(BulkItemStatus.Processed, outcome.OperationResult.Results[1].Status);
        orchestrator.Verify(item => item.AuthorAsync(
            It.Is<ProductAuthoringCommand>(command =>
                command.Identity.ProductId == harness.PreparedProductId &&
                command.Operation == ProductAuthoringOperation.Archive),
            It.IsAny<CancellationToken>()), Times.Once);
        orchestrator.Verify(item => item.AuthorAsync(
            It.Is<ProductAuthoringCommand>(command => command.Identity.ProductId == harness.ConflictProductId),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_UnexpectedItemFailure_RecordsFailureAndProcessesRemainingItems()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var tenantId = harness.TenantContext.TenantId!.Value;
        var first = await SeedProductAsync(
            harness, tenantId, isActive: false, PublishStatus.Draft, isOnlineVisible: false);
        var second = await SeedProductAsync(
            harness, tenantId, isActive: false, PublishStatus.Draft, isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.Setup(item => item.AuthorAsync(
                It.Is<ProductAuthoringCommand>(command => command.Identity.ProductId == first.Id),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected item failure."));
        orchestrator.Setup(item => item.AuthorAsync(
                It.Is<ProductAuthoringCommand>(command => command.Identity.ProductId == second.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductAuthoringResult.Accepted(
                second.Id, PublishStatus.Draft, "exception-isolation", 22));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            new BulkProductRequest([
                new BulkProductRequestItem(first.Id.ToString(), first.RowVersion.ToString()),
                new BulkProductRequestItem(second.Id.ToString(), second.RowVersion.ToString())
            ]),
            "exception-isolation-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(1, outcome.OperationResult!.Processed);
        Assert.Equal(1, outcome.OperationResult.Failed);
        Assert.Equal(BulkErrorCodes.AuthoringFailed, outcome.OperationResult.Results[0].Code);
        Assert.Equal(BulkItemStatus.Processed, outcome.OperationResult.Results[1].Status);
        Assert.NotNull((await harness.Context.ProductRequestIdempotencyRecords.SingleAsync()).ResponseStatusCode);
    }

    [Fact]
    public async Task Activate_AmbiguousAuthoringFailure_RetriesWithSameCommandAndReturnsAcceptedReplay()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness,
            harness.TenantContext.TenantId!.Value,
            isActive: false,
            PublishStatus.Draft,
            isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);
        orchestrator.SetupSequence(item => item.AuthorAsync(
                It.IsAny<ProductAuthoringCommand>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ambiguous commit result."))
            .ReturnsAsync(ProductAuthoringResult.Accepted(
                product.Id, PublishStatus.Draft, "ambiguous-retry", 23));

        var outcome = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            Request(product),
            "ambiguous-retry-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, outcome.Kind);
        Assert.Equal(BulkItemStatus.Processed, Assert.Single(outcome.OperationResult!.Results).Status);
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Activate_CancelledAfterClaim_FinalizesInterruptedBatchForDeterministicReplay()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: false);
        var product = await SeedProductAsync(
            harness,
            harness.TenantContext.TenantId!.Value,
            isActive: false,
            PublishStatus.Draft,
            isOnlineVisible: false);
        var (service, orchestrator) = CreateService(harness, out _);
        using var cancellation = new CancellationTokenSource();
        orchestrator.Setup(item => item.AuthorAsync(
                It.IsAny<ProductAuthoringCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<ProductAuthoringResult>(cancellation.Token);
            });
        var request = Request(product);

        // The original cancellation always propagates to the caller.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteLifecycleAsync(
                BulkOperationType.Activate,
                request,
                "cancelled-batch-key",
                cancellation.Token));

        // The outer claim stores a truthful HTTP 200 aggregate, never a 500.
        harness.Context.ChangeTracker.Clear();
        var storedRow = await harness.Context.ProductRequestIdempotencyRecords.SingleAsync();
        Assert.Equal(200, storedRow.ResponseStatusCode);
        Assert.NotNull(storedRow.ResponseJson);

        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "cancelled-batch-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, replay.Kind);
        Assert.NotNull(replay.OperationResult);
        Assert.Equal(1, replay.OperationResult.Requested);
        Assert.Equal(1, replay.OperationResult.Accepted);
        Assert.Equal(0, replay.OperationResult.Processed);
        Assert.Equal(0, replay.OperationResult.Unchanged);
        Assert.Equal(1, replay.OperationResult.Failed);

        var itemResult = Assert.Single(replay.OperationResult.Results);
        Assert.Equal(BulkItemStatus.Failed, itemResult.Status);
        Assert.Equal(BulkErrorCodes.RequestCancelled, itemResult.Code);
        Assert.Equal(product.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), itemResult.PreviousVersion);
        Assert.Null(itemResult.NewVersion);

        var secondReplay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "cancelled-batch-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.Success, secondReplay.Kind);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(secondReplay.OperationResult));

        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IdenticalRequest_ReplaysExactStoredResponseWithoutSecondMutation()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var request = new BulkProductRequest([
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
        ]);
        var (service, orchestrator) = CreateService(harness, out _);

        var first = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "replay-key",
            default);
        var replay = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            "replay-key",
            default);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first.OperationResult),
            System.Text.Json.JsonSerializer.Serialize(replay.OperationResult));
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SameKeyWithDifferentVersion_ReturnsConflictWithoutMutation()
    {
        await using var harness = BulkFoundationTestHarness.Create(seedProducts: true);
        var (service, orchestrator) = CreateService(harness, out _);
        var firstRequest = new BulkProductRequest([
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "7")
        ]);
        var changedRequest = new BulkProductRequest([
            new BulkProductRequestItem(harness.PreparedProductId.ToString(), "6")
        ]);

        await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            firstRequest,
            "conflict-key",
            default);
        var conflict = await service.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            changedRequest,
            "conflict-key",
            default);

        Assert.Equal(BulkLifecycleOutcomeKind.IdempotencyConflict, conflict.Kind);
        Assert.Equal(1, await harness.Context.ProductRequestIdempotencyRecords.CountAsync());
        orchestrator.Verify(item => item.AuthorAsync(
            It.IsAny<ProductAuthoringCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (IBulkProductLifecycleService, Mock<IProductAuthoringOrchestrator>) CreateService(
        BulkFoundationTestHarness harness,
        out Mock<IProductAuthoringOrchestrator> orchestratorMock,
        ProductAuthoringResult? authoringResult = null,
        Mock<PointOfSale.API.Services.IValidationService>? validationMock = null,
        TimeProvider? timeProvider = null)
    {
        orchestratorMock = new Mock<IProductAuthoringOrchestrator>();
        orchestratorMock.Setup(x => x.AuthorAsync(It.IsAny<ProductAuthoringCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authoringResult ?? ProductAuthoringResult.InfrastructurePrepared("test-correlation"));

        var commandMapperMock = new Mock<IProductAuthoringCommandMapper>();
        commandMapperMock.Setup(x => x.FromLifecycleProduct(It.IsAny<ProductAuthoringOperation>(), It.IsAny<Product>()))
            .Returns((ProductAuthoringOperation operation, Product product) =>
                BuildTestCommand(operation, product));

        var idempotencyService = new BulkIdempotencyService(
            new ProductMutationIdempotencyService(harness.Context),
            harness.TenantContext,
            timeProvider);

        var productLoader = new BulkProductLoader(harness.Context, harness.TenantContext);
        var preparationService = new BulkPreparationService(
            new BulkProductRequestValidator(),
            productLoader,
            new StaticCorrelationIdAccessor("lifecycle-test-correlation"));

        var aggregator = new BulkOperationResultAggregator();

        var validation = validationMock ?? CreateValidationMock();
        var service = new BulkProductLifecycleService(
            preparationService,
            idempotencyService,
            aggregator,
            productLoader,
            orchestratorMock.Object,
            commandMapperMock.Object,
            validation.Object,
            harness.Context,
            harness.TenantContext,
            NullLogger<BulkProductLifecycleService>.Instance);

        return (service, orchestratorMock);
    }

    internal static Mock<PointOfSale.API.Services.IValidationService> CreateValidationMock(
        PointOfSale.API.Services.ValidationResult? deletionResult = null)
    {
        var mock = new Mock<PointOfSale.API.Services.IValidationService>();
        mock.Setup(x => x.CanDeleteProductAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletionResult ?? PointOfSale.API.Services.ValidationResult.Success());
        return mock;
    }

    private static ProductAuthoringCommand BuildTestCommand(ProductAuthoringOperation operation, Product product)
    {
        return new ProductAuthoringCommand(
            operation,
            new ProductAuthoringIdentity(
                product.TenantId,
                product.Id,
                product.CategoryId,
                null,
                null),
            new ProductAuthoringContent(
                product.Name,
                null, null, null, null, null,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ProductAuthoringFaq>()),
            new ProductAuthoringPricing(0, 0, null, false, 0),
            new ProductAuthoringPublication(product.PublishStatus, product.IsActive, product.IsOnlineVisible, true, false),
            new ProductAuthoringSeo(null, null, null, null),
            new ProductAuthoringShippingProfile(0, 0, 0, 0, "kg", "cm"),
            new ProductAuthoringInventoryProfile("Piece", 0, false, false, 0, false),
            new ProductAuthoringUnits(null, null, Array.Empty<ProductAuthoringUnit>()),
            Array.Empty<ProductAuthoringVariant>(),
            new ProductAuthoringMetadata(
                false, null, null, null, "test-correlation", null, null,
                new Dictionary<string, string>()));
    }

    private static async Task<Product> SeedProductAsync(
        BulkFoundationTestHarness harness,
        Guid tenantId,
        bool isActive,
        PublishStatus publishStatus,
        bool isOnlineVisible)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Lifecycle Product",
            Unit = "Piece",
            WeightUnit = "kg",
            DimensionUnit = "cm",
            IsActive = isActive,
            PublishStatus = publishStatus,
            IsOnlineVisible = isOnlineVisible,
            IsDeleted = false,
            RowVersion = 20
        };
        harness.Context.Products.Add(product);
        await harness.Context.SaveChangesAsync();
        return product;
    }

    private static BulkProductRequest Request(Product product) =>
        new([
            new BulkProductRequestItem(
                product.Id.ToString(),
                product.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ]);
}
