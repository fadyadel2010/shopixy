namespace PointOfSale.Core.Enums;

/// <summary>
/// Classification of marketing review evidence.
/// Determined exclusively by the server — never by the client.
/// </summary>
public enum EvidenceType
{
    /// <summary>
    /// Campaign data was provided by the buyer without POS verification.
    /// OrdersCount and RevenueAmount will be null.
    /// </summary>
    SelfReported = 0,

    /// <summary>
    /// Campaign results were verified against POS order data.
    /// OrdersCount and RevenueAmount are computed server-side.
    /// </summary>
    PosVerified = 1
}
