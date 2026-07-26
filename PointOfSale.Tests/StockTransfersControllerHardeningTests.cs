using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class StockTransfersControllerHardeningTests
{
    [Fact]
    public async Task GetTransfers_WhenPageArgumentsAreOutOfRange_UsesNormalizedCappedPagination()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateDbContextAsync(connection, tenantId);
        var seeded = await SeedTransfersAsync(context, tenantId, transferCount: 150);

        var controller = CreateController(context, tenantId, seeded.UserId);
        var result = await controller.GetTransfers(page: 0, pageSize: 1000);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = JsonSerializer.SerializeToElement(ok.Value);

        Assert.Equal(1, payload.GetProperty("page").GetInt32());
        Assert.Equal(PaginationGuard.MaxPageSize, payload.GetProperty("pageSize").GetInt32());
        Assert.Equal(PaginationGuard.MaxPageSize, payload.GetProperty("items").GetArrayLength());
        Assert.Equal(150, payload.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, payload.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task ApproveTransfer_WhenUserIdentityMissing_ReturnsUnauthorized_AndLeavesTransferPending()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateDbContextAsync(connection, tenantId);
        var seeded = await SeedTransfersAsync(context, tenantId, transferCount: 1);

        var controller = CreateController(context, tenantId, userId: null);
        var result = await controller.ApproveTransfer(
            seeded.FirstTransferId,
            new ApprovalRequest { Notes = "approve" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Contains("Invalid user identity", JsonSerializer.Serialize(unauthorized.Value), StringComparison.Ordinal);

        context.ChangeTracker.Clear();

        var transfer = await context.StockTransfers.SingleAsync(transfer => transfer.Id == seeded.FirstTransferId);
        Assert.Equal(TransferStatus.Pending, transfer.Status);
        Assert.Null(transfer.ApprovedById);
        Assert.Null(transfer.ApprovedAt);
        Assert.Null(transfer.ApprovalNotes);
    }

    private static StockTransfersController CreateController(
        ApplicationDbContext context,
        Guid tenantId,
        Guid? userId)
    {
        var controller = new StockTransfersController(
            context,
            new TestTenantContext(tenantId),
            Mock.Of<IInventoryService>(),
            Mock.Of<IEventPublisher>(),
            NullLogger<StockTransfersController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(userId)
        };

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(Guid? userId)
    {
        var identity = userId.HasValue
            ? new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()),
                new Claim(ClaimTypes.Name, $"tester-{userId.Value:N}")
            ], "TestAuth")
            : new ClaimsIdentity();

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }

    private static async Task<ApplicationDbContext> CreateDbContextAsync(SqliteConnection connection, Guid tenantId)
    {
        RegisterSqliteFunctions(connection);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                return Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }

    private static async Task<SeededTransferListing> SeedTransfersAsync(
        ApplicationDbContext context,
        Guid tenantId,
        int transferCount)
    {
        var utcNow = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var fromWarehouseId = Guid.NewGuid();
        var toWarehouseId = Guid.NewGuid();
        var transferIds = new List<Guid>();

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Stock Transfer Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });

        context.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Username = "transfer-tester",
            Email = "transfer@test.local",
            PasswordHash = "hashed",
            FirstName = "Transfer",
            LastName = "Tester",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = utcNow
        });

        context.Warehouses.AddRange(
            new Warehouse
            {
                Id = fromWarehouseId,
                TenantId = tenantId,
                Name = "From Warehouse",
                Code = "FROM",
                IsActive = true,
                CreatedAt = utcNow
            },
            new Warehouse
            {
                Id = toWarehouseId,
                TenantId = tenantId,
                Name = "To Warehouse",
                Code = "TO",
                IsActive = true,
                CreatedAt = utcNow
            });

        for (var index = 0; index < transferCount; index++)
        {
            var transferId = Guid.NewGuid();
            transferIds.Add(transferId);

            context.StockTransfers.Add(new StockTransfer
            {
                Id = transferId,
                TenantId = tenantId,
                TransferNumber = $"TRF-TEST-{index:D4}",
                TransferDate = utcNow.AddMinutes(-index),
                FromWarehouseId = fromWarehouseId,
                ToWarehouseId = toWarehouseId,
                Status = TransferStatus.Pending,
                RequestedById = userId,
                CreatedAt = utcNow.AddMinutes(-index)
            });
        }

        await context.SaveChangesAsync();

        return new SeededTransferListing(userId, transferIds[0]);
    }

    private sealed record SeededTransferListing(Guid UserId, Guid FirstTransferId);
}
