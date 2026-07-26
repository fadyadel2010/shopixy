using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-authoritative order payment visibility used when replayed payment facet
/// evidence tries to mutate derivative order projections.
/// </summary>
public static class OrderPaymentLineageAuthority
{
    public static async Task<OrderPaymentVisibilityAuthoritySnapshot?> LoadCurrentVisibilityAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId &&
                order.Id == orderId &&
                !order.IsDeleted)
            .Select(order => new OrderPaymentVisibilityAuthoritySnapshot(
                order.TenantId,
                order.Id,
                order.PaymentStatus,
                order.OrderNumber))
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot == null)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "order_payment_visibility",
                OperationalConvergenceAuthority.OrderPayment,
                OperationalConvergenceReason.MissingSqlAuthority,
                tenantId);
        }

        return snapshot;
    }
}

public sealed record OrderPaymentVisibilityAuthoritySnapshot(
    Guid TenantId,
    Guid OrderId,
    PaymentStatus PaymentStatus,
    string OrderNumber);
