using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Tests.Infrastructure;
using Account = PointOfSale.Core.Entities.Accounting.Account;
using JournalEntry = PointOfSale.Core.Entities.Accounting.JournalEntry;
using LedgerEntry = PointOfSale.Core.Entities.Accounting.LedgerEntry;
using SupplierCardFundingInstrument = PointOfSale.Core.Entities.Accounting.SupplierCardFundingInstrument;
using TreasuryAccount = PointOfSale.Core.Entities.Accounting.TreasuryAccount;

namespace PointOfSale.Tests.Integration;

public sealed class PurchasePaymentIntegrityIntegrationTests
{
    [Fact]
    public async Task CreatePurchase_CardWithoutAuthority_FailsClosed()
    {
        var setup = await SetupPurchaseTestAsync("purchase-missing-card-authority-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var request = BuildRequest(
            setup.Seeded,
            paymentMethod: PaymentMethod.Card,
            paidAmount: 100m,
            unitCost: 100m);
        request.FundingInstrumentId = null;
        request.FundingInstrumentVersion = null;

        var result = await setup.Controller.CreatePurchase(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("FundingInstrumentId is required", JsonSerializer.Serialize(badRequest.Value), StringComparison.Ordinal);
        await using var verifyContext = scope.CreateDbContext(setup.TenantContext);
        Assert.Equal(0, await verifyContext.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task CreatePurchase_CardAboveFundedBalance_FailsClosed()
    {
        var setup = await SetupPurchaseTestAsync("purchase-card-overspend-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;

        var result = await setup.Controller.CreatePurchase(BuildRequest(
            setup.Seeded,
            paymentMethod: PaymentMethod.Card,
            paidAmount: 1000.01m,
            unitCost: 1000.01m));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Insufficient funded card balance", JsonSerializer.Serialize(badRequest.Value), StringComparison.Ordinal);
        await using var verifyContext = scope.CreateDbContext(setup.TenantContext);
        Assert.Equal(0, await verifyContext.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task CreatePurchase_RejectsOverpayment_AndDoesNotPersistInvoice()
    {
        var setup = await SetupPurchaseTestAsync("purchase-overpayment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 120m,
                unitCost: 100m));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(
            "Paid amount cannot exceed total amount.",
            JsonSerializer.Serialize(badRequest.Value),
            StringComparison.Ordinal);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(0, await verifyContext.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task CreatePurchase_AllowsExactPayment()
    {
        var setup = await SetupPurchaseTestAsync("purchase-exact-payment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100m,
                unitCost: 100m));

        var ok = AssertObjectResult(result, StatusCodes.Status200OK);
        var response = ReadPurchaseResponse(ok);
        Assert.Equal(100m, response.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var invoice = await verifyContext.PurchaseInvoices.SingleAsync();
        Assert.Equal(100m, invoice.TotalAmount);
        Assert.Equal(100m, invoice.PaidAmount);
        var payment = await verifyContext.PurchaseInvoicePayments.SingleAsync();
        Assert.Equal(100m, payment.Amount);
        Assert.Equal(PaymentMethod.Card, payment.PaymentMethod);
        Assert.Equal(seeded.FundingInstrumentId, payment.FundingInstrumentId);
        Assert.Equal(seeded.FundingLedgerAccountId, payment.FundingLedgerAccountId);
        Assert.Equal(seeded.FundingInstrumentVersion, payment.FundingInstrumentVersion);
    }

    [Fact]
    public async Task CreatePurchase_AllowsPartialPayment()
    {
        var setup = await SetupPurchaseTestAsync("purchase-partial-payment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Credit,
                paidAmount: 60m,
                unitCost: 100m,
                initialPaymentMethod: PaymentMethod.Card));

        var ok = AssertObjectResult(result, StatusCodes.Status200OK);
        var response = ReadPurchaseResponse(ok);
        Assert.Equal(100m, response.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var invoice = await verifyContext.PurchaseInvoices.SingleAsync();
        Assert.Equal(100m, invoice.TotalAmount);
        Assert.Equal(60m, invoice.PaidAmount);
        var payment = await verifyContext.PurchaseInvoicePayments.SingleAsync();
        Assert.Equal(60m, payment.Amount);
        Assert.Equal(PaymentMethod.Card, payment.PaymentMethod);
    }

    [Fact]
    public async Task CreatePurchase_PersistsMixedPaymentAllocation()
    {
        var setup = await SetupPurchaseTestAsync("purchase-mixed-payment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;

        var result = await setup.Controller.CreatePurchase(
            BuildRequest(
                setup.Seeded,
                paymentMethod: PaymentMethod.Mixed,
                paidAmount: 100m,
                unitCost: 100m,
                cashAmount: 40m,
                cardAmount: 60m));

        AssertObjectResult(result, StatusCodes.Status200OK);

        await using var verifyContext = scope.CreateDbContext(setup.TenantContext);
        var payments = await verifyContext.PurchaseInvoicePayments
            .OrderBy(payment => payment.PaymentMethod)
            .ToListAsync();
        Assert.Collection(
            payments,
            payment =>
            {
                Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
                Assert.Equal(40m, payment.Amount);
            },
            payment =>
            {
                Assert.Equal(PaymentMethod.Card, payment.PaymentMethod);
                Assert.Equal(60m, payment.Amount);
                Assert.Equal(setup.Seeded.FundingInstrumentId, payment.FundingInstrumentId);
                Assert.Equal(setup.Seeded.FundingLedgerAccountId, payment.FundingLedgerAccountId);
                Assert.Equal(setup.Seeded.FundingInstrumentVersion, payment.FundingInstrumentVersion);
            });
    }

    [Fact]
    public async Task UpdatePurchase_ReplacesPersistedPaymentAllocation()
    {
        var setup = await SetupPurchaseTestAsync("purchase-update-payment-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;

        var createResult = await setup.Controller.CreatePurchase(
            BuildRequest(
                setup.Seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100m,
                unitCost: 100m));
        var created = ReadPurchaseResponse(createResult);

        await using var beforeContext = scope.CreateDbContext(setup.TenantContext);
        var originalPaymentId = await beforeContext.PurchaseInvoicePayments
            .Select(payment => payment.Id)
            .SingleAsync();

        await using var updateContext = scope.CreateDbContext(setup.TenantContext);
        var updateController = CreateController(updateContext, setup.TenantContext);
        updateController.Request.Headers["Idempotency-Key"] = $"purchase-update-payment-{Guid.NewGuid():N}";
        var updateRequest = BuildRequest(
            setup.Seeded,
            paymentMethod: PaymentMethod.Mixed,
            paidAmount: 100m,
            unitCost: 100m,
            cashAmount: 40m,
            cardAmount: 60m);
        updateRequest.ExpectedMutationVersion = 0;

        var updateResult = await updateController.UpdatePurchase(created.InvoiceId, updateRequest);

        var response = ReadPurchaseResponse(AssertObjectResult(updateResult, StatusCodes.Status200OK));
        Assert.Equal(1, response.MutationVersion);

        await using var verifyContext = scope.CreateDbContext(setup.TenantContext);
        var payments = await verifyContext.PurchaseInvoicePayments
            .OrderBy(payment => payment.PaymentMethod)
            .ToListAsync();
        Assert.DoesNotContain(payments, payment => payment.Id == originalPaymentId);
        Assert.Collection(
            payments,
            payment =>
            {
                Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
                Assert.Equal(40m, payment.Amount);
            },
            payment =>
            {
                Assert.Equal(PaymentMethod.Card, payment.PaymentMethod);
                Assert.Equal(60m, payment.Amount);
                Assert.Equal(setup.Seeded.FundingInstrumentId, payment.FundingInstrumentId);
                Assert.Equal(setup.Seeded.FundingLedgerAccountId, payment.FundingLedgerAccountId);
                Assert.Equal(setup.Seeded.FundingInstrumentVersion, payment.FundingInstrumentVersion);
            });
    }

    [Fact]
    public async Task CreatePurchase_AllowsRoundedExactPayment()
    {
        var setup = await SetupPurchaseTestAsync("purchase-rounding-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var controller = setup.Controller;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var result = await controller.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100.01m,
                unitCost: 100.005m));

        var ok = AssertObjectResult(result, StatusCodes.Status200OK);
        var response = ReadPurchaseResponse(ok);
        Assert.Equal(100.01m, response.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        var invoice = await verifyContext.PurchaseInvoices.SingleAsync();
        Assert.Equal(100.01m, invoice.TotalAmount);
        Assert.Equal(100.01m, invoice.PaidAmount);
    }

    [Fact]
    public async Task CreatePurchase_ReplayingSameIdempotencyKey_DoesNotCreateDuplicatePersistence()
    {
        var setup = await SetupPurchaseTestAsync("purchase-idempotent-001");
        using var scope = setup.Scope;
        await using var actionContext = setup.ActionContext;
        var seeded = setup.Seeded;
        var tenantContext = setup.TenantContext;

        var firstController = setup.Controller;
        var firstResult = await firstController.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100m,
                unitCost: 100m));

        var firstResponse = ReadPurchaseResponse(firstResult);

        await using var replayContext = scope.CreateDbContext(tenantContext);
        var replayController = CreateController(replayContext, tenantContext);
        replayController.Request.Headers["Idempotency-Key"] = setup.IdempotencyKey;

        var replayResult = await replayController.CreatePurchase(
            BuildRequest(
                seeded,
                paymentMethod: PaymentMethod.Card,
                paidAmount: 100m,
                unitCost: 100m));

        var replayObjectResult = Assert.IsAssignableFrom<ObjectResult>(replayResult.Result);
        Assert.NotNull(replayObjectResult.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, replayObjectResult.StatusCode.Value);

        var replayResponse = ReadPurchaseResponse(replayResult);
        Assert.Equal(firstResponse.InvoiceId, replayResponse.InvoiceId);
        Assert.Equal(firstResponse.InvoiceNumber, replayResponse.InvoiceNumber);
        Assert.Equal(firstResponse.TotalAmount, replayResponse.TotalAmount);

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(1, await verifyContext.PurchaseInvoices.CountAsync());
        Assert.Equal(1, await verifyContext.PurchaseInvoiceItems.CountAsync());
        Assert.Equal(1, await verifyContext.PurchaseInvoicePayments.CountAsync());
        Assert.Equal(1, await verifyContext.PurchaseRequestIdempotencyRecords.CountAsync());

        var stock = await verifyContext.ProductVariantStocks.SingleAsync();
        Assert.Equal(1m, stock.Quantity);
    }

    private static async Task<PurchaseTestSetup> SetupPurchaseTestAsync(string idempotencyKey)
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            Assert.Fail("Failed to create test database scope. Ensure PostgreSQL is available and configured correctly.");
            return null!;
        }

        await using var seedContext = databaseScope.CreateDbContext(tenantContext);
        await seedContext.Database.ExecuteSqlRawAsync(
            "CREATE SEQUENCE IF NOT EXISTS purchase_invoice_seq START WITH 1000 INCREMENT BY 1 MINVALUE 1000 CACHE 1;");
        var seeded = await SeedScenarioAsync(seedContext, tenantId);

        var actionContext = databaseScope.CreateDbContext(tenantContext);
        var controller = CreateController(actionContext, tenantContext);
        var effectiveIdempotencyKey = $"{idempotencyKey}-{Guid.NewGuid():N}";
        controller.Request.Headers["Idempotency-Key"] = effectiveIdempotencyKey;

        return new PurchaseTestSetup(databaseScope, actionContext, controller, seeded, tenantContext, effectiveIdempotencyKey);
    }

    private static PurchasesController CreateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext)
    {
        var ledgerService = new Mock<ILedgerService>();
        ledgerService
            .Setup(service => service.ApplySupplierTransaction(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<TransactionType>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>()))
            .ReturnsAsync(0m);

        var auditService = new Mock<IAuditService>();
        auditService.SetReturnsDefault(Task.CompletedTask);

        var outbox = new Mock<IAccountingOutboxService>();
        outbox.SetReturnsDefault(Task.CompletedTask);
        outbox
            .Setup(service => service.EnqueuePosPurchaseWithCardAuthorityAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Guid tenantId, Guid invoiceId, int mutationVersion, decimal cashAmount, decimal cardAmount,
                Guid? fundingInstrumentId, Guid? fundingLedgerAccountId, int? fundingInstrumentVersion, CancellationToken cancellationToken) =>
            {
                context.AccountingOutboxMessages.Add(new PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EventType = "PosPurchase",
                    SourceType = "PosPurchase",
                    BusinessEntityId = invoiceId,
                    PayloadJson = "{}",
                    IdempotencyKey = BuildPurchaseAccountingOutboxIdempotencyKey(
                        tenantId,
                        invoiceId,
                        cashAmount,
                        cardAmount,
                        mutationVersion,
                        fundingInstrumentId,
                        fundingLedgerAccountId,
                        fundingInstrumentVersion),
                    AggregateVersion = mutationVersion,
                    OccurredAtUtc = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                });
                await context.SaveChangesAsync(cancellationToken);
            });
        outbox
            .Setup(service => service.EnqueuePurchaseReversalWithCardAuthorityAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                Guid tenantId,
                Guid invoiceId,
                string operationKey,
                int mutationVersion,
                decimal cashAmount,
                decimal cardAmount,
                Guid? fundingInstrumentId,
                Guid? fundingLedgerAccountId,
                int? fundingInstrumentVersion,
                CancellationToken cancellationToken) =>
            {
                context.AccountingOutboxMessages.Add(new PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EventType = "PurchaseReversal",
                    SourceType = "PurchaseReversal",
                    BusinessEntityId = invoiceId,
                    PayloadJson = "{}",
                    IdempotencyKey = BuildPurchaseMutationAccountingOutboxIdempotencyKey(
                        tenantId,
                        invoiceId,
                        operationKey,
                        cashAmount,
                        cardAmount,
                        mutationVersion,
                        fundingInstrumentId,
                        fundingLedgerAccountId,
                        fundingInstrumentVersion),
                    AggregateVersion = mutationVersion,
                    OccurredAtUtc = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                });
                await context.SaveChangesAsync(cancellationToken);
            });

        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostPosPurchaseAsync(
                It.IsAny<PurchaseInvoice>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<decimal?>(),
                It.IsAny<decimal?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<int?>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "JRN-PUR-TEST"));

        var cashboxAccountService = new Mock<ICashboxLedgerAccountService>();
        cashboxAccountService
            .Setup(service => service.ResolveWarehouseCashboxAccountAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashboxLedgerAccountInfo(
                CashboxId: null,
                AccountId: Guid.NewGuid(),
                AccountCode: "1010",
                Name: "Test Cashbox",
                IsDefault: true,
                WarehouseId: null,
                Location: null));

        var treasuryReadService = new Mock<ITreasuryReadService>();
        treasuryReadService
            .Setup(service => service.GetAccountBalanceAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1_000_000m);

        return new PurchasesController(
            context,
            tenantContext,
            NullLogger<PurchasesController>.Instance,
            ledgerService.Object,
            Mock.Of<IPartyBalanceMaintenanceService>(),
            Mock.Of<IValidationService>(),
            auditService.Object,
            new NoOpEventPublisher(),
            outbox.Object,
            commerceAccounting.Object,
            Mock.Of<IFinancialPostingService>(),
            Mock.Of<IAccountResolver>(),
            treasuryReadService.Object,
            cashboxAccountService.Object,
            Mock.Of<IPurchaseFinancialIntegrityService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static string BuildPurchaseAccountingOutboxIdempotencyKey(
        Guid tenantId,
        Guid invoiceId,
        decimal cashAmount,
        decimal cardAmount,
        int mutationVersion,
        Guid? fundingInstrumentId = null,
        Guid? fundingLedgerAccountId = null,
        int? fundingInstrumentVersion = null) =>
        $"PURCHASE:{tenantId}:{invoiceId}:C{cashAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:D{cardAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:V{mutationVersion}" +
        BuildCardAuthorityKeySuffix(cardAmount, fundingInstrumentId, fundingLedgerAccountId, fundingInstrumentVersion);

    private static string BuildPurchaseMutationAccountingOutboxIdempotencyKey(
        Guid tenantId,
        Guid invoiceId,
        string operationKey,
        decimal cashAmount,
        decimal cardAmount,
        int mutationVersion,
        Guid? fundingInstrumentId = null,
        Guid? fundingLedgerAccountId = null,
        int? fundingInstrumentVersion = null) =>
        $"PURCHASE:{tenantId}:{invoiceId}:{operationKey}:C{cashAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:D{cardAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}:V{mutationVersion}" +
        BuildCardAuthorityKeySuffix(cardAmount, fundingInstrumentId, fundingLedgerAccountId, fundingInstrumentVersion);

    private static string BuildCardAuthorityKeySuffix(
        decimal cardAmount,
        Guid? fundingInstrumentId,
        Guid? fundingLedgerAccountId,
        int? fundingInstrumentVersion) =>
        cardAmount > 0m
            ? $":FI{fundingInstrumentId:D}:FA{fundingLedgerAccountId:D}:FV{fundingInstrumentVersion}"
            : string.Empty;

    private static CreatePurchaseRequest BuildRequest(
        SeededPurchaseScenario seeded,
        PaymentMethod paymentMethod,
        decimal paidAmount,
        decimal unitCost,
        PaymentMethod? initialPaymentMethod = null,
        decimal? cashAmount = null,
        decimal? cardAmount = null)
    {
        return new CreatePurchaseRequest
        {
            SupplierId = seeded.SupplierId,
            WarehouseId = seeded.WarehouseId,
            InvoiceDate = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            PaymentMethod = paymentMethod,
            InitialPaymentMethod = initialPaymentMethod,
            PaidAmount = paidAmount,
            CashAmount = cashAmount,
            CardAmount = cardAmount,
            FundingInstrumentId = paymentMethod == PaymentMethod.Card || paymentMethod == PaymentMethod.Mixed ||
                (paymentMethod == PaymentMethod.Credit && initialPaymentMethod is PaymentMethod.Card or PaymentMethod.Mixed)
                ? seeded.FundingInstrumentId
                : null,
            FundingInstrumentVersion = paymentMethod == PaymentMethod.Card || paymentMethod == PaymentMethod.Mixed ||
                (paymentMethod == PaymentMethod.Credit && initialPaymentMethod is PaymentMethod.Card or PaymentMethod.Mixed)
                ? seeded.FundingInstrumentVersion
                : null,
            Items =
            [
                new PurchaseItemRequest
                {
                    ProductId = seeded.ProductId,
                    ProductVariantId = seeded.VariantId,
                    Quantity = 1m,
                    UnitCost = unitCost,
                    TaxRate = 0m
                }
            ]
        };
    }

    private static async Task<SeededPurchaseScenario> SeedScenarioAsync(
        ApplicationDbContext context,
        Guid tenantId)
    {
        var utcNow = DateTime.UtcNow;
        var categoryId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var fundingSourceAccount = Account.Create(tenantId, "1098", "Test funding source", AccountType.Asset);
        fundingSourceAccount.Id = Guid.NewGuid();
        var cardAccount = Account.Create(tenantId, "1099", "Test funded supplier card", AccountType.Asset);
        cardAccount.Id = Guid.NewGuid();
        cardAccount.DesignateAsSupplierCardFunding();
        var fundingSource = TreasuryAccount.CreateBank(tenantId, fundingSourceAccount.Id, "Test bank");
        fundingSource.Id = Guid.NewGuid();
        var fundingInstrument = SupplierCardFundingInstrument.Create(
            tenantId, "Test company card", cardAccount.Id, fundingSource.Id, "Test issuer", "1234", true);

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Purchase Payment Integrity Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Purchase Tests",
            CreatedAt = utcNow
        });

        context.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Primary Supplier",
            Balance = 0m,
            CreatedAt = utcNow
        });

        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Main Warehouse",
            Code = "WH-PUR",
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Purchase Test Product",
            CategoryId = categoryId,
            SalePrice = 150m,
            OnlinePrice = 150m,
            PurchasePrice = 80m,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = "PUR-TEST-001",
            Price = 150m,
            OnlinePrice = 150m,
            CostPrice = 80m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = 0m,
            ReservedQuantity = 0m,
            DamagedQuantity = 0m,
            CreatedAt = utcNow
        });

        context.LedgerAccounts.AddRange(fundingSourceAccount, cardAccount);
        context.TreasuryAccounts.Add(fundingSource);
        context.SupplierCardFundingInstruments.Add(fundingInstrument);

        await context.SaveChangesAsync();

        const decimal fundedBalance = 1000m;
        var openingJournal = JournalEntry.Create(
            tenantId, $"JE-TEST-{Guid.NewGuid():N}", utcNow, "TestOpening", Guid.NewGuid(), null,
            "Fund supplier card test authority", $"TEST-CARD-FUNDING:{Guid.NewGuid():N}", null);
        cardAccount.ApplyEntry(fundedBalance, 0m);
        fundingSourceAccount.ApplyEntry(0m, fundedBalance);
        context.JournalEntries.Add(openingJournal);
        context.LedgerEntries.AddRange(
            LedgerEntry.CreateDebit(tenantId, openingJournal.Id, cardAccount.Id, fundedBalance, fundedBalance, 1),
            LedgerEntry.CreateCredit(tenantId, openingJournal.Id, fundingSourceAccount.Id, fundedBalance, -fundedBalance, 2));
        using (LedgerMutationScope.Authorize())
        {
            await context.SaveChangesAsync();
        }

        return new SeededPurchaseScenario(
            supplierId, warehouseId, productId, variantId, fundingInstrument.Id, cardAccount.Id, fundingInstrument.AuthorityVersion);
    }

    private static PurchaseResponse ReadPurchaseResponse(ActionResult<PurchaseResponse> result)
    {
        if (result.Result is ObjectResult objectResult)
        {
            return ReadPurchaseResponse(objectResult);
        }

        Assert.Fail($"Expected an object result carrying a purchase response, but received '{result.Result?.GetType().Name ?? "null"}'.");
        return null!;
    }

    private static ObjectResult AssertObjectResult(ActionResult<PurchaseResponse> result, int expectedStatusCode)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode ?? StatusCodes.Status200OK);
        return objectResult;
    }

    private static PurchaseResponse ReadPurchaseResponse(ObjectResult result)
    {
        if (result.Value is PurchaseResponse response)
        {
            return response;
        }

        if (result.Value is JsonElement payload)
        {
            var deserialized = payload.Deserialize<PurchaseResponse>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(deserialized);
            return deserialized!;
        }

        var serialized = JsonSerializer.Serialize(result.Value);
        var fallback = JsonSerializer.Deserialize<PurchaseResponse>(serialized, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(fallback);
        return fallback!;
    }

    private sealed record PurchaseTestSetup(
        TestDatabaseScope Scope,
        ApplicationDbContext ActionContext,
        PurchasesController Controller,
        SeededPurchaseScenario Seeded,
        TestTenantContext TenantContext,
        string IdempotencyKey);

    private sealed record SeededPurchaseScenario(
        Guid SupplierId,
        Guid WarehouseId,
        Guid ProductId,
        Guid VariantId,
        Guid FundingInstrumentId,
        Guid FundingLedgerAccountId,
        int FundingInstrumentVersion);

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent =>
            Task.CompletedTask;

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            Task.CompletedTask;
    }
}
