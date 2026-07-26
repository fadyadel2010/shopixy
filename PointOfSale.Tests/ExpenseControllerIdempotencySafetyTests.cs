using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class ExpenseControllerIdempotencySafetyTests
{
    [Fact]
    public async Task CreateExpense_RejectsMissingIdempotencyKey()
    {
        var controller = CreateController();

        var result = await controller.CreateExpense(new CreateExpenseDto(
            Guid.NewGuid(),
            DateTime.UtcNow,
            100m,
            "Office rent",
            null,
            "Cash",
            null,
            null,
            null,
            "REF-1",
            false,
            null,
            false),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Idempotency-Key", JsonSerializer.Serialize(badRequest.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad_key")]
    [InlineData("bad/key")]
    public async Task CreateExpense_RejectsInvalidIdempotencyKey(string key)
    {
        var controller = CreateController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.Request.Headers["Idempotency-Key"] = key;

        var result = await controller.CreateExpense(new CreateExpenseDto(
            Guid.NewGuid(),
            DateTime.UtcNow,
            50m,
            "Stationery",
            null,
            "Cash",
            null,
            null,
            null,
            "REF-2",
            false,
            null,
            false),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Idempotency-Key", JsonSerializer.Serialize(badRequest.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task CreateExpense_RejectsNonPositiveAmount(decimal amount)
    {
        var controller = CreateController();

        var result = await controller.CreateExpense(new CreateExpenseDto(
            Guid.NewGuid(),
            DateTime.UtcNow,
            amount,
            "Stationery",
            null,
            "Cash",
            null,
            null,
            null,
            "REF-NEG",
            false,
            null,
            false),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("greater than zero", JsonSerializer.Serialize(badRequest.Value));
    }

    [Fact]
    public async Task CreateExpense_RejectsMissingExpenseCategoryBeforePersistence()
    {
        var controller = CreateController();
        controller.Request.Headers["Idempotency-Key"] = "expense-missing-category";

        var result = await controller.CreateExpense(new CreateExpenseDto(
            Guid.NewGuid(),
            DateTime.UtcNow,
            75m,
            "Stationery",
            null,
            "Cash",
            null,
            null,
            null,
            "REF-MISSING-CAT",
            false,
            null,
            false),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("category", JsonSerializer.Serialize(badRequest.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApproveExpense_IsRestrictedToAccountingGovernanceRoles()
    {
        var method = typeof(ExpensesController).GetMethod(nameof(ExpensesController.ApproveExpense));

        var attribute = Assert.Single(method!.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("Owner,Admin,Manager", attribute.Roles);
    }

    [Fact]
    public void BuildExpenseRequestHash_ProducesDeterministicHash()
    {
        var method = typeof(ExpensesController).GetMethod(
            "BuildExpenseRequestHash",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var request = new UpdateExpenseDto(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc),
            120m,
            "Internet bill",
            "April",
            "BankTransfer",
            null,
            null,
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            "APR-2026",
            false,
            null);

        var hash1 = (string)method!.Invoke(null, ["EXPENSE_UPDATE", request])!;
        var hash2 = (string)method.Invoke(null, ["EXPENSE_UPDATE", request])!;

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public async Task GetExpenses_WhenPageArgumentsAreOutOfRange_NormalizesAndCapsPagination()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var categoryId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;

        context.ExpenseCategories.Add(new ExpenseCategory
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Operations",
            CreatedAt = utcNow
        });

        for (var index = 0; index < 150; index++)
        {
            context.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ExpenseCategoryId = categoryId,
                ExpenseDate = utcNow.AddMinutes(-index),
                Amount = 10 + index,
                Description = $"Expense {index}",
                PaymentMethod = "Cash",
                IsApproved = true,
                CreatedAt = utcNow.AddMinutes(-index)
            });
        }

        await context.SaveChangesAsync();

        var controller = CreateController(context, tenantId);
        var result = await controller.GetExpenses(page: 0, pageSize: 1000, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = ok.Value!;
        var payloadType = payload.GetType();
        var items = Assert.IsAssignableFrom<IEnumerable<ExpenseDto>>(
            payloadType.GetProperty("Items")!.GetValue(payload));

        Assert.Equal(1, (int)payloadType.GetProperty("Page")!.GetValue(payload)!);
        Assert.Equal(PaginationGuard.MaxPageSize, (int)payloadType.GetProperty("PageSize")!.GetValue(payload)!);
        Assert.Equal(PaginationGuard.MaxPageSize, items.Count());
        Assert.Equal(150, (int)payloadType.GetProperty("TotalCount")!.GetValue(payload)!);
        Assert.Equal(2, (int)payloadType.GetProperty("TotalPages")!.GetValue(payload)!);
    }

    private static ExpensesController CreateController()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var context = new ApplicationDbContext(options);
        return CreateController(context, tenantId);
    }

    private static ExpensesController CreateController(ApplicationDbContext context, Guid tenantId)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(value => value.TenantId).Returns(tenantId);
        tenantContext.SetupGet(value => value.RequiredTenantId).Returns(tenantId);

        var controller = new ExpensesController(
            context,
            tenantContext.Object,
            NullLogger<ExpensesController>.Instance,
            Mock.Of<IAccountingOutboxService>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }
}
