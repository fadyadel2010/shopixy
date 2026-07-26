namespace PointOfSale.Core.Constants;

/// <summary>
/// Canonical carrier code constants.
/// 
/// ARCHITECTURAL RULE (NON-NEGOTIABLE):
/// - All carrier codes MUST be defined here as constants
/// - NO magic strings allowed in code
/// - "manual" is a first-class carrier, NOT a fallback
/// 
/// Adding a new carrier:
/// 1. Add constant here
/// 2. Create adapter in Infrastructure/Shipping/Adapters/
/// 3. Register adapter in DI
/// </summary>
public static class CarrierCodes
{
    /// <summary>
    /// Manual fulfillment - no carrier API integration.
    /// Used when merchant handles shipping themselves.
    /// This is a REAL carrier, not a fallback.
    /// </summary>
    public const string Manual = "manual";
    
    /// <summary>
    /// Bosta - Egyptian last-mile delivery carrier.
    /// </summary>
    public const string Bosta = "bosta";
    
    /// <summary>
    /// Aramex - MENA region logistics carrier.
    /// </summary>
    public const string Aramex = "aramex";
    
    /// <summary>
    /// Mylerz - Egyptian delivery carrier.
    /// </summary>
    public const string Mylerz = "mylerz";
    
    /// <summary>
    /// DHL Express - International express carrier.
    /// </summary>
    public const string Dhl = "dhl";

    /// <summary>
    /// FedEx - global logistics carrier.
    /// Runtime foundation only; operational behavior is not certified.
    /// </summary>
    public const string FedEx = "fedex";
    
    /// <summary>
    /// All valid carrier codes (for validation).
    /// </summary>
    public static readonly string[] AllCodes = { Manual, Bosta, Aramex, Mylerz, Dhl, FedEx };
    
    /// <summary>
    /// Checks if a carrier code is the manual fulfillment carrier.
    /// </summary>
    public static bool IsManual(string? carrierCode)
    {
        return string.IsNullOrWhiteSpace(carrierCode) || 
               string.Equals(carrierCode, Manual, StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Normalizes a carrier code (null/empty becomes "manual").
    /// </summary>
    public static string Normalize(string? carrierCode)
    {
        return string.IsNullOrWhiteSpace(carrierCode) ? Manual : carrierCode.ToLowerInvariant();
    }
}
