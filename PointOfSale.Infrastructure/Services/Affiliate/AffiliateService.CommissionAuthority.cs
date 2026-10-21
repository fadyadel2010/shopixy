using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Services.Affiliate;

public partial class AffiliateService
{
    private async Task<Order?> LoadCommissionOrderAuthorityAsync(Guid orderId, CancellationToken ct)
    {
        // Never reuse a tracked, stale payment/refund snapshot as financial authority.
        if (_context.Database.IsNpgsql())
            return await _context.Orders.FromSqlInterpolated($@"
                SELECT *, xmin FROM ""Orders"" WHERE ""Id"" = {orderId} AND NOT ""IsDeleted"" FOR UPDATE")
                .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);
        if ((_context.Database.ProviderName ?? string.Empty).Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return await _context.Orders.FromSqlInterpolated($@"
                SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {orderId} AND [IsDeleted] = 0")
                .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);
        return await _context.Orders.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == orderId && !order.IsDeleted, ct);
    }

    private async Task ValidateCommissionAttributionAsync(Order order, AffiliateAttribution attribution, CancellationToken ct)
    {
        if (order.AffiliateAccountId != attribution.AffiliateAccountId ||
            !string.Equals(order.AffiliateReferralCode?.Trim(), attribution.ReferralCode, StringComparison.OrdinalIgnoreCase) ||
            order.PromotionLinkId != attribution.PromotionLinkId)
            throw new InvalidOperationException("AFFILIATE_COMMISSION_ATTRIBUTION_CONFLICT: Attribution differs from canonical order snapshots.");

        if (!attribution.PromotionLinkId.HasValue) return; // Supported legacy admission.
        var link = await _context.AffiliatePromotionLinks.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(link => link.Id == attribution.PromotionLinkId.Value, ct);
        if (link == null || link.TenantId != order.TenantId || link.AffiliateAccountId != attribution.AffiliateAccountId ||
            !await _context.Products.IgnoreQueryFilters().AsNoTracking().AnyAsync(product =>
                product.Id == link.ProductId && product.TenantId == order.TenantId, ct) ||
            !await _context.AffiliateUsers.IgnoreQueryFilters().AsNoTracking().AnyAsync(user =>
                user.Id == link.AffiliateUserId && user.AffiliateAccountId == attribution.AffiliateAccountId, ct))
            throw new InvalidOperationException("AFFILIATE_COMMISSION_LINK_CONFLICT: Promotion lineage does not belong to this order and global affiliate.");
        // Checkout owns admission. Subsequent policy/suspension/marketing changes
        // do not retrospectively erase a valid, immutable commission candidate.
    }

    private static void ValidateCommissionSnapshot(OrderItem item)
    {
        var rate = item.AffiliateCommissionRateSnapshot!.Value;
        var cap = item.AffiliateMaxCommissionPerItemSnapshot;
        var amount = item.AffiliateCommissionBaseAmountSnapshot!.Value;
        var expected = Math.Round(cap.HasValue ? Math.Min(amount * rate, cap.Value * item.Quantity) : amount * rate,
            4, MidpointRounding.ToZero);
        if (item.Quantity <= 0 || rate < 0.0001m || rate > 0.50m || cap <= 0m ||
            amount != item.Total || expected != item.AffiliateCommissionAmountSnapshot)
            throw new InvalidOperationException("AFFILIATE_COMMISSION_SNAPSHOT_CONFLICT: Immutable commission terms do not reproduce the checkout formula.");
    }

    private async Task ValidateCommissionLineageAsync(Order order, OrderItem item, CancellationToken ct)
    {
        // Ownership only: never replace historical commercial values with current policy.
        if (!await _context.AffiliateProductPolicies.IgnoreQueryFilters().AsNoTracking().AnyAsync(policy =>
                policy.Id == item.AffiliateCommissionPolicyIdSnapshot && policy.ProductId == item.ProductId &&
                policy.TenantId == order.TenantId, ct) ||
            !await _context.Products.IgnoreQueryFilters().AsNoTracking().AnyAsync(product =>
                product.Id == item.ProductId && product.TenantId == order.TenantId, ct))
            throw new InvalidOperationException("AFFILIATE_COMMISSION_POLICY_LINEAGE_CONFLICT: Snapshot policy/product does not belong to the originating merchant.");
    }

    private async Task EnsureAffiliateDeliveryShipmentAuthorityAsync(Order order, CancellationToken ct)
    {
        if (order.FulfillmentMode == FulfillmentMode.Manual && order.ShippingMethodId.HasValue &&
            !await _context.Shipments.IgnoreQueryFilters().AsNoTracking().AnyAsync(s => s.TenantId == order.TenantId && s.OrderId == order.Id &&
                !s.IsDeleted && s.Status == ShipmentStatus.Delivered && s.CarrierCode == "manual", ct))
            throw new InvalidOperationException("AFFILIATE_MANUAL_DELIVERY_EVIDENCE_REQUIRED: The original Manual shipment must be delivered.");
    }
}
