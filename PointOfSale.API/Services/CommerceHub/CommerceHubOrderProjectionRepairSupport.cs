using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.CommerceHub;

internal static class CommerceHubOrderProjectionRepairSupport
{
    public static CommerceHubOrderDocument BuildProjectionDocument(CommerceHubMarketplaceOrder order)
    {
        var sourceMutationVersion = ResolveSourceMutationVersion(order);
        var projectionTimestampUtc = order.UpdatedAt ?? order.CreatedAt;

        var document = new CommerceHubOrderDocument
        {
            MarketplaceOrderId = order.Id,
            OrderNumber = order.OrderNumber,
            FullName = order.FullName,
            PhoneNumber = order.PhoneNumber,
            AddressLine = order.AddressLine,
            City = order.City,
            Area = order.Area,
            Notes = order.Notes,
            Currency = order.Currency,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            PaymentReference = order.PaymentReference,
            PaymentReferenceId = order.PaymentReferenceId,
            PaymentGateway = order.PaymentGateway,
            Subtotal = order.Subtotal,
            ShippingTotal = order.ShippingTotal,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt ?? order.CreatedAt,
            PaidAt = order.PaidAt,
            LastEventId = null,
            LastEventOccurredAtUtc = projectionTimestampUtc,
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(sourceMutationVersion),
            LastSourceMutationTicks = sourceMutationVersion,
            CustomerId = order.CustomerId,
            OnlineCustomerId = order.OnlineCustomerId,
            SubOrders = order.SubOrders.Select(subOrder => new CommerceHubSubOrderDocument
            {
                SubOrderId = subOrder.Id,
                OrderNumber = subOrder.OrderNumber,
                StoreId = subOrder.StoreId,
                StoreName = subOrder.StoreName,
                Currency = subOrder.Currency,
                Subtotal = subOrder.Subtotal,
                Shipping = subOrder.Shipping,
                ShippingMethodId = subOrder.ShippingMethodId,
                ShippingMethodName = subOrder.ShippingMethodName,
                ShippingSnapshotJson = subOrder.ShippingSnapshotJson,
                PaymentMethod = subOrder.PaymentMethod,
                PaymentMethodCode = subOrder.PaymentMethodCode,
                PaymentMethodName = subOrder.PaymentMethodName,
                PaymentSnapshotJson = subOrder.PaymentSnapshotJson,
                Total = subOrder.Total,
                Status = subOrder.Status,
                PaymentStatus = subOrder.PaymentStatus,
                PaymentReferenceId = subOrder.PaymentReferenceId,
                PaidAt = subOrder.PaidAt,
                Items = subOrder.Items.Select(item => new CommerceHubOrderItemDocument
                {
                    ListingId = item.ListingId,
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    ProductName = item.ProductName,
                    VariantName = item.VariantName,
                    VariantAttributesJson = item.VariantAttributesJson,
                    ImageUrl = item.ImageUrl,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Total = item.Total
                }).ToList()
            }).ToList()
        };

        document.ProjectionChecksum = CommerceHubOrderProjectionChecksum.Compute(document);
        return document;
    }

    public static FilterDefinition<CommerceHubOrderDocument> BuildProjectionRecoveryFilter(
        Guid marketplaceOrderId,
        Guid customerId,
        long sourceMutationVersion,
        string projectionChecksum)
    {
        var filter = Builders<CommerceHubOrderDocument>.Filter;

        // Divergent projection: checksum/customer mismatch regardless of tick count
        // This allows repair to replace projections with wrong customer or checksum
        // even if they claim a higher LastSourceMutationTicks
        var divergentProjection = filter.Or(
            filter.Eq(doc => doc.CustomerId, Guid.Empty),
            filter.Ne(doc => doc.CustomerId, customerId),
            filter.Exists(nameof(CommerceHubOrderDocument.ProjectionChecksum), false),
            filter.Ne(doc => doc.ProjectionChecksum, projectionChecksum));

        return filter.And(
            filter.Eq(doc => doc.MarketplaceOrderId, marketplaceOrderId),
            filter.Or(
                filter.Exists(nameof(CommerceHubOrderDocument.LastEventOccurredAtUtc), false),
                filter.Eq(doc => doc.LastEventOccurredAtUtc, default),
                filter.Eq(doc => doc.LastEventVersion, 0),
                filter.Lt(doc => doc.LastSourceMutationTicks, sourceMutationVersion),
                divergentProjection));
    }

    public static bool ProjectionProvesSqlAuthority(
        CommerceHubProjectionRecoveryState? existing,
        Guid customerId,
        long sourceMutationVersion,
        string projectionChecksum) =>
        existing != null &&
        existing.CustomerId == customerId &&
        existing.CustomerId != Guid.Empty &&
        existing.LastEventOccurredAtUtc != default &&
        existing.LastEventVersion > 0 &&
        existing.LastSourceMutationTicks >= sourceMutationVersion &&
        existing.ProjectionChecksum == projectionChecksum;

    public static long ResolveSourceMutationVersion(CommerceHubMarketplaceOrder order)
    {
        var sourceVersion = order.ProjectionMutationVersion;

        foreach (var subOrder in order.SubOrders)
        {
            sourceVersion = Math.Max(sourceVersion, subOrder.ProjectionMutationVersion);
        }

        return sourceVersion <= 0 ? 1L : sourceVersion;
    }

    public sealed class CommerceHubProjectionRecoveryState
    {
        public Guid CustomerId { get; init; }
        public DateTime LastEventOccurredAtUtc { get; init; }
        public int LastEventVersion { get; init; }
        public long LastSourceMutationTicks { get; init; }
        public string ProjectionChecksum { get; init; } = string.Empty;
    }
}
