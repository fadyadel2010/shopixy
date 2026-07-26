namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Phone number normalization service.
/// Converts local phone numbers to E.164 international format.
/// 
/// ARCHITECTURAL RULES:
/// 1. Uses Google's libphonenumber for industry-standard parsing
/// 2. Never hardcodes country codes
/// 3. Tenant default country is the primary resolution source
/// 4. Fails fast on invalid numbers (no silent guessing)
/// 
/// Usage:
/// - WhatsApp message dispatch (primary use case)
/// - OTP verification
/// - Any future messaging channels (SMS, etc.)
/// </summary>
public interface IPhoneNumberNormalizer
{
    /// <summary>
    /// Normalize a phone number to E.164 format using the tenant's default country.
    /// </summary>
    /// <param name="phoneNumber">The phone number in local or international format</param>
    /// <param name="tenantId">The tenant ID to resolve default country</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>E.164 formatted phone number (e.g., +201022174803)</returns>
    /// <exception cref="PhoneNumberNormalizationException">Thrown if normalization fails</exception>
    Task<string> NormalizeAsync(string phoneNumber, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Normalize a phone number to E.164 format using an explicit country code.
    /// Use this when the country is known (e.g., from shipping address).
    /// </summary>
    /// <param name="phoneNumber">The phone number in local format</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country code (e.g., "EG", "SA", "US")</param>
    /// <returns>E.164 formatted phone number</returns>
    /// <exception cref="PhoneNumberNormalizationException">Thrown if normalization fails</exception>
    string Normalize(string phoneNumber, string countryCode);

    /// <summary>
    /// Try to normalize a phone number without throwing exceptions.
    /// </summary>
    /// <param name="phoneNumber">The phone number</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country code</param>
    /// <param name="normalizedNumber">The normalized E.164 number if successful</param>
    /// <returns>True if normalization succeeded, false otherwise</returns>
    bool TryNormalize(string phoneNumber, string countryCode, out string normalizedNumber);

    /// <summary>
    /// Validate that a phone number is valid for the given country.
    /// Does NOT normalize, only validates.
    /// </summary>
    /// <param name="phoneNumber">The phone number</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country code</param>
    /// <returns>True if the number is valid</returns>
    bool IsValidNumber(string phoneNumber, string countryCode);
}

/// <summary>
/// Exception thrown when phone number normalization fails.
/// </summary>
public class PhoneNumberNormalizationException : Exception
{
    public string? PhoneNumber { get; }
    public string? CountryCode { get; }
    public PhoneNormalizationFailureReason Reason { get; }

    public PhoneNumberNormalizationException(
        string message,
        string? phoneNumber = null,
        string? countryCode = null,
        PhoneNormalizationFailureReason reason = PhoneNormalizationFailureReason.Unknown)
        : base(message)
    {
        PhoneNumber = phoneNumber;
        CountryCode = countryCode;
        Reason = reason;
    }
}

/// <summary>
/// Reason for phone normalization failure.
/// </summary>
public enum PhoneNormalizationFailureReason
{
    Unknown,
    InvalidFormat,
    TooShort,
    TooLong,
    InvalidCountryCode,
    MissingCountryCode,
    NotANumber,
    InvalidForCountry
}
