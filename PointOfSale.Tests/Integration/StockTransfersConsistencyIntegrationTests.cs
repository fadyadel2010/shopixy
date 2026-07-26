using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class StockTransfersConsistencyIntegrationTests
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task CreateTransfer_ReplayingSameClientRequestId_ReturnsExistingTransfer()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId);

        var request = BuildCreateTransferRequest(seeded, "stock-transfer-create-001");

        await using var actionContext = scope.CreateDbContext(tenantContext);
        var controller = CreateController(actionContext, tenantContext, seeded.UserId);
        var firstResult = await controller.CreateTransfer(request);
        var firstResponse = ReadCreateTransferResponse(firstResult);

        await using var replayContext = scope.CreateDbContext(tenantContext);
        var replayController = CreateController(replayContext, tenantContext, seeded.UserId);
        var replayResult = await replayController.CreateTransfer(BuildCreateTransferRequest(seeded, "stock-transfer-create-001"));
        var replayResponse = ReadCreateTransferResponse(replayResult);

        Assert.Equal(firstResponse.TransferId, replayResponse.TransferId);
        Assert.Equal(firstResponse.TransferNumber, replayResponse.TransferNumber);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(1, await verifyContext.StockTransfers.CountAsync());
        Assert.Equal(1, await verifyContext.StockTransferItems.CountAsync());
        Assert.Equal(1, await verifyContext.StockTransferRequestIdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task RejectTransfer_WhenCommitFails_RollsBackPendingStatusMutation()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId);
        var transferId = await SeedPendingTransferAsync(seedContext, tenantId, seeded);

        var connectionString = seedContext.Database.GetDbConnection().ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors()
            .AddInterceptors(new CommitFailureInterceptor())
            .Options;

        await using var actionContext = new ApplicationDbContext(options, tenantContext);
        var controller = CreateController(actionContext, tenantContext, seeded.UserId);

        var result = await controller.RejectTransfer(transferId, new ApprovalRequest { Notes = "Reject with forced failure" });
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var transfer = await verifyContext.StockTransfers.SingleAsync(entity => entity.Id == transferId);
        Assert.Equal(TransferStatus.Pending, transfer.Status);
        Assert.Null(transfer.ApprovedById);
        Assert.Null(transfer.ApprovedAt);
        Assert.Null(transfer.ApprovalNotes);
    }

    [Fact]
    public async Task ApproveAndRejectTransfer_WhenExecutedConcurrently_OnlyOneStateTransitionSucceeds()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId);
        var transferId = await SeedPendingTransferAsync(seedContext, tenantId, seeded);
        var connectionString = seedContext.Database.GetDbConnection().ConnectionString;

        var blockingSaveInterceptor = new BlockingSaveChangesInterceptor();
        var rejectLockProbe = new MatchingCommandInterceptor("FOR UPDATE");

        var approveOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors()
            .AddInterceptors(blockingSaveInterceptor)
            .Options;

        var rejectOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors()
            .AddInterceptors(rejectLockProbe)
            .Options;

        await using var approveContext = new ApplicationDbContext(approveOptions, tenantContext);
        await using var rejectContext = new ApplicationDbContext(rejectOptions, tenantContext);

        var approveController = CreateController(approveContext, tenantContext, seeded.UserId);
        var rejectController = CreateController(rejectContext, tenantContext, seeded.UserId);

        var approveTask = approveController.ApproveTransfer(
            transferId,
            new ApprovalRequest { Notes = "approve-wins" });

        await blockingSaveInterceptor.WaitUntilBlockedAsync();

        var rejectTask = rejectController.RejectTransfer(
            transferId,
            new ApprovalRequest { Notes = "reject-loses" });

        await rejectLockProbe.WaitForMatchAsync();
        await Task.Delay(150);
        Assert.False(rejectTask.IsCompleted);

        blockingSaveInterceptor.Release();

        var approveResult = await approveTask;
        var rejectResult = await rejectTask;

        Assert.IsType<OkObjectResult>(approveResult);

        var badRequest = Assert.IsType<BadRequestObjectResult>(rejectResult);
        Assert.Contains("not pending", JsonSerializer.Serialize(badRequest.Value), StringComparison.OrdinalIgnoreCase);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var transfer = await verifyContext.StockTransfers.SingleAsync(entity => entity.Id == transferId);
        Assert.Equal(TransferStatus.Approved, transfer.Status);
        Assert.Equal(seeded.UserId, transfer.ApprovedById);
        Assert.Equal("approve-wins", transfer.ApprovalNotes);
    }

    [Fact]
    public async Task WarehouseTransfer_ReplayingSameRequestId_DoesNotDoubleApplyStock()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId);

        var dto = new StockTransferDto
        {
            SourceWarehouseId = seeded.SourceWarehouseId,
            TargetWarehouseId = seeded.TargetWarehouseId,
            ProductId = seeded.ProductId,
            ProductVariantId = seeded.VariantId,
            Quantity = 3m,
            Notes = "direct-transfer"
        };

        await using var actionContext = scope.CreateDbContext(tenantContext);
        var controller = CreateWarehouseController(actionContext, tenantContext, seeded.UserId);
        controller.Request.Headers["Idempotency-Key"] = "warehouse-transfer-001";

        var firstResult = await controller.TransferStock(dto);
        var firstObjectResult = Assert.IsType<OkObjectResult>(firstResult);
        var firstResponse = Assert.IsType<StockMovementDto>(firstObjectResult.Value);

        await using var replayContext = scope.CreateDbContext(tenantContext);
        var replayController = CreateWarehouseController(replayContext, tenantContext, seeded.UserId);
        replayController.Request.Headers["Idempotency-Key"] = "warehouse-transfer-001";

        var replayResult = await replayController.TransferStock(dto);
        var replayObjectResult = Assert.IsAssignableFrom<ObjectResult>(replayResult);
        Assert.Equal(StatusCodes.Status200OK, replayObjectResult.StatusCode ?? StatusCodes.Status200OK);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var stocks = await verifyContext.ProductVariantStocks
            .Where(stock =>
                stock.TenantId == tenantId &&
                stock.VariantId == seeded.VariantId)
            .ToListAsync();

        Assert.Equal(2, stocks.Count);
        Assert.Equal(7m, stocks.Single(stock => stock.WarehouseId == seeded.SourceWarehouseId).Quantity);
        Assert.Equal(3m, stocks.Single(stock => stock.WarehouseId == seeded.TargetWarehouseId).Quantity);

        var movements = await verifyContext.StockMovements
            .Where(movement =>
                movement.ProductVariantId == seeded.VariantId &&
                (movement.MovementType == StockMovementType.TransferOut ||
                 movement.MovementType == StockMovementType.TransferIn))
            .ToListAsync();

        Assert.Equal(2, movements.Count);
        Assert.Equal(1, await verifyContext.StockTransferRequestIdempotencyRecords.CountAsync());
        Assert.Contains(
            await verifyContext.StockTransferRequestIdempotencyRecords.Select(record => record.Key).ToListAsync(),
            key => key.Contains("warehouse-transfer-001", StringComparison.Ordinal));
        Assert.Equal(3m, firstResponse.QuantityChange);
    }

    [Fact]
    public async Task CreateTransfer_WithDuplicateVariantLines_UsesAggregatedLockedAvailability()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        using var scope = databaseScope;
        await using var seedContext = scope.CreateDbContext(tenantContext);
        var seeded = await SeedScenarioAsync(seedContext, tenantId);
        var sourceStock = await seedContext.ProductVariantStocks.SingleAsync(stock =>
            stock.TenantId == tenantId &&
            stock.WarehouseId == seeded.SourceWarehouseId &&
            stock.VariantId == seeded.VariantId);
        sourceStock.ReservedQuantity = 2m;
        await seedContext.SaveChangesAsync();

        var request = new CreateTransferRequest
        {
            ClientRequestId = "stock-transfer-duplicate-lines-001",
            FromWarehouseId = seeded.SourceWarehouseId,
            ToWarehouseId = seeded.TargetWarehouseId,
            Notes = "Duplicate line aggregation",
            Items =
            [
                new TransferItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    Quantity = 5m,
                    Notes = "line-1"
                },
                new TransferItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    Quantity = 4m,
                    Notes = "line-2"
                }
            ]
        };

        // Capture source stock state before the request
        await using var preActionContext = scope.CreateDbContext(tenantContext);
        var sourceStockSnapshot = await preActionContext.ProductVariantStocks
            .AsNoTracking()
            .SingleAsync(stock =>
                stock.TenantId == tenantId &&
                stock.WarehouseId == seeded.SourceWarehouseId &&
                stock.VariantId == seeded.VariantId);
        var snapshotId = sourceStockSnapshot.Id;
        var snapshotQuantity = sourceStockSnapshot.Quantity;
        var snapshotReservedQuantity = sourceStockSnapshot.ReservedQuantity;
        var snapshotDamagedQuantity = sourceStockSnapshot.DamagedQuantity;
        var snapshotUpdatedAt = sourceStockSnapshot.UpdatedAt;

        await using var actionContext = scope.CreateDbContext(tenantContext);
        var controller = CreateController(actionContext, tenantContext, seeded.UserId);
        var result = await controller.CreateTransfer(request);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode ?? StatusCodes.Status400BadRequest);
        Assert.Contains("Insufficient stock", JsonSerializer.Serialize(badRequest.Value), StringComparison.Ordinal);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(0, await verifyContext.StockTransfers.CountAsync());
        Assert.Equal(0, await verifyContext.StockTransferItems.CountAsync());
        Assert.Equal(0, await verifyContext.StockTransferRequestIdempotencyRecords.CountAsync());

        // Verify source stock remains unchanged after validation failure
        var sourceStockAfter = await verifyContext.ProductVariantStocks
            .AsNoTracking()
            .SingleAsync(stock => stock.Id == snapshotId);
        Assert.Equal(snapshotQuantity, sourceStockAfter.Quantity);
        Assert.Equal(snapshotReservedQuantity, sourceStockAfter.ReservedQuantity);
        Assert.Equal(snapshotDamagedQuantity, sourceStockAfter.DamagedQuantity);
        Assert.Equal(snapshotUpdatedAt, sourceStockAfter.UpdatedAt);
    }

    private static StockTransfersController CreateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        Guid userId)
    {
        return new StockTransfersController(
            context,
            tenantContext,
            Mock.Of<IInventoryService>(),
            Mock.Of<IEventPublisher>(),
            NullLogger<StockTransfersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(userId)
            }
        };
    }

    private static WarehousesController CreateWarehouseController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        Guid userId)
    {
        return new WarehousesController(
            context,
            tenantContext,
            new InventoryService(context, tenantContext, NullLogger<InventoryService>.Instance),
            Mock.Of<IEventPublisher>(),
            NullLogger<WarehousesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(userId)
            }
        };
    }

    private static CreateTransferRequest BuildCreateTransferRequest(SeededTransferScenario seeded, string clientRequestId)
    {
        return new CreateTransferRequest
        {
            ClientRequestId = clientRequestId,
            FromWarehouseId = seeded.SourceWarehouseId,
            ToWarehouseId = seeded.TargetWarehouseId,
            Notes = "Integration transfer",
            Items =
            [
                new TransferItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    Quantity = 2m,
                    Notes = "move-2"
                }
            ]
        };
    }

    private static CreateTransferResponse ReadCreateTransferResponse(ActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode ?? StatusCodes.Status200OK);

        if (objectResult.Value is CreateTransferResponse response)
        {
            return response;
        }

        if (objectResult.Value is JsonElement payload)
        {
            return JsonSerializer.Deserialize<CreateTransferResponse>(payload.GetRawText(), ResponseJsonOptions)
                   ?? throw new InvalidOperationException("Stored stock transfer response payload could not be deserialized.");
        }

        return JsonSerializer.Deserialize<CreateTransferResponse>(
                   JsonSerializer.Serialize(objectResult.Value),
                   ResponseJsonOptions)
               ?? throw new InvalidOperationException("Stock transfer response payload could not be deserialized.");
    }

    private static async Task<SeededTransferScenario> SeedScenarioAsync(ApplicationDbContext context, Guid tenantId)
    {
        var utcNow = DateTime.UtcNow;
        var categoryId = Guid.NewGuid();
        var sourceWarehouseId = Guid.NewGuid();
        var targetWarehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var userId = Guid.Parse("40000000-0000-0000-0000-000000000001");

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Stock Transfer Consistency Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Username = "transfer-user",
            Email = "transfer-user@test.local",
            PasswordHash = "hashed",
            FirstName = "Transfer",
            LastName = "Tester",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Transfers",
            CreatedAt = utcNow
        });

        context.Warehouses.AddRange(
            new Warehouse
            {
                Id = sourceWarehouseId,
                TenantId = tenantId,
                Name = "Source Warehouse",
                Code = "WH-SRC",
                IsActive = true,
                CreatedAt = utcNow
            },
            new Warehouse
            {
                Id = targetWarehouseId,
                TenantId = tenantId,
                Name = "Target Warehouse",
                Code = "WH-TGT",
                IsActive = true,
                CreatedAt = utcNow
            });

        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Transfer Product",
            CategoryId = categoryId,
            PurchasePrice = 5m,
            SalePrice = 10m,
            OnlinePrice = 10m,
            PriceIncludesTax = false,
            TaxRate = 0m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = true,
            CreatedAt = utcNow
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "TRF-IT-001",
            Price = 10m,
            OnlinePrice = 10m,
            CostPrice = 5m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = sourceWarehouseId,
            VariantId = variantId,
            Quantity = 10m,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = utcNow
        });

        await context.SaveChangesAsync();

        return new SeededTransferScenario(
            SourceWarehouseId: sourceWarehouseId,
            TargetWarehouseId: targetWarehouseId,
            ProductId: productId,
            VariantId: variantId,
            UserId: userId);
    }

    private static async Task<Guid> SeedPendingTransferAsync(
        ApplicationDbContext context,
        Guid tenantId,
        SeededTransferScenario seeded)
    {
        var transferId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;

        var transfer = new StockTransfer
        {
            Id = transferId,
            TenantId = tenantId,
            TransferNumber = "TRF-TEST-0001",
            TransferDate = utcNow,
            FromWarehouseId = seeded.SourceWarehouseId,
            ToWarehouseId = seeded.TargetWarehouseId,
            Status = TransferStatus.Pending,
            RequestedById = seeded.UserId,
            CreatedAt = utcNow
        };

        transfer.Items.Add(new StockTransferItem
        {
            Id = Guid.NewGuid(),
            StockTransferId = transferId,
            ProductId = seeded.ProductId,
            ProductVariantId = seeded.VariantId,
            Quantity = 1m,
            CreatedAt = utcNow
        });

        context.StockTransfers.Add(transfer);
        await context.SaveChangesAsync();

        return transferId;
    }

    private static DefaultHttpContext BuildHttpContext(Guid userId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, $"tester-{userId:N}")
        ], "TestAuth");

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }

    private sealed record SeededTransferScenario(
        Guid SourceWarehouseId,
        Guid TargetWarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid UserId);

    private sealed class CommitFailureInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<InterceptionResult>(
                new InvalidOperationException("Simulated commit failure."));
        }
    }

    private sealed class BlockingSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _saveEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitUntilBlockedAsync()
        {
            await _saveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void Release()
        {
            _releaseGate.TrySetResult();
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _saveEntered.TrySetResult();
            await _releaseGate.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class MatchingCommandInterceptor : DbCommandInterceptor
    {
        private readonly string _pattern;
        private readonly TaskCompletionSource _matchSeen =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MatchingCommandInterceptor(string pattern)
        {
            _pattern = pattern;
        }

        public async Task WaitForMatchAsync()
        {
            await _matchSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(_pattern, StringComparison.OrdinalIgnoreCase))
            {
                _matchSeen.TrySetResult();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
