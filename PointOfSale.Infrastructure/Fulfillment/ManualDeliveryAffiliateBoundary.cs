using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Fulfillment;

/// <summary>SQL attribution, never a client flag, determines whether approval authority is needed.</summary>
internal static class ManualDeliveryAffiliateBoundary
{
    internal static async Task<bool> HasObligationAsync(ApplicationDbContext db, Order order, CancellationToken ct) =>
        order.AffiliateAccountId.HasValue || order.PromotionLinkId.HasValue ||
        await db.AffiliateAttributions.IgnoreQueryFilters().AnyAsync(a => a.OrderId == order.Id, ct) ||
        await db.AffiliateCommissionLedger.IgnoreQueryFilters().AnyAsync(a => a.OrderId == order.Id, ct);
}
