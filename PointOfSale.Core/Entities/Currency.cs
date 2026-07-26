namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents a valid ISO 4217 currency code.
/// This is a reference table to enforce currency code validity across the system.
/// </summary>
public class Currency
{
    /// <summary>
    /// ISO 4217 currency code (e.g., "USD", "EUR", "GBP")
    /// This is the primary key.
    /// </summary>
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Full name of the currency (e.g., "US Dollar", "Euro", "British Pound")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Display symbol for the currency (e.g., "$", "€", "£")
    /// </summary>
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of decimal places typically used for this currency
    /// </summary>
    public int DecimalPlaces { get; set; } = 2;
}
