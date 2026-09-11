using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubPaymentRecoveryAuthorityServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubPaymentRecoveryAuthorityService _service;

    public CommerceHubPaymentRecoveryAuthorityServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _service = new CommerceHubPaymentRecoveryAuthorityService(_dbContext);
    }

    [Fact]
    public async Task EvaluateAsync_WhenPendingPaymentHasOutcomeUnknownGap_ShouldBlockRetry()
    {
        var order = BuildPendingPaymobOrder();
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = order.TotalAmount,
            Currency = order.Currency,
            Status = CommerceHubPaymentRecordStatus.Pending,
            Provider = PaymentProvider.Paymob,
            ProviderReference = $"paymob-{Guid.NewGuid():N}"[..39],
            FailureReason = "PROVIDER_SESSION_OUTCOME_UNKNOWN: provider accepted request but POS could not confirm the handoff.",
            IsProcessed = false
        };

        order.Payments.Add(payment);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            await _dbContext.SaveChangesAsync();
        }

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.False(decision.CanRetryPayment);
        Assert.False(decision.HasUnresolvedCapturedPayment);
        Assert.Contains("provider-session reconciliation", decision.PaymentRecoveryMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_WhenProviderIdentityWasLoggedWithoutReusableSession_ShouldBlockRetry()
    {
        var order = BuildPendingPaymobOrder();
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            Amount = order.TotalAmount,
            Currency = order.Currency,
            Status = CommerceHubPaymentRecordStatus.Pending,
            Provider = PaymentProvider.Paymob,
            ProviderReference = $"paymob-{Guid.NewGuid():N}"[..39],
            IsProcessed = false
        };

        payment.Logs.Add(new CommerceHubPaymentLog
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            EventType = "initiate.paymob",
            ProviderEventId = $"pi_{Guid.NewGuid():N}"[..20],
            RawResponse = "{}"
        });

        order.Payments.Add(payment);

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            await _dbContext.SaveChangesAsync();
        }

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.OnHold);

        Assert.False(decision.CanRetryPayment);
        Assert.False(decision.HasUnresolvedCapturedPayment);
        Assert.Contains("provider-session reconciliation", decision.PaymentRecoveryMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationAuthorityIsMissingAndNoPaymentEvidence_ShouldAllowGuardedReacquisition()
    {
        var order = await SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(activeReservation: false);

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.True(decision.CanRetryPayment);
        Assert.True(decision.HasReservationAuthorityLoss);
        Assert.True(decision.RequiresReservationReacquisition);
        Assert.False(decision.HasUnresolvedCapturedPayment);
        Assert.Null(decision.PaymentRecoveryMessage);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationAuthorityIsMissingAndPaymentEvidenceExists_ShouldBlockRetry()
    {
        var order = await SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(activeReservation: false);
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubPayments.Add(new CommerceHubPayment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Currency = order.Currency,
                Provider = PaymentProvider.Paymob,
                ProviderReference = $"paymob-{Guid.NewGuid():N}"[..39],
                ProviderIntentId = $"provider-{Guid.NewGuid():N}",
                Status = CommerceHubPaymentRecordStatus.Failed,
                IsProcessed = true
            });
            await _dbContext.SaveChangesAsync();
        }

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.False(decision.CanRetryPayment);
        Assert.True(decision.HasReservationAuthorityLoss);
        Assert.False(decision.RequiresReservationReacquisition);
        Assert.Contains("reservation authority", decision.PaymentRecoveryMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationExpiredAndAllFailedPaymentsAreEvidenceFree_ShouldAllowGuardedReacquisition()
    {
        var order = await SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(activeReservation: false);
        var payment = new CommerceHubPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Amount = order.TotalAmount,
            Currency = order.Currency,
            Provider = PaymentProvider.Paymob,
            ProviderReference = $"paymob-{Guid.NewGuid():N}"[..39],
            ProviderIntentId = $"{CommerceHubPaymentSessionRecoveryAuthority.ProviderSessionReinitFencePrefix}{Guid.NewGuid():N}",
            Status = CommerceHubPaymentRecordStatus.Failed,
            FailureReason = "PROVIDER_SESSION_RECONCILED_NOT_CREATED: operator confirmed no matching provider session.",
            IsProcessed = false
        };
        payment.Attempts.Add(new CommerceHubPaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            Provider = PaymentProvider.Paymob,
            AttemptNumber = 1,
            ProviderReference = payment.ProviderReference,
            ProviderIntentId = $"{CommerceHubPaymentSessionRecoveryAuthority.ProviderSessionReinitFencePrefix}{Guid.NewGuid():N}",
            Status = CommerceHubPaymentRecordStatus.Failed,
            FailureReason = payment.FailureReason,
            CompletedAtUtc = DateTime.UtcNow
        });
        payment.Logs.Add(new CommerceHubPaymentLog
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Payment = payment,
            EventType = "initiate.paymob.outcome_reconciled_not_created",
            RawResponse = "{}"
        });

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubPayments.Add(payment);
            await _dbContext.SaveChangesAsync();
        }

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.True(decision.CanRetryPayment);
        Assert.True(decision.HasReservationAuthorityLoss);
        Assert.True(decision.RequiresReservationReacquisition);
        Assert.Null(decision.PaymentRecoveryMessage);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationAuthorityIsMissingAndDeletedPaymentEvidenceExists_ShouldBlockRetry()
    {
        var order = await SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(activeReservation: false);
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubPayments.Add(new CommerceHubPayment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Currency = order.Currency,
                Provider = PaymentProvider.Paymob,
                ProviderReference = $"paymob-{Guid.NewGuid():N}"[..39],
                Status = CommerceHubPaymentRecordStatus.Failed,
                IsProcessed = true,
                IsDeleted = true
            });
            await _dbContext.SaveChangesAsync();
        }

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.False(decision.CanRetryPayment);
        Assert.True(decision.HasReservationAuthorityLoss);
        Assert.False(decision.RequiresReservationReacquisition);
    }

    [Fact]
    public async Task EvaluateAsync_WhenOnlyUndamagedAvailableStockIsInsufficient_ShouldBlockRetry()
    {
        var order = await SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(activeReservation: false);
        var stock = await _dbContext.ProductVariantStocks.IgnoreQueryFilters().SingleAsync();
        stock.Quantity = 2m;
        stock.DamagedQuantity = 1m;
        await _dbContext.SaveChangesAsync();

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.False(decision.CanRetryPayment);
        Assert.True(decision.HasReservationAuthorityLoss);
        Assert.False(decision.RequiresReservationReacquisition);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationAuthorityIsActive_ShouldAllowRetry()
    {
        var order = await SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(activeReservation: true);

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.True(decision.CanRetryPayment);
        Assert.False(decision.HasReservationAuthorityLoss);
        Assert.False(decision.RequiresReservationReacquisition);
        Assert.False(decision.HasUnresolvedCapturedPayment);
        Assert.Null(decision.PaymentRecoveryMessage);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationWarehouseIsInactive_ShouldBlockRetry()
    {
        var order = await SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(
            activeReservation: true,
            warehouseActive: false);

        var decision = await _service.EvaluateAsync(
            order.Id,
            CommerceHubPaymentMethods.Paymob,
            PaymentStatus.Pending,
            OrderStatus.Pending);

        Assert.False(decision.CanRetryPayment);
        Assert.True(decision.HasReservationAuthorityLoss);
        Assert.False(decision.HasUnresolvedCapturedPayment);
        Assert.Contains("reservation authority", decision.PaymentRecoveryMessage, StringComparison.OrdinalIgnoreCase);
    }

    private CommerceHubMarketplaceOrder BuildPendingPaymobOrder()
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = $"CH-REC-{Guid.NewGuid():N}"[..15],
            FullName = "Recovery Test",
            PhoneNumber = "01000000000",
            AddressLine = "123 Recovery Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = CommerceHubPaymentMethods.Paymob,
            PaymentMethodName = "Paymob",
            PaymentReference = $"payref-{Guid.NewGuid():N}"[..20],
            Subtotal = 120m,
            ShippingTotal = 0m,
            TotalAmount = 120m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

        order.SubOrders.Add(new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{order.OrderNumber}-1",
            StoreId = Guid.NewGuid(),
            StoreName = "Recovery Store",
            Currency = "EGP",
            Subtotal = 120m,
            Shipping = 0m,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = CommerceHubPaymentMethods.Paymob,
            PaymentMethodName = "Paymob",
            Total = 120m,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        });

        return order;
    }

    private async Task<CommerceHubMarketplaceOrder> SeedPendingPaymobOrderWithSqlReservationAuthorityAsync(
        bool activeReservation,
        bool warehouseActive = true)
    {
        var tenantId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var order = BuildPendingPaymobOrder();
        var subOrder = order.SubOrders.Single();
        subOrder.StoreId = tenantId;
        var orderItemId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        subOrder.Items.Add(new CommerceHubSubOrderItem
        {
            Id = orderItemId,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = productId,
            VariantId = variantId,
            ProductName = "Recovery Authority Product",
            Quantity = 2,
            UnitPrice = 60m,
            Total = 120m,
            Status = CommerceHubOrderItemStatus.Pending,
            CreatedAt = nowUtc
        });

        _dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Recovery Authority Store",
            Currency = "EGP",
            CreatedAtUtc = nowUtc
        });
        _dbContext.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Recovery Authority Warehouse",
            IsActive = warehouseActive,
            CreatedAt = nowUtc
        });
        _dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Recovery Authority Category",
            CreatedAt = nowUtc
        });
        _dbContext.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = "Recovery Authority Product",
            SalePrice = 60m,
            OnlinePrice = 60m,
            PurchasePrice = 30m,
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            TrackStock = true,
            CreatedAt = nowUtc
        });
        _dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            TenantId = tenantId,
            ProductId = productId,
            SKU = $"REC-{Guid.NewGuid():N}"[..16],
            Price = 60m,
            OnlinePrice = 60m,
            CostPrice = 30m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = nowUtc
        });
        _dbContext.ProductVariantStocks.Add(new ProductVariantStock
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            Quantity = 4m,
            ReservedQuantity = activeReservation ? 2m : 0m,
            CreatedAt = nowUtc
        });

        _dbContext.InventoryReservations.Add(new InventoryReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SubOrderId = subOrder.Id,
            OrderItemId = orderItemId,
            ProductId = productId,
            VariantId = variantId,
            WarehouseId = warehouseId,
            ReservedQuantity = 2m,
            ReleasedQuantity = activeReservation ? 0m : 2m,
            Status = activeReservation ? InventoryReservationStatus.Active : InventoryReservationStatus.Released,
            ReservationEventId = Guid.NewGuid(),
            ExpiresAtUtc = activeReservation ? nowUtc.AddMinutes(10) : nowUtc.AddMinutes(-1),
            CreatedAt = nowUtc
        });

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            _dbContext.CommerceHubMarketplaceOrders.Add(order);
            await _dbContext.SaveChangesAsync();
        }

        return order;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
