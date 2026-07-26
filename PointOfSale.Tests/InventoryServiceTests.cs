using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests;

/// <summary>
/// Unit tests for InventoryService.
/// Uses EF Core InMemory provider for isolated testing.
/// </summary>
public class InventoryServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ITenantContext> _mockTenantContext;
    private readonly Mock<ILogger<InventoryService>> _mockLogger;
    private readonly InventoryService _service;
    private readonly Guid _testTenantId = Guid.NewGuid();

    public InventoryServiceTests()
    {
        // Setup mock tenant context (required for multi-tenant isolation)
        _mockTenantContext = new Mock<ITenantContext>();
        _mockTenantContext.Setup(tc => tc.RequiredTenantId).Returns(_testTenantId);
        _mockTenantContext.Setup(tc => tc.TenantId).Returns(_testTenantId);

        // Configure InMemory database for unit testing.
        // EF Core InMemory provider does not support transactions.
        // TransactionIgnoredWarning is explicitly ignored because:
        // 1. InventoryService uses transactions for atomicity in production
        // 2. InMemory provider ignores transactions but still executes operations
        // 3. This warning would otherwise fail tests unnecessarily
        // Note: This suppression applies ONLY to unit tests, not production code.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new ApplicationDbContext(options, _mockTenantContext.Object);
        
        _mockLogger = new Mock<ILogger<InventoryService>>();
        _service = new InventoryService(_context, _mockTenantContext.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task AdjustStockAsync_IncreasesStock_WhenRecordExists()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        _context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = _testTenantId,
            ProductId = productId,
            IsDefault = true,
            IsActive = true
        });

        var initialStock = new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = 10,
            ReservedQuantity = 0
        };
        _context.ProductVariantStocks.Add(initialStock);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AdjustStockAsync(warehouseId, productId, 5, StockMovementType.Adjustment, "Test adjustment");
        await _context.SaveChangesAsync();

        // Assert
        var stock = await _context.ProductVariantStocks.FirstAsync(ws => ws.Id == initialStock.Id);
        Assert.Equal(15, stock.Quantity);
        Assert.Equal(5, result.QuantityChange);
        Assert.Equal(15, result.QuantityAfter);
        Assert.Equal(variantId, result.ProductVariantId);
    }

    [Fact]
    public async Task AdjustStockAsync_CreatesRecord_WhenNoneExists()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        _context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = _testTenantId,
            ProductId = productId,
            IsDefault = true,
            IsActive = true
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.AdjustStockAsync(warehouseId, productId, 10, StockMovementType.InitialStock, "Initial stock");
        await _context.SaveChangesAsync();

        // Assert
        var stock = await _context.ProductVariantStocks.FirstOrDefaultAsync(ws => ws.WarehouseId == warehouseId && ws.VariantId == variantId);
        Assert.NotNull(stock);
        Assert.Equal(10, stock.Quantity);
        Assert.Empty(_context.WarehouseStocks);
        Assert.Equal(variantId, result.ProductVariantId);
    }

    [Fact]
    public async Task AdjustStockAsync_ThrowsException_WhenInsufficientStock()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        _context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = _testTenantId,
            ProductId = productId,
            IsDefault = true,
            IsActive = true
        });

        var initialStock = new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            WarehouseId = warehouseId,
            VariantId = variantId,
            Quantity = 5,
            ReservedQuantity = 0
        };
        _context.ProductVariantStocks.Add(initialStock);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InsufficientStockException>(() => 
            _service.AdjustStockAsync(warehouseId, productId, -10, StockMovementType.Adjustment, "Reduce too much"));
    }

    [Fact]
    public async Task TransferStockAsync_TransfersCorrectly()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        _context.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = _testTenantId,
            ProductId = productId,
            IsDefault = true,
            IsActive = true
        });

        _context.ProductVariantStocks.AddRange(
            new ProductVariantStock { Id = Guid.NewGuid(), TenantId = _testTenantId, WarehouseId = sourceId, VariantId = variantId, Quantity = 100 },
            new ProductVariantStock { Id = Guid.NewGuid(), TenantId = _testTenantId, WarehouseId = targetId, VariantId = variantId, Quantity = 0 }
        );
        await _context.SaveChangesAsync();

        // Act
        await _service.TransferStockAsync(sourceId, targetId, productId, 20, "Test transfer");
        await _context.SaveChangesAsync();

        // Assert
        var sourceStock = await _context.ProductVariantStocks.FirstAsync(ws => ws.WarehouseId == sourceId && ws.VariantId == variantId);
        var targetStock = await _context.ProductVariantStocks.FirstAsync(ws => ws.WarehouseId == targetId && ws.VariantId == variantId);

        Assert.Equal(80, sourceStock.Quantity);
        Assert.Equal(20, targetStock.Quantity);

        var movements = await _context.StockMovements.ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, m => m.MovementType == StockMovementType.TransferOut && m.WarehouseId == sourceId && m.QuantityChange == -20);
        Assert.Contains(movements, m => m.MovementType == StockMovementType.TransferIn && m.WarehouseId == targetId && m.QuantityChange == 20);
        Assert.Empty(_context.WarehouseStocks);
    }
}
