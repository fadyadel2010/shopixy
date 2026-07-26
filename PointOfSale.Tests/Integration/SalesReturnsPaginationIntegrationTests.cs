using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class SalesReturnsPaginationIntegrationTests
{
    [Fact]
    public async Task GetSalesReturns_ClampsOversizedPageSizeToConfiguredMaximum()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, tenantContext);

        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Returns Warehouse",
            Code = "RET-WH",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Warehouses.Add(warehouse);
        context.Returns.AddRange(
            Enumerable.Range(1, 150).Select(index => new Return
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ReturnNumber = $"SR-{index:D4}",
                ReturnDate = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                ReturnType = ReturnType.SalesReturn,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                Subtotal = index,
                TaxAmount = 0m,
                TotalAmount = index,
                Reason = "Pagination hardening"
            }));

        await context.SaveChangesAsync();

        var controller = new SalesReturnsController(
            context,
            tenantContext,
            NullLogger<SalesReturnsController>.Instance,
            Mock.Of<ILedgerService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IAccountingOutboxService>(),
            Mock.Of<ICommerceAccountingService>());

        var result = await controller.GetSalesReturns(page: 1, pageSize: 1000);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PointOfSale.API.Controllers.PagedResult<ReturnResponseDto>>(okResult.Value);

        Assert.Equal(100, payload.PageSize);
        Assert.Equal(100, payload.Items.Count);
        Assert.Equal(150, payload.TotalCount);
        Assert.Equal(2, payload.TotalPages);
    }

    [Fact]
    public async Task GetSalesReturns_NormalizesLowBoundPageAndPageSizeToMinimums()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, tenantContext);

        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Returns Warehouse",
            Code = "RET-WH",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Warehouses.Add(warehouse);
        context.Returns.AddRange(
            Enumerable.Range(1, 10).Select(index => new Return
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ReturnNumber = $"SR-{index:D4}",
                ReturnDate = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                ReturnType = ReturnType.SalesReturn,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                Subtotal = index,
                TaxAmount = 0m,
                TotalAmount = index,
                Reason = "Low-bound normalization test"
            }));

        await context.SaveChangesAsync();

        var controller = new SalesReturnsController(
            context,
            tenantContext,
            NullLogger<SalesReturnsController>.Instance,
            Mock.Of<ILedgerService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IAccountingOutboxService>(),
            Mock.Of<ICommerceAccountingService>());

        var result = await controller.GetSalesReturns(page: 0, pageSize: -5);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PointOfSale.API.Controllers.PagedResult<ReturnResponseDto>>(okResult.Value);

        Assert.Equal(1, payload.Page);
        Assert.Equal(1, payload.PageSize);
        Assert.Equal(1, payload.Items.Count);
        Assert.Equal(10, payload.TotalCount);
        Assert.Equal(10, payload.TotalPages);
    }
}
