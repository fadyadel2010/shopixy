using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class AdminTenantOperationalScopeTests
{
    [Fact]
    public async Task BanksController_ForeignTenantBankStatementAndUpdate_FailClosed()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var bankId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setup = CreateDbContext(databaseName, foreignTenantId))
        {
            setup.Banks.Add(new Bank
            {
                Id = bankId,
                TenantId = foreignTenantId,
                BankName = "Foreign Bank",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            setup.BankTransactions.Add(new BankTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = foreignTenantId,
                BankId = bankId,
                Amount = 500,
                TransactionType = 1,
                TransactionDate = DateTime.UtcNow,
                Description = "foreign",
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using (var adminContext = CreateDbContext(databaseName, tenantId))
        {
            var controller = CreateBanksController(adminContext, tenantId);

            var statement = await controller.GetStatement(new BankStatementRequestDto { BankId = bankId });
            var update = await controller.UpdateBank(
                bankId,
                new UpdateBankDto("Mutated", null, null, null, null, true));

            Assert.IsType<NotFoundResult>(statement.Result);
            Assert.IsType<NotFoundResult>(update);
        }

        await using var verify = CreateDbContext(databaseName, foreignTenantId);
        var bank = await verify.Banks.SingleAsync(b => b.Id == bankId);
        Assert.Equal("Foreign Bank", bank.BankName);
    }

    [Fact]
    public async Task ExpenseCategoriesController_ForeignTenantToggle_FailsClosedWithoutMutation()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setup = CreateDbContext(databaseName, foreignTenantId))
        {
            setup.ExpenseCategories.Add(new ExpenseCategory
            {
                Id = categoryId,
                TenantId = foreignTenantId,
                Name = "Foreign Expense",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using (var adminContext = CreateDbContext(databaseName, tenantId))
        {
            var controller = CreateExpenseCategoriesController(adminContext, tenantId);
            var result = await controller.ToggleStatus(categoryId);

            Assert.IsType<NotFoundResult>(result);
        }

        await using var verify = CreateDbContext(databaseName, foreignTenantId);
        var category = await verify.ExpenseCategories.SingleAsync(c => c.Id == categoryId);
        Assert.True(category.IsActive);
    }

    [Fact]
    public async Task FinancialAccountsController_ForeignTenantParentAndUpdate_FailClosed()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setup = CreateDbContext(databaseName, foreignTenantId))
        {
            setup.FinancialAccounts.Add(new FinancialAccount
            {
                Id = accountId,
                TenantId = foreignTenantId,
                Name = "Foreign Account",
                AccountCode = "FA-1",
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using (var adminContext = CreateDbContext(databaseName, tenantId))
        {
            var controller = CreateFinancialAccountsController(adminContext, tenantId);

            var create = await controller.CreateAccount(new CreateFinancialAccountDto
            {
                Name = "Local Child",
                ParentAccountId = accountId
            });
            var update = await controller.UpdateAccount(accountId, new UpdateFinancialAccountDto { Name = "Mutated" });

            Assert.IsType<BadRequestObjectResult>(create.Result);
            Assert.IsType<NotFoundResult>(update);
        }

        await using var verify = CreateDbContext(databaseName, foreignTenantId);
        var account = await verify.FinancialAccounts.SingleAsync(a => a.Id == accountId);
        Assert.Equal("Foreign Account", account.Name);
    }

    [Fact]
    public async Task ShippingMethodsController_ForeignTenantUpdateAndCalculate_FailClosed()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var methodId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setup = CreateDbContext(databaseName, foreignTenantId))
        {
            setup.Warehouses.Add(new Warehouse
            {
                Id = warehouseId,
                TenantId = foreignTenantId,
                Name = "Foreign Origin",
                IsActive = true
            });
            setup.ShippingMethods.Add(new ShippingMethod
            {
                Id = methodId,
                TenantId = foreignTenantId,
                Name = "Foreign Shipping",
                BaseRate = 20,
                PerKgRate = 3,
                EstimatedDeliveryDaysMin = 1,
                EstimatedDeliveryDaysMax = 3,
                IsActive = true,
                FulfillmentWarehouseId = warehouseId,
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        var publisher = new Mock<IEventPublisher>();
        await using (var adminContext = CreateDbContext(databaseName, tenantId))
        {
            var controller = CreateShippingMethodsController(adminContext, tenantId, publisher.Object);

            var update = await controller.UpdateShippingMethod(methodId, new UpdateShippingMethodDto
            {
                Name = "Mutated Shipping",
                BaseRate = 99,
                PerKgRate = 9,
                EstimatedDeliveryDaysMin = 1,
                EstimatedDeliveryDaysMax = 2,
                IsActive = false
            });
            var calculate = await controller.CalculateShippingCost(new CalculateShippingRequest
            {
                ShippingMethodId = methodId,
                OrderTotal = 100,
                TotalWeight = 1
            });

            Assert.IsType<NotFoundObjectResult>(update);
            Assert.IsType<NotFoundObjectResult>(calculate.Result);
        }

        publisher.Verify(x => x.PublishAsync(It.IsAny<ShippingMethodUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var verify = CreateDbContext(databaseName, foreignTenantId);
        var method = await verify.ShippingMethods.SingleAsync(s => s.Id == methodId);
        Assert.Equal("Foreign Shipping", method.Name);
    }

    private static ApplicationDbContext CreateDbContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static BanksController CreateBanksController(ApplicationDbContext context, Guid tenantId) =>
        new(
            context,
            new TestTenantContext(tenantId),
            NullLogger<BanksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static ExpenseCategoriesController CreateExpenseCategoriesController(ApplicationDbContext context, Guid tenantId) =>
        new(
            context,
            new TestTenantContext(tenantId),
            NullLogger<ExpenseCategoriesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static FinancialAccountsController CreateFinancialAccountsController(ApplicationDbContext context, Guid tenantId) =>
        new(
            context,
            new TestTenantContext(tenantId),
            NullLogger<FinancialAccountsController>.Instance,
            Mock.Of<ITreasuryOperationService>(),
            Mock.Of<ITreasuryReadService>(),
            Mock.Of<IAccountResolver>(),
            Mock.Of<ICashboxLedgerAccountService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static ShippingMethodsController CreateShippingMethodsController(
        ApplicationDbContext context,
        Guid tenantId,
        IEventPublisher publisher) =>
        new(
            context,
            new TestTenantContext(tenantId),
            publisher,
            Mock.Of<IShippingCarrierRegistry>(),
            Mock.Of<ICarrierIntegrationAuthority>(),
            Mock.Of<IShippingCalculationService>(),
            new CommerceReliabilityTelemetry(),
            NullLogger<ShippingMethodsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
