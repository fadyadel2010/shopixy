using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class OrderRuntimeStage1CertificationTests
{
    [Theory]
    [InlineData(nameof(Order.Currency), "USD")]
    [InlineData(nameof(Order.Total), 125.50)]
    [InlineData(nameof(Order.PricingSnapshotJson), "{\"total\":125.50}")]
    [InlineData(nameof(Order.PaymentMethodCode), "PAYMOB")]
    [InlineData(nameof(Order.CartId), "11111111-1111-1111-1111-111111111111")]
    [InlineData(nameof(Order.OnlineCustomerId), "22222222-2222-2222-2222-222222222222")]
    public async Task PersistedOrder_SnapshotsCannotBeRewritten(string propertyName, object replacement)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var order = CreateOrder(tenantId);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var property = context.Entry(order).Property(propertyName);
        property.CurrentValue = CoerceValue(replacement, property.Metadata.ClrType);
        property.IsModified = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("immutable after creation", ex.Message, StringComparison.Ordinal);
        Assert.Contains(propertyName, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(OrderItem.UnitPrice), 99.99)]
    [InlineData(nameof(OrderItem.VariantAttributesJson), "{\"Color\":\"Blue\"}")]
    [InlineData(nameof(OrderItem.Quantity), 3)]
    [InlineData(nameof(OrderItem.AffiliateCommissionRateSnapshot), 0.25)]
    public async Task PersistedOrderItem_SkuAndPricingSnapshotsCannotBeRewritten(string propertyName, object replacement)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var order = CreateOrder(tenantId);
        var item = CreateOrderItem(order.Id);
        order.Items.Add(item);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var property = context.Entry(item).Property(propertyName);
        property.CurrentValue = CoerceValue(replacement, property.Metadata.ClrType);
        property.IsModified = true;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("immutable after creation", ex.Message, StringComparison.Ordinal);
        Assert.Contains(propertyName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistedRefund_FinancialFactsCannotBeRewritten()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var order = CreateOrder(tenantId);
        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            RefundId = "refund-1",
            Amount = 10m,
            Currency = "EGP",
            IsFullRefund = false,
            Reason = "Customer request",
            PaymentMethod = "CARD",
            RefundedAtUtc = DateTime.UtcNow
        };

        context.Orders.Add(order);
        context.OrderRefunds.Add(refund);
        await context.SaveChangesAsync();

        refund.Amount = 20m;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("Order refund financial facts are immutable", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(OrderRefund.Amount), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage1Guard_MustBePartOfSaveChangesPipeline()
    {
        var source = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("GuardStorefrontOrderHistoricalSnapshotImmutability();", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Order.Currency)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Order.PricingSnapshotJson)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(OrderItem.UnitPrice)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(OrderRefund.Amount)", source, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OrderRuntimeStage1_{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static Order CreateOrder(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        OrderNumber = $"ORD-{Guid.NewGuid():N}",
        OnlineCustomerId = Guid.NewGuid(),
        CartId = Guid.NewGuid(),
        GuestEmail = "customer@example.test",
        GuestPhone = "+201000000000",
        GuestName = "Test Customer",
        IsGuestCheckout = false,
        ShippingAddress = "1 Test Street",
        ShippingCity = "Cairo",
        ShippingCountry = "EG",
        ShippingMethodId = Guid.NewGuid(),
        ShippingMethodName = "Standard",
        ShippingCost = 10m,
        EstimatedDeliveryDays = "3-5 days",
        Subtotal = 100m,
        TotalDiscountAmount = 5m,
        TaxAmount = 0m,
        Total = 105m,
        Currency = "EGP",
        PricingSnapshotJson = "{\"total\":105.00}",
        PaymentMethod = PaymentMethod.Cash,
        PaymentMethodCode = "COD",
        PaymentMethodName = "Cash on Delivery",
        Source = OrderSource.Ecommerce
    };

    private static OrderItem CreateOrderItem(Guid orderId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        ProductId = Guid.NewGuid(),
        VariantId = Guid.NewGuid(),
        SKU = "SKU-1",
        ProductName = "Snapshot Product",
        VariantName = "Red",
        VariantAttributesJson = "{\"Color\":\"Red\"}",
        Quantity = 1,
        UnitPrice = 100m,
        CostPrice = 40m,
        Total = 100m,
        AffiliateCommissionEligible = true,
        AffiliateCommissionPolicyIdSnapshot = Guid.NewGuid(),
        AffiliateCommissionRateSnapshot = 0.10m,
        AffiliateCommissionBaseAmountSnapshot = 100m,
        AffiliateCommissionAmountSnapshot = 10m,
        AffiliateCommissionSnapshotCapturedAtUtc = DateTime.UtcNow
    };

    private static object? CoerceValue(object value, Type targetType)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType != null)
        {
            targetType = nullableType;
        }

        if (targetType == typeof(Guid))
        {
            return Guid.Parse(value.ToString()!);
        }

        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, value.ToString()!);
        }

        return Convert.ChangeType(value, targetType);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
