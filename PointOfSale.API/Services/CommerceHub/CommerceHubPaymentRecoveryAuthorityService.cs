using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubPaymentRecoveryAuthorityService
{
    Task<CommerceHubPaymentRecoveryAuthorityDecision> EvaluateAsync(
        Guid orderId,
        string paymentMethodCode,
        PaymentStatus paymentStatus,
        OrderStatus orderStatus,
        CancellationToken cancellationToken = default);
}

public sealed record CommerceHubPaymentRecoveryAuthorityDecision(
    bool CanRetryPayment,
    string? PaymentRecoveryMessage,
    bool HasUnresolvedCapturedPayment,
    bool HasReservationAuthorityLoss = false,
    bool RequiresReservationReacquisition = false);

public class CommerceHubPaymentRecoveryAuthorityService : ICommerceHubPaymentRecoveryAuthorityService
{
    private const string PendingTenantResolutionStatus = "PendingTenantResolution";
    private const string RoutedToSuspenseStatus = "RoutedToSuspense";
    private const string SuspenseMatchedPendingClearanceStatus = "SuspenseMatchedPaymentPendingClearance";
    private const string PaymentRecoveryReviewMessage =
        "Payment is under manual review because the provider already reported a captured or ambiguous payment outcome. Do not retry payment until reconciliation completes.";
    private const string ProviderSessionRecoveryBlockedMessage =
        "Payment initiation is waiting for provider-session reconciliation. Do not retry payment until POS confirms whether an external payment session was created.";
    private const string ReservationAuthorityLostMessage =
        "Payment retry is blocked because the marketplace order no longer has active SQL reservation authority. Restart checkout so inventory and payment authority are recreated from current SQL truth.";

    private readonly ApplicationDbContext _dbContext;

    public CommerceHubPaymentRecoveryAuthorityService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CommerceHubPaymentRecoveryAuthorityDecision> EvaluateAsync(
        Guid orderId,
        string paymentMethodCode,
        PaymentStatus paymentStatus,
        OrderStatus orderStatus,
        CancellationToken cancellationToken = default)
    {
        var provider = TryResolvePaymentProvider(paymentMethodCode, out var resolvedProvider)
            ? resolvedProvider
            : (PaymentProvider?)null;

        var hasUnresolvedCapturedPayment = await _dbContext.CommerceHubUnresolvedPaymentWebhooks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                webhook =>
                    webhook.OrderId == orderId &&
                    (!provider.HasValue || webhook.Provider == provider.Value) &&
                    (webhook.Status == PendingTenantResolutionStatus ||
                     webhook.Status == RoutedToSuspenseStatus ||
                     webhook.Status == SuspenseMatchedPendingClearanceStatus),
                cancellationToken);

        var hasProviderSessionRecoveryBlock = false;
        var hasAnyPaymentAuthorityEvidence = false;
        if (provider.HasValue)
        {
            var orderPayments = await _dbContext.CommerceHubPayments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(payment => payment.Attempts)
                .Include(payment => payment.Logs)
                .AsSplitQuery()
                .Where(payment =>
                    payment.OrderId == orderId)
                .OrderByDescending(payment => payment.CreatedAt)
                .ToListAsync(cancellationToken);

            hasAnyPaymentAuthorityEvidence = orderPayments.Any(
                    CommerceHubPaymentSessionRecoveryAuthority.HasReservationReacquisitionBlockingEvidence) ||
                await _dbContext.CommerceHubUnresolvedPaymentWebhooks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(
                        webhook =>
                            webhook.OrderId == orderId &&
                            webhook.Provider == provider.Value,
                        cancellationToken);
            hasProviderSessionRecoveryBlock = orderPayments
                .Where(payment => payment.Provider == provider.Value)
                .Any(
                CommerceHubPaymentSessionRecoveryAuthority.HasProviderSessionPersistenceGap);
        }

        var canRetryPaymentByOrderState =
            !hasUnresolvedCapturedPayment &&
            !hasProviderSessionRecoveryBlock &&
            provider.HasValue &&
            paymentStatus == PaymentStatus.Pending &&
            !string.IsNullOrWhiteSpace(paymentMethodCode) &&
            !string.Equals(paymentMethodCode, CommerceHubPaymentMethods.Cod, StringComparison.OrdinalIgnoreCase) &&
            (orderStatus == OrderStatus.Pending || orderStatus == OrderStatus.OnHold);

        var hasReservationAuthorityLoss = false;
        if (canRetryPaymentByOrderState)
        {
            hasReservationAuthorityLoss = !await HasActiveMarketplaceReservationAuthorityAsync(
                orderId,
                cancellationToken);
        }

        var requiresReservationReacquisition =
            canRetryPaymentByOrderState &&
            hasReservationAuthorityLoss &&
            !hasAnyPaymentAuthorityEvidence &&
            await HasMarketplaceReservationReacquisitionAuthorityAsync(orderId, cancellationToken);
        var canRetryPayment = canRetryPaymentByOrderState &&
            (!hasReservationAuthorityLoss || requiresReservationReacquisition);

        var paymentRecoveryMessage = hasUnresolvedCapturedPayment
            ? PaymentRecoveryReviewMessage
            : hasProviderSessionRecoveryBlock
                ? ProviderSessionRecoveryBlockedMessage
                : hasReservationAuthorityLoss && !requiresReservationReacquisition
                    ? ReservationAuthorityLostMessage
                : null;

        return new CommerceHubPaymentRecoveryAuthorityDecision(
            canRetryPayment,
            paymentRecoveryMessage,
            hasUnresolvedCapturedPayment,
            hasReservationAuthorityLoss,
            requiresReservationReacquisition);
    }

    private async Task<bool> HasActiveMarketplaceReservationAuthorityAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var expectedItems = await _dbContext.CommerceHubSubOrderItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.SubOrder.MarketplaceOrderId == orderId &&
                !item.IsDeleted &&
                !item.SubOrder.IsDeleted &&
                !item.SubOrder.MarketplaceOrder.IsDeleted &&
                item.SubOrder.MarketplaceOrder.Status != OrderStatus.Cancelled &&
                item.SubOrder.MarketplaceOrder.Status != OrderStatus.Refunded &&
                item.SubOrder.Status != OrderStatus.Cancelled &&
                item.SubOrder.Status != OrderStatus.Refunded &&
                item.SubOrder.PaymentMethod == CommerceHubPaymentMethod.Online &&
                item.Status != CommerceHubOrderItemStatus.Cancelled &&
                item.Quantity > 0)
            .Select(item => new
            {
                TenantId = item.SubOrder.StoreId,
                OrderItemId = item.Id,
                item.ProductId,
                item.VariantId,
                RequiredQuantity = (decimal)item.Quantity
            })
            .ToListAsync(cancellationToken);

        if (expectedItems.Count == 0)
        {
            return false;
        }

        var tenantIds = expectedItems.Select(item => item.TenantId).Distinct().ToList();
        var productIds = expectedItems.Select(item => item.ProductId).Distinct().ToList();
        var variantIds = expectedItems.Select(item => item.VariantId).Distinct().ToList();

        var activeProducts = await _dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(product =>
                tenantIds.Contains(product.TenantId) &&
                productIds.Contains(product.Id) &&
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published &&
                product.TrackStock)
            .Select(product => new { product.TenantId, product.Id })
            .ToListAsync(cancellationToken);

        var activeProductKeys = activeProducts
            .Select(product => (product.TenantId, product.Id))
            .ToHashSet();

        var activeVariants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(variant =>
                tenantIds.Contains(variant.TenantId) &&
                variantIds.Contains(variant.Id) &&
                !variant.IsDeleted &&
                variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                variant.SemanticSupersededAtUtc == null &&
                variant.SemanticReplacementVariantId == null &&
                variant.IsActive)
            .Select(variant => new { variant.TenantId, variant.ProductId, variant.Id })
            .ToListAsync(cancellationToken);

        var activeVariantKeys = activeVariants
            .Select(variant => (variant.TenantId, variant.ProductId, VariantId: variant.Id))
            .ToHashSet();

        if (expectedItems.Any(item =>
                !activeProductKeys.Contains((item.TenantId, item.ProductId)) ||
                !activeVariantKeys.Contains((item.TenantId, item.ProductId, item.VariantId))))
        {
            return false;
        }

        var rawReservations = await _dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reservation =>
                reservation.OrderId == orderId &&
                !reservation.IsDeleted &&
                reservation.Status == InventoryReservationStatus.Active &&
                reservation.ReservedQuantity > reservation.CommittedQuantity + reservation.ReleasedQuantity &&
                (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc.Value > nowUtc))
            .ToListAsync(cancellationToken);

        var reservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAcrossTenantsAsync(
            _dbContext,
            rawReservations,
            nowUtc,
            cancellationToken);

        var expectedKeys = expectedItems
            .Select(item => (item.TenantId, item.OrderItemId, item.VariantId))
            .ToHashSet();

        if (reservations.Any(reservation =>
                !expectedKeys.Contains((reservation.TenantId, reservation.OrderItemId, reservation.VariantId))))
        {
            return false;
        }

        var reservedByItem = reservations
            .GroupBy(reservation => new
            {
                reservation.TenantId,
                reservation.OrderItemId,
                reservation.VariantId
            })
                .ToDictionary(
                    group => (group.Key.TenantId, group.Key.OrderItemId, group.Key.VariantId),
                    group => group.Sum(reservation => reservation.RemainingReservedQuantity));

        if (expectedItems.Any(item =>
                reservedByItem.GetValueOrDefault((item.TenantId, item.OrderItemId, item.VariantId)) != item.RequiredQuantity))
        {
            return false;
        }

        var stockKeys = reservations
            .Select(reservation => new
            {
                reservation.TenantId,
                reservation.VariantId,
                reservation.WarehouseId
            })
            .Distinct()
            .ToList();

        foreach (var stockKey in stockKeys)
        {
            var requiredReservedQuantity = reservations
                .Where(reservation =>
                    reservation.TenantId == stockKey.TenantId &&
                    reservation.VariantId == stockKey.VariantId &&
                    reservation.WarehouseId == stockKey.WarehouseId)
                .Sum(reservation => reservation.RemainingReservedQuantity);

            var stockReservedQuantity = await _dbContext.ProductVariantStocks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .WhereCurrentInventoryStockOwner(stockKey.TenantId)
                .Where(stock =>
                    stock.TenantId == stockKey.TenantId &&
                    stock.VariantId == stockKey.VariantId &&
                    stock.WarehouseId == stockKey.WarehouseId &&
                    !stock.IsDeleted)
                .Select(stock => (decimal?)stock.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            if (stockReservedQuantity.GetValueOrDefault() < requiredReservedQuantity)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> HasMarketplaceReservationReacquisitionAuthorityAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var expectedItems = await _dbContext.CommerceHubSubOrderItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.SubOrder.MarketplaceOrderId == orderId &&
                !item.IsDeleted &&
                !item.SubOrder.IsDeleted &&
                !item.SubOrder.MarketplaceOrder.IsDeleted &&
                item.SubOrder.MarketplaceOrder.Status != OrderStatus.Cancelled &&
                item.SubOrder.MarketplaceOrder.Status != OrderStatus.Refunded &&
                item.SubOrder.Status != OrderStatus.Cancelled &&
                item.SubOrder.Status != OrderStatus.Refunded &&
                item.SubOrder.PaymentMethod == CommerceHubPaymentMethod.Online &&
                item.Status != CommerceHubOrderItemStatus.Cancelled &&
                item.Quantity > 0)
            .Select(item => new MarketplaceReacquisitionItem(
                item.SubOrder.StoreId,
                item.SubOrderId,
                item.Id,
                item.ProductId,
                item.VariantId,
                (decimal)item.Quantity))
            .ToListAsync(cancellationToken);

        if (expectedItems.Count == 0)
        {
            return false;
        }

        var tenantIds = expectedItems.Select(item => item.TenantId).Distinct().ToList();
        var productIds = expectedItems.Select(item => item.ProductId).Distinct().ToList();
        var variantIds = expectedItems.Select(item => item.VariantId).Distinct().ToList();
        var activeCatalogRows = await (
            from product in _dbContext.Products.IgnoreQueryFilters().AsNoTracking()
            join variant in _dbContext.ProductVariants.IgnoreQueryFilters().AsNoTracking()
                on product.Id equals variant.ProductId
            where tenantIds.Contains(product.TenantId) &&
                  productIds.Contains(product.Id) &&
                  variantIds.Contains(variant.Id) &&
                  product.TenantId == variant.TenantId &&
                  !product.IsDeleted &&
                  product.IsActive &&
                  product.IsOnlineVisible &&
                  product.PublishStatus == PublishStatus.Published &&
                  product.TrackStock &&
                  !variant.IsDeleted &&
                  variant.IsActive &&
                  variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                  variant.SemanticSupersededAtUtc == null &&
                  variant.SemanticReplacementVariantId == null
            select new { product.TenantId, ProductId = product.Id, VariantId = variant.Id })
            .ToListAsync(cancellationToken);
        var activeCatalogKeys = activeCatalogRows
            .Select(row => (row.TenantId, row.ProductId, row.VariantId))
            .ToHashSet();

        if (expectedItems.Any(item =>
                !activeCatalogKeys.Contains((item.TenantId, item.ProductId, item.VariantId))))
        {
            return false;
        }

        var reservationHistory = await _dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reservation => reservation.OrderId == orderId && !reservation.IsDeleted)
            .OrderBy(reservation => reservation.CreatedAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken);
        var expectedByItem = expectedItems.ToDictionary(item => item.OrderItemId);

        if (reservationHistory.Count == 0 ||
            reservationHistory.Any(reservation =>
                !expectedByItem.TryGetValue(reservation.OrderItemId, out var item) ||
                reservation.TenantId != item.TenantId ||
                reservation.SubOrderId != item.SubOrderId ||
                reservation.ProductId != item.ProductId ||
                reservation.VariantId != item.VariantId))
        {
            return false;
        }

        foreach (var item in expectedItems)
        {
            var itemHistory = reservationHistory
                .Where(reservation => reservation.OrderItemId == item.OrderItemId)
                .ToList();
            if (itemHistory.Count == 0)
            {
                return false;
            }

            var originalEventId = itemHistory[0].ReservationEventId;
            if (originalEventId == Guid.Empty ||
                itemHistory
                    .Where(reservation => reservation.ReservationEventId == originalEventId)
                    .Sum(reservation => reservation.ReservedQuantity) != item.RequiredQuantity)
            {
                return false;
            }
        }

        var nowUtc = DateTime.UtcNow;
        var rawActiveReservations = reservationHistory
            .Where(reservation =>
                reservation.Status == InventoryReservationStatus.Active &&
                reservation.RemainingReservedQuantity > 0m &&
                (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc))
            .ToList();
        var activeReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAcrossTenantsAsync(
            _dbContext,
            rawActiveReservations,
            nowUtc,
            cancellationToken);
        if (activeReservations.Count != rawActiveReservations.Count)
        {
            return false;
        }

        var activeByItem = activeReservations
            .GroupBy(reservation => (reservation.TenantId, reservation.OrderItemId, reservation.VariantId))
            .ToDictionary(group => group.Key, group => group.Sum(reservation => reservation.RemainingReservedQuantity));
        var missingByVariant = expectedItems
            .Select(item => new
            {
                item.TenantId,
                item.VariantId,
                MissingQuantity = item.RequiredQuantity -
                    activeByItem.GetValueOrDefault((item.TenantId, item.OrderItemId, item.VariantId))
            })
            .GroupBy(item => (item.TenantId, item.VariantId))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.MissingQuantity));

        if (missingByVariant.Values.Any(quantity => quantity < 0m))
        {
            return false;
        }

        foreach (var missing in missingByVariant.Where(item => item.Value > 0m))
        {
            var availableQuantity = await _dbContext.ProductVariantStocks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .WhereCurrentInventoryStockOwner(missing.Key.TenantId)
                .Where(stock => stock.VariantId == missing.Key.VariantId && !stock.IsDeleted)
                .SumAsync(stock => stock.Quantity - stock.ReservedQuantity - stock.DamagedQuantity, cancellationToken);
            if (availableQuantity < missing.Value)
            {
                return false;
            }
        }

        var shippingSelections = await _dbContext.CommerceHubSubOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(subOrder =>
                subOrder.MarketplaceOrderId == orderId &&
                !subOrder.IsDeleted &&
                subOrder.ShippingMethodId.HasValue)
            .Select(subOrder => new { subOrder.StoreId, ShippingMethodId = subOrder.ShippingMethodId!.Value })
            .ToListAsync(cancellationToken);

        foreach (var shipping in shippingSelections)
        {
            if (!await _dbContext.ShippingMethods
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(method =>
                        method.TenantId == shipping.StoreId &&
                        method.Id == shipping.ShippingMethodId &&
                        !method.IsDeleted &&
                        method.IsActive,
                        cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record MarketplaceReacquisitionItem(
        Guid TenantId,
        Guid SubOrderId,
        Guid OrderItemId,
        Guid ProductId,
        Guid VariantId,
        decimal RequiredQuantity);

    private static bool TryResolvePaymentProvider(string paymentMethodCode, out PaymentProvider provider)
    {
        switch (paymentMethodCode?.Trim().ToUpperInvariant())
        {
            case CommerceHubPaymentMethods.Paymob:
                provider = PaymentProvider.Paymob;
                return true;
            case CommerceHubPaymentMethods.Stripe:
                provider = PaymentProvider.Stripe;
                return true;
            default:
                provider = default;
                return false;
        }
    }
}
