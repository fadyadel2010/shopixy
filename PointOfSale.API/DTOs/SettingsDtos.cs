using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs;

public class SettingsDto
{
    // Company Information
    [Required(ErrorMessage = "Company name is required")]
    [StringLength(100, MinimumLength = 2)]
    [NoControlCharacters]
    public string CompanyName { get; set; } = "My Shop";

    [StringLength(500)]
    [NoControlCharacters(AllowNewLines = true)]
    public string? CompanyAddress { get; set; } = string.Empty;

    [StringLength(50)]
    [NoControlCharacters]
    public string? CompanyPhone { get; set; } = string.Empty;

    [StringLength(254)]
    [OptionalEmailAddress]
    [NoControlCharacters]
    public string? CompanyEmail { get; set; } = string.Empty;

    public string? CompanyLogo { get; set; } // Base64 or URL

    // Tax & Currency
    /// <summary>
    /// ISO 4217 currency code. Tenant settings currently support `EGP` only.
    /// Must be exactly 3 uppercase letters.
    /// </summary>
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency code must be exactly 3 uppercase letters (ISO 4217 format)")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency code must be exactly 3 characters")]
    public string? CurrencyCode { get; set; }
    
    /// <summary>
    /// Display symbol for the currency (e.g., "$", "€", "£").
    /// If not provided or invalid, this will be automatically derived from <see cref="CurrencyCode"/>.
    /// Custom symbols are allowed but <see cref="CurrencyCode"/> remains the authoritative currency identifier.
    /// </summary>
    [StringLength(10)]
    [NoControlCharacters]
    public string CurrencySymbol { get; set; } = "E£";

    /// <summary>
    /// Backend-resolved storefront origin. This is read-only operational data;
    /// values submitted by clients are ignored.
    /// </summary>
    public string? StorefrontUrl { get; set; }

    public bool EnableTax { get; set; } = true;

    [Range(typeof(decimal), "0", "100")]
    public decimal TaxRate { get; set; } = 0;

    // Receipt Settings
    [StringLength(500)]
    [NoControlCharacters(AllowNewLines = true)]
    public string? ReceiptHeader { get; set; } = "Welcome";

    [StringLength(500)]
    [NoControlCharacters(AllowNewLines = true)]
    public string? ReceiptFooter { get; set; } = "Thank you for your visit";

    public bool ShowLogoOnReceipt { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NoControlCharactersAttribute : ValidationAttribute
{
    public bool AllowNewLines { get; set; }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text)
        {
            return ValidationResult.Success;
        }

        var containsForbiddenControl = text.Any(character =>
            char.IsControl(character) &&
            !(AllowNewLines && character is '\r' or '\n'));

        return containsForbiddenControl
            ? new ValidationResult($"{validationContext.DisplayName} contains unsupported control characters")
            : ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OptionalEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute EmailAddress = new();

    public override bool IsValid(object? value) =>
        value is null ||
        value is string text && (string.IsNullOrWhiteSpace(text) || EmailAddress.IsValid(text));
}
