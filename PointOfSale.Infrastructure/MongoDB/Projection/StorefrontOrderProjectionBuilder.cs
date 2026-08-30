using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Projection;

public static class StorefrontOrderProjectionBuilder
{
    public static OrderDocument Build(
        Order order,
        Guid triggeringEventId,
        DateTime sourceTimestamp,
        long aggregateVersion,
        IReadOnlyCollection<OrderTimelineItem>? existingTimeline = null,
        StorefrontOrderProjectionAuthoritySnapshot? authoritySnapshot = null)
    {
        sourceTimestamp = NormalizeForMongoDateTime(sourceTimestamp);

        var customerEmail = order.GuestEmail ?? order.OnlineCustomer?.Email ?? order.Customer?.Email;
        var customerName = order.GuestName ?? order.OnlineCustomer?.DisplayName ?? order.Customer?.Name;
        var shippingStatus = ResolveShippingStatus(order, authoritySnapshot);

        var timeline = existingTimeline?.ToList() ?? [];
        timeline.Add(new OrderTimelineItem
        {
            At = sourceTimestamp,
            Type = "ProjectionRehydrated",
            From = string.Empty,
            To = order.Status.ToString(),
            Description = "Order state synchronized from source of truth"
        });

        var clampedAggregateVersion = aggregateVersion > int.MaxValue ? int.MaxValue : (int)aggregateVersion;

        return new OrderDocument
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            OnlineCustomerId = order.OnlineCustomerId,
            SessionId = order.SessionId,
            CustomerEmail = customerEmail,
            CustomerName = customerName,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            ShippingStatus = shippingStatus,
            CancellationReason = order.CancellationReason,
            Subtotal = order.Subtotal,
            PromotionDiscountAmount = order.PromotionDiscountAmount,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            ShippingCost = order.ShippingCost,
            Total = order.Total,
            Currency = order.Currency,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            ShippingMethodName = order.ShippingMethodName,
            CouponCode = order.CouponCode,
            ShippingAddress = new OrderAddressDocument
            {
                Street = order.ShippingAddress,
                City = order.ShippingCity,
                State = order.ShippingState,
                ZipCode = order.ShippingZipCode,
                Country = order.ShippingCountry,
                Phone = order.GuestPhone
            },
            TrackingNumber = authoritySnapshot?.ShipmentTrackingNumber ?? order.TrackingNumber,
            CarrierName = authoritySnapshot?.ShipmentCarrierCode ?? order.Carrier,
            DeliveredAt = NormalizeForMongoDateTime(authoritySnapshot?.ShipmentDeliveredAtUtc ?? order.DeliveredAt),
            FulfillmentMode = order.FulfillmentMode.ToString(),
            FulfillmentCarrierCode = order.FulfillmentCarrierCode,
            Items = order.Items
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.VariantId)
                .ThenBy(item => item.SKU)
                .Select(item => new OrderItemDocument
                {
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    SKU = item.SKU,
                    ProductName = item.ProductName,
                    VariantName = item.VariantName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Total = item.Total,
                    ImageUrl = item.ImageUrl
                })
                .ToList(),
            Timeline = timeline,
            CreatedAt = NormalizeForMongoDateTime(order.CreatedAt),
            UpdatedAt = NormalizeForMongoDateTime(order.UpdatedAt ?? sourceTimestamp),
            LastEventId = triggeringEventId,
            LastEventOccurredAtUtc = sourceTimestamp,
            LastEventVersion = clampedAggregateVersion,
            LastStatusEventId = triggeringEventId,
            LastStatusEventOccurredAtUtc = sourceTimestamp,
            LastStatusEventVersion = clampedAggregateVersion,
            LastPaymentEventId = triggeringEventId,
            LastPaymentEventOccurredAtUtc = sourceTimestamp,
            LastPaymentEventVersion = clampedAggregateVersion,
            LastShipmentEventId = triggeringEventId,
            LastShipmentEventOccurredAtUtc = sourceTimestamp,
            LastShipmentEventVersion = clampedAggregateVersion,
            IsAffiliateOrder = order.AffiliateAccountId.HasValue,
            AffiliateId = order.AffiliateAccountId,
            AffiliateReferralCode = order.AffiliateReferralCode,
            AttributedAtUtc = NormalizeForMongoDateTime(authoritySnapshot?.AttributedAtUtc ??
                (order.AffiliateAccountId.HasValue ? order.CreatedAt : null)),
            AffiliateCommissionAmount = authoritySnapshot?.AffiliateCommissionAmount,
            LastAffiliateAttributionEventId = order.AffiliateAccountId.HasValue ? triggeringEventId : null,
            LastAffiliateAttributionEventOccurredAtUtc = order.AffiliateAccountId.HasValue ? sourceTimestamp : null,
            LastAffiliateAttributionEventVersion = order.AffiliateAccountId.HasValue
                ? clampedAggregateVersion
                : 0,
            LastAffiliateCommissionEventId = authoritySnapshot?.AffiliateCommissionAmount.HasValue == true
                ? triggeringEventId
                : null,
            LastAffiliateCommissionEventOccurredAtUtc = authoritySnapshot?.AffiliateCommissionAmount.HasValue == true
                ? sourceTimestamp
                : null,
            LastAffiliateCommissionEventVersion = authoritySnapshot?.AffiliateCommissionAmount.HasValue == true
                ? clampedAggregateVersion
                : 0
        };
    }

    public static DateTime ResolveSourceTimestamp(Order order, DateTime triggeringEventOccurredAtUtc)
    {
        var sourceTimestamp = new[]
            {
                order.CreatedAt,
                order.UpdatedAt ?? order.CreatedAt,
                order.PaidAt ?? DateTime.MinValue,
                order.ShippedAt ?? DateTime.MinValue,
                order.DeliveredAt ?? DateTime.MinValue,
                order.CancelledAt ?? DateTime.MinValue,
                triggeringEventOccurredAtUtc
            }
            .Where(value => value != DateTime.MinValue)
            .Max();

        return NormalizeForMongoDateTime(sourceTimestamp);
    }

    private static string ResolveShippingStatus(
        Order order,
        StorefrontOrderProjectionAuthoritySnapshot? authoritySnapshot)
    {
        var terminalStatus = order.FulfillmentStatus switch
        {
            FulfillmentStatus.Returned => "Returned",
            FulfillmentStatus.FailedDelivery => "Failed",
            FulfillmentStatus.Cancelled => "Cancelled",
            FulfillmentStatus.Delivered or FulfillmentStatus.Completed => "Delivered",
            _ => null
        };

        if (terminalStatus != null)
        {
            return terminalStatus;
        }

        return authoritySnapshot?.ShipmentShippingStatus ?? order.FulfillmentStatus switch
        {
            FulfillmentStatus.Confirmed or FulfillmentStatus.Packed => "Processing",
            FulfillmentStatus.OutForDelivery => "OutForDelivery",
            _ when order.ShippedAt.HasValue => "Shipped",
            _ => "Pending"
        };
    }

    public static DateTime NormalizeForMongoDateTime(DateTime value)
    {
        if (value == DateTime.MinValue || value == DateTime.MaxValue)
        {
            return value;
        }

        var utcValue = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };

        return new DateTime(
            utcValue.Ticks - utcValue.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
    }

    public static DateTime? NormalizeForMongoDateTime(DateTime? value)
    {
        return value.HasValue
            ? NormalizeForMongoDateTime(value.Value)
            : null;
    }

    public static string ComputeAuthorityFingerprint(OrderDocument document)
    {
        var payload = new
        {
            document.TenantId,
            document.OrderId,
            document.OrderNumber,
            document.CustomerId,
            document.OnlineCustomerId,
            document.SessionId,
            document.CustomerEmail,
            document.CustomerName,
            document.Status,
            document.PaymentStatus,
            document.ShippingStatus,
            document.CancellationReason,
            document.Subtotal,
            document.PromotionDiscountAmount,
            document.DiscountAmount,
            document.TaxAmount,
            document.ShippingCost,
            document.Total,
            document.Currency,
            document.PaymentMethodCode,
            document.PaymentMethodName,
            document.ShippingMethodName,
            document.CouponCode,
            ShippingAddress = document.ShippingAddress == null
                ? null
                : new
                {
                    document.ShippingAddress.Street,
                    document.ShippingAddress.City,
                    document.ShippingAddress.State,
                    document.ShippingAddress.ZipCode,
                    document.ShippingAddress.Country,
                    document.ShippingAddress.Phone
                },
            document.TrackingNumber,
            document.CarrierName,
            DeliveredAt = NormalizeForMongoDateTime(document.DeliveredAt),
            document.FulfillmentMode,
            document.FulfillmentCarrierCode,
            document.IsAffiliateOrder,
            document.AffiliateId,
            document.AffiliateReferralCode,
            document.AffiliateCommissionAmount,
            AttributedAtUtc = NormalizeForMongoDateTime(document.AttributedAtUtc),
            Items = document.Items
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.VariantId)
                .ThenBy(item => item.SKU)
                .Select(item => new
                {
                    item.ProductId,
                    item.VariantId,
                    item.SKU,
                    item.ProductName,
                    item.VariantName,
                    item.Quantity,
                    item.UnitPrice,
                    item.Total,
                    item.ImageUrl
                })
                .ToList()
        };

        return JsonSerializer.Serialize(payload);
    }
}
