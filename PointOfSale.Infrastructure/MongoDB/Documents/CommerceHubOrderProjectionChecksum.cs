using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

public static class CommerceHubOrderProjectionChecksum
{
    public static string Compute(CommerceHubOrderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        Append(builder, document.MarketplaceOrderId);
        Append(builder, document.OrderNumber);
        Append(builder, document.FullName);
        Append(builder, document.PhoneNumber);
        Append(builder, document.AddressLine);
        Append(builder, document.City);
        Append(builder, document.Area);
        Append(builder, document.Notes);
        Append(builder, document.Currency);
        Append(builder, document.PaymentMethodCode);
        Append(builder, document.PaymentMethodName);
        Append(builder, document.PaymentReference);
        Append(builder, document.PaymentReferenceId);
        Append(builder, document.PaymentGateway);
        Append(builder, document.Subtotal);
        Append(builder, document.ShippingTotal);
        Append(builder, document.TotalAmount);
        Append(builder, document.Status);
        Append(builder, document.PaymentStatus);
        Append(builder, document.CreatedAt);
        Append(builder, document.UpdatedAt);
        Append(builder, document.PaidAt);
        Append(builder, document.CustomerId);
        Append(builder, document.OnlineCustomerId);

        foreach (var subOrder in document.SubOrders.OrderBy(subOrder => subOrder.SubOrderId))
        {
            Append(builder, subOrder.SubOrderId);
            Append(builder, subOrder.OrderNumber);
            Append(builder, subOrder.StoreId);
            Append(builder, subOrder.StoreName);
            Append(builder, subOrder.Currency);
            Append(builder, subOrder.Subtotal);
            Append(builder, subOrder.Shipping);
            Append(builder, subOrder.ShippingMethodId);
            Append(builder, subOrder.ShippingMethodName);
            Append(builder, subOrder.ShippingSnapshotJson);
            Append(builder, subOrder.PaymentMethod);
            Append(builder, subOrder.PaymentMethodCode);
            Append(builder, subOrder.PaymentMethodName);
            Append(builder, subOrder.PaymentSnapshotJson);
            Append(builder, subOrder.Total);
            Append(builder, subOrder.Status);
            Append(builder, subOrder.PaymentStatus);
            Append(builder, subOrder.PaymentReferenceId);
            Append(builder, subOrder.PaidAt);

            foreach (var item in subOrder.Items.OrderBy(item => item.ListingId).ThenBy(item => item.VariantId))
            {
                Append(builder, item.ListingId);
                Append(builder, item.ProductId);
                Append(builder, item.VariantId);
                Append(builder, item.ProductName);
                Append(builder, item.VariantName);
                Append(builder, item.VariantAttributesJson);
                Append(builder, item.ImageUrl);
                Append(builder, item.Quantity);
                Append(builder, item.UnitPrice);
                Append(builder, item.Total);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, object? value)
    {
        var normalized = value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            decimal amount => amount.ToString("0.00####", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        builder.Append(normalized.Length);
        builder.Append(':');
        builder.Append(normalized);
        builder.Append('|');
    }
}
