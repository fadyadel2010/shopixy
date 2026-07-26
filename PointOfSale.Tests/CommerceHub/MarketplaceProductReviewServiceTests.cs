using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.MarketplaceReviews;
using PointOfSale.Core.DTOs.MarketplaceReviews;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class MarketplaceProductReviewServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IMarketplaceEventOutboxService> _eventOutbox = new();
    private readonly MarketplaceProductReviewService _service;

    public MarketplaceProductReviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new MarketplaceProductReviewService(
            _dbContext,
            _eventOutbox.Object,
            NullLogger<MarketplaceProductReviewService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_WhenCustomerHasNoDeliveredPurchase_ShouldThrow()
    {
        var customer = await SeedCustomerAsync();
        await SeedPublishedProductAsync(customer.Id);

        var exception = await Assert.ThrowsAsync<MarketplaceProductReviewException>(() =>
            _service.CreateAsync(
                customer.Id,
                new MarketplaceCreateProductReviewRequest
                {
                    ProductId = SeedState.ProductId,
                    Rating = 5,
                    Comment = "Excellent product."
                },
                "corr-no-purchase"));

        Assert.Equal("PURCHASE_REQUIRED", exception.Code);
        Assert.Empty(_dbContext.ProductReviews);
    }

    [Fact]
    public async Task CreateAsync_WhenDuplicateReviewExists_ShouldThrow()
    {
        var customer = await SeedCustomerAsync();
        await SeedPublishedProductAsync(customer.Id);
        await SeedDeliveredPurchaseAsync(customer.Id);

        _dbContext.ProductReviews.Add(ProductReview.Create(
            SeedState.ProductId,
            customer.Id,
            4,
            "Existing",
            "Already reviewed.",
            true));
        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MarketplaceProductReviewException>(() =>
            _service.CreateAsync(
                customer.Id,
                new MarketplaceCreateProductReviewRequest
                {
                    ProductId = SeedState.ProductId,
                    Rating = 5,
                    Comment = "Trying again."
                },
                "corr-duplicate"));

        Assert.Equal("REVIEW_ALREADY_EXISTS", exception.Code);
        Assert.Single(_dbContext.ProductReviews);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<CommerceHubCustomer> SeedCustomerAsync()
    {
        var customer = CommerceHubCustomer.Create("reviewer@test.local", "Review Tester", "01000000000", "hash");
        _dbContext.CommerceHubCustomers.Add(customer);
        await _dbContext.SaveChangesAsync();
        return customer;
    }

    private async Task SeedPublishedProductAsync(Guid customerId)
    {
        var tenantId = SeedState.TenantId;
        var productId = SeedState.ProductId;
        var variantId = SeedState.VariantId;

        if (!await _dbContext.Tenants.AnyAsync(item => item.Id == tenantId))
        {
            _dbContext.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Review Store",
                Currency = "EGP",
                IsActive = true,
                Status = TenantStatus.Active
            });
        }

        if (!await _dbContext.Products.IgnoreQueryFilters().AnyAsync(item => item.Id == productId))
        {
            _dbContext.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Marketplace Review Product",
                CategoryId = Guid.NewGuid(),
                SalePrice = 100m,
                OnlinePrice = 100m,
                IsActive = true,
                IsOnlineVisible = true
            });
        }

        if (!await _dbContext.ProductVariants.IgnoreQueryFilters().AnyAsync(item => item.Id == variantId))
        {
            _dbContext.ProductVariants.Add(new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                Price = 100m,
                OnlinePrice = 100m,
                CostPrice = 60m,
                IsDefault = true,
                IsActive = true
            });
        }

        if (!await _dbContext.CommerceHubListings.IgnoreQueryFilters().AnyAsync(item => item.ProductId == productId && item.VariantId == variantId))
        {
            _dbContext.CommerceHubListings.Add(CommerceHubListing.Create(
                tenantId,
                productId,
                variantId,
                Guid.NewGuid(),
                null));
        }

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task SeedDeliveredPurchaseAsync(Guid customerId)
    {
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        _dbContext.CommerceHubMarketplaceOrders.Add(new CommerceHubMarketplaceOrder
        {
            Id = orderId,
            CustomerId = customerId,
            OrderNumber = $"MP-{Guid.NewGuid():N}"[..12],
            FullName = "Review Tester",
            PhoneNumber = "01000000000",
            AddressLine = "123 Test Street",
            City = "Cairo",
            PaymentMethodCode = "cod",
            PaymentMethodName = "Cash on delivery",
            Subtotal = 100m,
            TotalAmount = 100m,
            ShippingTotal = 0m,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        _dbContext.CommerceHubSubOrders.Add(new CommerceHubSubOrder
        {
            Id = subOrderId,
            MarketplaceOrderId = orderId,
            OrderNumber = $"SO-{Guid.NewGuid():N}"[..12],
            StoreId = SeedState.TenantId,
            StoreName = "Review Store",
            Subtotal = 100m,
            Total = 100m,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        _dbContext.CommerceHubSubOrderItems.Add(new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrderId,
            ListingId = _dbContext.CommerceHubListings.IgnoreQueryFilters().Single().Id,
            ProductId = SeedState.ProductId,
            VariantId = SeedState.VariantId,
            CategoryId = Guid.NewGuid(),
            ProductName = "Marketplace Review Product",
            Quantity = 1,
            UnitPrice = 100m,
            Total = 100m,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private static class SeedState
    {
        public static readonly Guid TenantId = Guid.NewGuid();
        public static readonly Guid ProductId = Guid.NewGuid();
        public static readonly Guid VariantId = Guid.NewGuid();
    }
}
