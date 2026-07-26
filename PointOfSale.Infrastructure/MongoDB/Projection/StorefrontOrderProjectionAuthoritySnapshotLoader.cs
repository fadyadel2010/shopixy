using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.MongoDB.Projection;

public static class StorefrontOrderProjectionAuthoritySnapshotLoader
{
    public static async Task<StorefrontOrderProjectionAuthoritySnapshot> LoadAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var attributedAtUtc = await dbContext.AffiliateAttributions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(attribution =>
                attribution.TenantId == tenantId &&
                attribution.OrderId == orderId &&
                !attribution.IsDeleted)
            .Select(attribution => (DateTime?)attribution.AttributedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // Single aggregate query to compute both earned and reversed commissions in one round-trip
        var commissionSummary = await dbContext.AffiliateCommissionLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.StoreTenantId == tenantId &&
                entry.OrderId == orderId)
            .GroupBy(entry => 1) // Group all matching entries into a single result
            .Select(group => new
            {
                Earned = group
                    .Where(entry => entry.EntryType == CommissionEntryType.Earned)
                    .Sum(entry => entry.CommissionAmount),
                Reversed = group
                    .Where(entry => entry.EntryType == CommissionEntryType.Reversed)
                    .Sum(entry => entry.CommissionAmount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var earnedCommission = commissionSummary?.Earned ?? 0m;
        var reversedCommission = commissionSummary?.Reversed ?? 0m;
        var netAffiliateCommission = Math.Max(0m, earnedCommission - reversedCommission);
        var shipmentVisibility = await OrderFulfillmentLineageAuthority.LoadCurrentShipmentVisibilityAsync(
            dbContext,
            tenantId,
            orderId,
            cancellationToken);

        return new StorefrontOrderProjectionAuthoritySnapshot(
            AttributedAtUtc: attributedAtUtc,
            AffiliateCommissionAmount: netAffiliateCommission > 0m ? netAffiliateCommission : null,
            ShipmentShippingStatus: shipmentVisibility == null
                ? null
                : OrderFulfillmentLineageAuthority.ToProjectionShippingStatus(shipmentVisibility.ShipmentStatus),
            ShipmentTrackingNumber: shipmentVisibility?.TrackingNumber,
            ShipmentCarrierCode: shipmentVisibility?.CarrierCode,
            ShipmentDeliveredAtUtc: shipmentVisibility?.DeliveredAtUtc);
    }
}
