using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Controllers;

public sealed class PurchaseReturnsInvoiceReadTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly Guid _otherSupplierId = Guid.NewGuid();
    private readonly ApplicationDbContext _context;
    private readonly PurchaseReturnsController _controller;

    public PurchaseReturnsInvoiceReadTests()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(context => context.TenantId).Returns(_tenantId);
        tenantContext.Setup(context => context.RequiredTenantId).Returns(_tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"PurchaseReturnsInvoiceRead_{Guid.NewGuid():N}")
            .Options;
        _context = new ApplicationDbContext(options, tenantContext.Object);

        _controller = new PurchaseReturnsController(
            _context,
            tenantContext.Object,
            NullLogger<PurchaseReturnsController>.Instance,
            Mock.Of<ILedgerService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IAccountingOutboxService>(),
            Mock.Of<ICommerceAccountingService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task EligibleInvoices_AreSupplierTenantAndLifecycleScoped()
    {
        var eligible = AddInvoice(_tenantId, _supplierId, InvoiceStatus.Completed, "PUR-ELIGIBLE");
        AddInvoice(_tenantId, _otherSupplierId, InvoiceStatus.Completed, "PUR-OTHER-SUPPLIER");
        AddInvoice(_tenantId, _supplierId, InvoiceStatus.FullyReturned, "PUR-FULLY-RETURNED");
        AddInvoice(_otherTenantId, Guid.NewGuid(), InvoiceStatus.Completed, "PUR-OTHER-TENANT");
        await _context.SaveChangesAsync();

        var result = await _controller.GetEligiblePurchaseInvoices(_supplierId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var invoices = Assert.IsAssignableFrom<IReadOnlyList<PurchaseReturnEligibleInvoiceResponseDto>>(ok.Value);
        var invoice = Assert.Single(invoices);
        Assert.Equal(eligible.Id, invoice.Id);
        Assert.Equal("PUR-ELIGIBLE", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task InvoiceDetail_UsesScalarProductContractAndSubtractsPriorReturns()
    {
        var invoice = AddInvoice(
            _tenantId,
            _supplierId,
            InvoiceStatus.PartiallyReturned,
            "PUR-PARTIAL",
            quantity: 3m,
            conversionFactor: 10m,
            totalAmount: 330m,
            taxAmount: 30m);
        var item = Assert.Single(invoice.Items);
        var priorReturn = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ReturnNumber = "PR-PRIOR",
            ReturnDate = DateTime.UtcNow,
            ReturnType = ReturnType.PurchaseReturn,
            OriginalPurchaseInvoiceId = invoice.Id,
            OriginalInvoiceNumber = invoice.InvoiceNumber,
            SupplierId = invoice.SupplierId,
            WarehouseId = invoice.WarehouseId,
            CreatedAt = DateTime.UtcNow
        };
        priorReturn.Items.Add(new ReturnItem
        {
            Id = Guid.NewGuid(),
            ReturnId = priorReturn.Id,
            ProductId = item.ProductId,
            ProductVariantId = item.ProductVariantId,
            UnitId = item.UnitId,
            Quantity = 1m,
            ConversionFactor = 10m,
            BaseQuantity = 10m,
            UnitPrice = 100m,
            TaxRate = 10m,
            TaxAmount = 10m,
            TotalAmount = 110m,
            CreatedAt = DateTime.UtcNow
        });
        _context.Returns.Add(priorReturn);
        await _context.SaveChangesAsync();

        var result = await _controller.GetPurchaseInvoiceForReturn(invoice.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PurchaseReturnInvoiceResponseDto>(ok.Value);
        var responseItem = Assert.Single(response.Items);
        Assert.Equal("Coffee", responseItem.ProductName);
        Assert.Equal(3m, responseItem.PurchasedQuantity);
        Assert.Equal(1m, responseItem.ReturnedQuantity);
        Assert.Equal(2m, responseItem.RemainingQuantity);
        Assert.Equal(100m, responseItem.UnitPrice);
        Assert.Equal(10m, responseItem.TaxRate);
        Assert.Equal(item.Id, Assert.Single(responseItem.OriginalInvoiceItemIds));
    }

    private PurchaseInvoice AddInvoice(
        Guid tenantId,
        Guid supplierId,
        InvoiceStatus status,
        string invoiceNumber,
        decimal quantity = 1m,
        decimal conversionFactor = 1m,
        decimal totalAmount = 11m,
        decimal taxAmount = 1m)
    {
        var supplier = _context.Suppliers.Local.FirstOrDefault(candidate => candidate.Id == supplierId)
            ?? new Supplier
            {
                Id = supplierId,
                TenantId = tenantId,
                Name = $"Supplier {supplierId:N}",
                CreatedAt = DateTime.UtcNow
            };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = $"Warehouse {invoiceNumber}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Carton",
            Symbol = "ctn",
            CreatedAt = DateTime.UtcNow
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = Guid.NewGuid(),
            Name = "Coffee",
            CreatedAt = DateTime.UtcNow
        };
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            Product = product,
            SKU = "COFFEE-1KG",
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var invoice = new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = invoiceNumber,
            InvoiceDate = DateTime.UtcNow,
            SupplierId = supplier.Id,
            Supplier = supplier,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Status = status,
            Subtotal = totalAmount - taxAmount,
            TaxAmount = taxAmount,
            TotalAmount = totalAmount,
            CreatedAt = DateTime.UtcNow
        };
        invoice.Items.Add(new PurchaseInvoiceItem
        {
            Id = Guid.NewGuid(),
            PurchaseInvoiceId = invoice.Id,
            ProductId = product.Id,
            Product = product,
            ProductVariantId = variant.Id,
            ProductVariant = variant,
            UnitId = unit.Id,
            Unit = unit,
            Quantity = quantity,
            ConversionFactor = conversionFactor,
            BaseQuantity = quantity * conversionFactor,
            UnitPrice = (totalAmount - taxAmount) / quantity,
            TaxRate = taxAmount * 100m / (totalAmount - taxAmount),
            TaxAmount = taxAmount,
            TotalAmount = totalAmount,
            CreatedAt = DateTime.UtcNow
        });

        _context.PurchaseInvoices.Add(invoice);
        return invoice;
    }

    public void Dispose() => _context.Dispose();
}
