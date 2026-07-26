using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests;

public sealed class SupplierFinancialIdempotencyTests
{
    [Fact]
    public async Task SaveSupplierOpeningBalances_ProcessesDuplicateSupplierOnlyOnce()
    {
        var harness = CreateSupplierHarness();
        var supplier = await harness.CreateSupplierAsync("Duplicate Once Supplier");

        var result = await harness.Controller.SaveSupplierOpeningBalances(
            new SaveSupplierOpeningBalancesRequest
            {
                SupplierBalances =
                [
                    new SupplierOpeningBalanceItem
                    {
                        SupplierId = supplier.Id,
                        Amount = 125m,
                        BalanceType = "Credit",
                        Date = new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc),
                        Notes = "Initial credit"
                    },
                    new SupplierOpeningBalanceItem
                    {
                        SupplierId = supplier.Id,
                        Amount = 125m,
                        BalanceType = "Credit",
                        Date = new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc),
                        Notes = "Initial credit"
                    }
                ]
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var openingBalanceId = await harness.Context.AccountLedgers
            .Where(entry =>
                entry.TenantId == harness.TenantId &&
                entry.PartyType == PartyType.Supplier &&
                entry.PartyId == supplier.Id &&
                entry.TransactionType == TransactionType.OpeningBalance)
            .Select(entry => entry.Id)
            .SingleAsync();
        Assert.Equal(1, await harness.Context.AccountLedgers.CountAsync(entry =>
            entry.PartyType == PartyType.Supplier &&
            entry.PartyId == supplier.Id &&
            entry.TransactionType == TransactionType.OpeningBalance));
        Assert.Equal(1, await harness.Context.SupplierTransactions.CountAsync(entry =>
            entry.SupplierId == supplier.Id &&
            entry.TransactionType == TransactionType.OpeningBalance));
        harness.Outbox.Verify(
            outbox => outbox.EnqueueSupplierOpeningBalanceAsync(
                harness.TenantId,
                openingBalanceId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveSupplierOpeningBalances_ReplaysStoredResultOnRetry()
    {
        var harness = CreateSupplierHarness();
        var supplier = await harness.CreateSupplierAsync("Replay Supplier");
        var request = new SaveSupplierOpeningBalancesRequest
        {
            SupplierBalances =
            [
                new SupplierOpeningBalanceItem
                {
                    SupplierId = supplier.Id,
                    Amount = 87.5m,
                    BalanceType = "Debit",
                    Date = new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc),
                    Notes = "Opening debit"
                }
            ]
        };

        var firstResult = await harness.Controller.SaveSupplierOpeningBalances(request, CancellationToken.None);
        var secondResult = await harness.Controller.SaveSupplierOpeningBalances(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(firstResult);
        Assert.IsType<ObjectResult>(secondResult);
        var openingBalanceId = await harness.Context.AccountLedgers
            .Where(entry =>
                entry.TenantId == harness.TenantId &&
                entry.PartyType == PartyType.Supplier &&
                entry.PartyId == supplier.Id &&
                entry.TransactionType == TransactionType.OpeningBalance)
            .Select(entry => entry.Id)
            .SingleAsync();
        Assert.Equal(1, await harness.Context.AccountLedgers.CountAsync(entry =>
            entry.PartyType == PartyType.Supplier &&
            entry.PartyId == supplier.Id &&
            entry.TransactionType == TransactionType.OpeningBalance));
        Assert.Equal(1, await harness.Context.SupplierTransactions.CountAsync(entry =>
            entry.SupplierId == supplier.Id &&
            entry.TransactionType == TransactionType.OpeningBalance));
        Assert.Equal(1, await harness.Context.SalesRequestIdempotencyRecords.CountAsync());
        harness.Outbox.Verify(
            outbox => outbox.EnqueueSupplierOpeningBalanceAsync(
                harness.TenantId,
                openingBalanceId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveSupplierOpeningBalances_RejectsConflictingDuplicateEntries()
    {
        var harness = CreateSupplierHarness();
        var supplier = await harness.CreateSupplierAsync("Conflicting Duplicate Supplier");

        var result = await harness.Controller.SaveSupplierOpeningBalances(
            new SaveSupplierOpeningBalancesRequest
            {
                SupplierBalances =
                [
                    new SupplierOpeningBalanceItem
                    {
                        SupplierId = supplier.Id,
                        Amount = 20m,
                        BalanceType = "Debit",
                        Date = new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc)
                    },
                    new SupplierOpeningBalanceItem
                    {
                        SupplierId = supplier.Id,
                        Amount = 30m,
                        BalanceType = "Debit",
                        Date = new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc)
                    }
                ]
            },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(0, await harness.Context.AccountLedgers.CountAsync(entry =>
            entry.PartyType == PartyType.Supplier &&
            entry.PartyId == supplier.Id &&
            entry.TransactionType == TransactionType.OpeningBalance));
        harness.Outbox.Verify(
            outbox => outbox.EnqueueSupplierOpeningBalanceAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAdjustment_ForSupplier_ReplaysStoredResultOnRetry()
    {
        var harness = CreateStatementHarness();
        var supplier = await harness.CreateSupplierAsync("Adjustment Supplier");
        harness.Controller.Request.Headers["Idempotency-Key"] = "supplier-adjust-001";

        var request = new CreateAdjustmentRequest
        {
            PartyType = PartyType.Supplier,
            PartyId = supplier.Id,
            AdjustmentType = TransactionType.DebitAdjustment,
            Amount = 45m,
            ReferenceNumber = "SUP-ADJ-001",
            Notes = "Supplier debit adjustment"
        };

        var firstResult = await harness.Controller.CreateAdjustment(request, CancellationToken.None);
        var secondResult = await harness.Controller.CreateAdjustment(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(firstResult.Result);
        Assert.IsType<ObjectResult>(secondResult.Result);
        Assert.Equal(1, await harness.Context.PartyLedgerAdjustments.CountAsync());
        Assert.Equal(1, await harness.Context.SupplierTransactions.CountAsync(entry =>
            entry.SupplierId == supplier.Id &&
            entry.TransactionType == TransactionType.DebitAdjustment));
        Assert.Equal(1, await harness.Context.AccountLedgers.CountAsync(entry =>
            entry.PartyType == PartyType.Supplier &&
            entry.PartyId == supplier.Id &&
            entry.TransactionType == TransactionType.DebitAdjustment));
        harness.Outbox.Verify(
            outbox => outbox.EnqueuePartyLedgerAdjustmentAsync(
                harness.TenantId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinancialMutationIdempotencyService_BlocksSecondSupplierClaimWhileFirstIsInProgress()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var service = new FinancialMutationIdempotencyService(
            context,
            new StubTenantContext(tenantId),
            NullLogger<FinancialMutationIdempotencyService>.Instance);

        var key = $"SUPPLIER:{tenantId}:operation-001";
        var firstClaim = await service.TryClaimAsync(
            "SUPPLIER_OPENING_BALANCE_BATCH",
            key,
            requestHash: "HASH-A",
            cancellationToken: CancellationToken.None);
        var secondClaim = await service.TryClaimAsync(
            "SUPPLIER_OPENING_BALANCE_BATCH",
            key,
            requestHash: "HASH-A",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(firstClaim.Record);
        Assert.Null(firstClaim.ShortCircuitResult);
        var conflict = Assert.IsType<ConflictObjectResult>(secondClaim.ShortCircuitResult);
        Assert.Contains(
            "already being processed",
            JsonSerializer.Serialize(conflict.Value),
            StringComparison.OrdinalIgnoreCase);
    }

    private static SupplierControllerHarness CreateSupplierHarness()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new StubTenantContext(tenantId);
        var context = CreateContext(tenantId);
        var audit = CreateAuditMock();
        var outbox = CreateOutboxMock();
        var ledgerService = new LedgerService(
            context,
            tenantContext,
            NullLogger<LedgerService>.Instance,
            audit.Object);
        var idempotency = new FinancialMutationIdempotencyService(
            context,
            tenantContext,
            NullLogger<FinancialMutationIdempotencyService>.Instance);

        var controller = new SuppliersController(
            context,
            tenantContext,
            NullLogger<SuppliersController>.Instance,
            Mock.Of<IValidationService>(),
            audit.Object,
            ledgerService,
            outbox.Object,
            Mock.Of<IAccountResolver>(),
            Mock.Of<ITreasuryReadService>(),
            Mock.Of<ICashboxLedgerAccountService>(),
            idempotency)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return new SupplierControllerHarness(tenantId, context, controller, outbox);
    }

    private static StatementControllerHarness CreateStatementHarness()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new StubTenantContext(tenantId);
        var context = CreateContext(tenantId);
        var audit = CreateAuditMock();
        var outbox = CreateOutboxMock();
        var ledgerService = new LedgerService(
            context,
            tenantContext,
            NullLogger<LedgerService>.Instance,
            audit.Object);
        var idempotency = new FinancialMutationIdempotencyService(
            context,
            tenantContext,
            NullLogger<FinancialMutationIdempotencyService>.Instance);

        var controller = new StatementOfAccountController(
            context,
            tenantContext,
            NullLogger<StatementOfAccountController>.Instance,
            ledgerService,
            audit.Object,
            outbox.Object,
            idempotency)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return new StatementControllerHarness(tenantId, context, controller, outbox);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options, new StubTenantContext(tenantId));
    }

    private static Mock<IAccountingOutboxService> CreateOutboxMock()
    {
        var mock = new Mock<IAccountingOutboxService>();
        mock.SetReturnsDefault(Task.CompletedTask);
        return mock;
    }

    private static Mock<IAuditService> CreateAuditMock()
    {
        var mock = new Mock<IAuditService>();
        mock.SetReturnsDefault(Task.CompletedTask);
        return mock;
    }

    private sealed record SupplierControllerHarness(
        Guid TenantId,
        ApplicationDbContext Context,
        SuppliersController Controller,
        Mock<IAccountingOutboxService> Outbox)
    {
        public async Task<Supplier> CreateSupplierAsync(string name)
        {
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Name = name,
                Balance = 0m,
                CreatedAt = DateTime.UtcNow
            };

            Context.Suppliers.Add(supplier);
            await Context.SaveChangesAsync();
            return supplier;
        }
    }

    private sealed record StatementControllerHarness(
        Guid TenantId,
        ApplicationDbContext Context,
        StatementOfAccountController Controller,
        Mock<IAccountingOutboxService> Outbox)
    {
        public async Task<Supplier> CreateSupplierAsync(string name)
        {
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Name = name,
                Balance = 0m,
                CreatedAt = DateTime.UtcNow
            };

            Context.Suppliers.Add(supplier);
            await Context.SaveChangesAsync();
            return supplier;
        }
    }

    private sealed class StubTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public Guid RequiredTenantId => tenantId;
    }
}
