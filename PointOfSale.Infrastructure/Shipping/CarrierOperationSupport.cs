using PointOfSale.Core.Constants;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>Installed code surfaces only; never account entitlement or permission to execute.</summary>
public sealed record CarrierOperationSupport(string Operation, string Implementation, IReadOnlyList<string> Limitations);

public static class CarrierOperationSupports
{
    public static IReadOnlyList<CarrierOperationSupport> Describe(string carrier)
    {
        carrier = CarrierCodes.Normalize(carrier);
        CarrierOperationSupport Op(string operation, string implementation, params string[] limitations) => new(operation, implementation, limitations);
        if (!CarrierCodes.AllCodes.Contains(carrier)) return new[] { "quote", "create", "tracking", "label", "cancellation", "recovery" }
            .Select(operation => Op(operation, "Unavailable", "CARRIER_NOT_SUPPORTED")).ToArray();
        if (CarrierCodes.IsManual(carrier)) return [
            Op("quote", "LocalOnly", "MERCHANT_METHOD_PRICE"), Op("create", "LocalOnly"), Op("tracking", "LocalOnly"),
            Op("label", "StoredOnly"), Op("cancellation", "LocalOnly"), Op("recovery", "LocalOnly")];
        if (carrier == CarrierCodes.Mylerz) return [
            Op("quote", "Unavailable", "MYLERZ_CONTRACT_UNPROVEN"), Op("create", "Unavailable", "MYLERZ_CONTRACT_UNPROVEN"),
            Op("tracking", "Unavailable", "MYLERZ_CONTRACT_UNPROVEN"), Op("label", "StoredOnly"),
            Op("cancellation", "Unavailable", "MYLERZ_CONTRACT_UNPROVEN"), Op("recovery", "ReconciliationOnly", "MYLERZ_ORIGINAL_AUTHORITY_REQUIRED")];
        return [
            carrier switch
            {
                CarrierCodes.Bosta => Op("quote", "Unavailable", "BOSTA_REMOTE_QUOTES_UNSUPPORTED"),
                CarrierCodes.Aramex => Op("quote", "Implemented", "PREPAID_SINGLE_MEASURED_PARCEL_ONLY"),
                CarrierCodes.Dhl => Op("quote", "Implemented", "PREPAID_SINGLE_MEASURED_PARCEL_ONLY"),
                _ => Op("quote", "Unavailable", "FEDEX_NOT_YET_CERTIFIED")
            },
            Op("create", "Implemented", "IMMUTABLE_ACCOUNT_REQUEST_AND_MEASURED_PARCEL_REQUIRED", "ROUTE_SERVICE_ENTITLEMENT_REQUIRED"),
            Op("tracking", "Implemented", "ORIGINAL_SHIPMENT_ACCOUNT_IDENTITY_REQUIRED"),
            Op("label", "StoredOnly", "NO_REMOTE_LABEL_PURCHASE"),
            carrier == CarrierCodes.Bosta ? Op("cancellation", "Implemented", "ORIGINAL_SHIPMENT_ACCOUNT_IDENTITY_REQUIRED", "PRE_PICKUP_ONLY", "EXACT_DELIVERY_CANCELLED_READ_REQUIRED", "ACCOUNT_FEES_NOT_CERTIFIED") :
                Op("cancellation", "Unavailable", "REMOTE_SHIPMENT_CANCELLATION_UNSUPPORTED"),
            Op("recovery", "ReconciliationOnly", "AUTOMATIC_REPOST_UNSUPPORTED", "ORIGINAL_SHIPMENT_ACCOUNT_IDENTITY_REQUIRED")
        ];
    }
}
