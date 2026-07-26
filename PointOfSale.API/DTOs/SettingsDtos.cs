using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs;

public class SettingsDto
{
    // Company Information
    public string CompanyName { get; set; } = "My Shop";
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;
    public string CompanyEmail { get; set; } = string.Empty;
    public string? CompanyLogo { get; set; } // Base64 or URL

    // Tax & Currency
    /// <summary>
    /// ISO 4217 currency code. Tenant settings currently support `EGP` only.
    /// Must be exactly 3 uppercase letters.
    /// </summary>
    [Required(ErrorMessage = "Currency code is required")]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency code must be exactly 3 uppercase letters (ISO 4217 format)")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency code must be exactly 3 characters")]
    public string CurrencyCode { get; set; } = null!;
    
    /// <summary>
    /// Display symbol for the currency (e.g., "$", "€", "£").
    /// If not provided or invalid, this will be automatically derived from <see cref="CurrencyCode"/>.
    /// Custom symbols are allowed but <see cref="CurrencyCode"/> remains the authoritative currency identifier.
    /// </summary>
    public string CurrencySymbol { get; set; } = "E£";
    public bool EnableTax { get; set; } = true;
    public decimal TaxRate { get; set; } = 0;

    // Receipt Settings
    public string ReceiptHeader { get; set; } = "Welcome";
    public string ReceiptFooter { get; set; } = "Thank you for your visit";
    public bool ShowLogoOnReceipt { get; set; } = true;
}
