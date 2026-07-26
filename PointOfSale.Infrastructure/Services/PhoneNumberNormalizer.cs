using Microsoft.Extensions.Logging;
using PhoneNumbers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Phone number normalization service using Google's libphonenumber.
/// 
/// PRODUCTION-READY IMPLEMENTATION:
/// 1. Uses libphonenumber-csharp for industry-standard parsing
/// 2. Resolves tenant default country from DB (with Redis caching)
/// 3. Converts local numbers to E.164 format
/// 4. Fails fast on invalid numbers with structured logging
/// 
/// NORMALIZATION PIPELINE (strict order):
/// 1. If number already starts with +, validate and return
/// 2. Resolve tenant default country code (cached in Redis)
/// 3. Parse using libphonenumber with tenant's country
/// 4. Validate and format to E.164
/// 5. Fail with specific error if any step fails
/// </summary>
public class PhoneNumberNormalizer : IPhoneNumberNormalizer
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<PhoneNumberNormalizer> _logger;
    private readonly PhoneNumberUtil _phoneUtil;

    // Redis cache settings
    private const string TENANT_COUNTRY_CACHE_PREFIX = "tenant:country:";
    private static readonly TimeSpan CACHE_TTL = TimeSpan.FromHours(24);

    public PhoneNumberNormalizer(
        ApplicationDbContext dbContext,
        IConnectionMultiplexer? redis,
        ILogger<PhoneNumberNormalizer> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
        _phoneUtil = PhoneNumberUtil.GetInstance();
    }

    /// <inheritdoc />
    public async Task<string> NormalizeAsync(string phoneNumber, Guid tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new PhoneNumberNormalizationException(
                "Phone number is required",
                phoneNumber,
                null,
                PhoneNormalizationFailureReason.NotANumber);
        }

        // Step 1: Clean the input
        var cleanedNumber = CleanPhoneNumber(phoneNumber);

        // Step 2: If already in E.164 format (starts with +), validate and return
        if (cleanedNumber.StartsWith('+'))
        {
            try
            {
                var parsed = _phoneUtil.Parse(cleanedNumber, null);
                if (_phoneUtil.IsValidNumber(parsed))
                {
                    var formatted = _phoneUtil.Format(parsed, PhoneNumberFormat.E164);
                    _logger.LogDebug(
                        "[PhoneNormalizer] Number already in E.164 | Input: {Input} | Output: {Output}",
                        MaskPhone(phoneNumber), MaskPhone(formatted));
                    return formatted;
                }
            }
            catch (NumberParseException)
            {
                // Continue to try with country code
            }
        }

        // Step 3: Resolve tenant's default country
        var countryCode = await GetTenantCountryAsync(tenantId, ct);
        
        if (string.IsNullOrEmpty(countryCode))
        {
            _logger.LogError(
                "[PhoneNormalizer] Missing country for tenant | TenantId: {TenantId} | Phone: {Phone}",
                tenantId, MaskPhone(phoneNumber));
            
            throw new PhoneNumberNormalizationException(
                "Tenant default country is not configured. Please set the country in tenant settings.",
                MaskPhone(phoneNumber),
                null,
                PhoneNormalizationFailureReason.MissingCountryCode);
        }

        // Step 4: Normalize using the tenant's country
        return Normalize(cleanedNumber, countryCode);
    }

    /// <inheritdoc />
    public string Normalize(string phoneNumber, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new PhoneNumberNormalizationException(
                "Phone number is required",
                phoneNumber,
                countryCode,
                PhoneNormalizationFailureReason.NotANumber);
        }

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            throw new PhoneNumberNormalizationException(
                "Country code is required for phone normalization",
                MaskPhone(phoneNumber),
                countryCode,
                PhoneNormalizationFailureReason.MissingCountryCode);
        }

        // Validate country code is ISO 3166-1 alpha-2
        var upperCountry = countryCode.ToUpperInvariant();
        if (upperCountry.Length != 2)
        {
            throw new PhoneNumberNormalizationException(
                $"Invalid country code format: {countryCode}. Expected ISO 3166-1 alpha-2 (e.g., 'EG', 'US', 'SA')",
                MaskPhone(phoneNumber),
                countryCode,
                PhoneNormalizationFailureReason.InvalidCountryCode);
        }

        var cleanedNumber = CleanPhoneNumber(phoneNumber);

        try
        {
            // Parse the number with the provided country code
            var parsed = _phoneUtil.Parse(cleanedNumber, upperCountry);

            // Validate the parsed number
            if (!_phoneUtil.IsValidNumber(parsed))
            {
                var reason = _phoneUtil.IsPossibleNumberWithReason(parsed);
                throw new PhoneNumberNormalizationException(
                    $"Invalid phone number for country {upperCountry}: {GetValidationReasonMessage(reason)}",
                    MaskPhone(phoneNumber),
                    upperCountry,
                    MapValidationReason(reason));
            }

            // Format to E.164
            var e164 = _phoneUtil.Format(parsed, PhoneNumberFormat.E164);

            _logger.LogDebug(
                "[PhoneNormalizer] Normalized | Input: {Input} | Country: {Country} | Output: {Output}",
                MaskPhone(phoneNumber), upperCountry, MaskPhone(e164));

            return e164;
        }
        catch (NumberParseException ex)
        {
            _logger.LogWarning(
                "[PhoneNormalizer] Parse failed | Phone: {Phone} | Country: {Country} | Error: {Error}",
                MaskPhone(phoneNumber), upperCountry, ex.Message);

            throw new PhoneNumberNormalizationException(
                $"Failed to parse phone number: {ex.Message}",
                MaskPhone(phoneNumber),
                upperCountry,
                PhoneNormalizationFailureReason.InvalidFormat);
        }
    }

    /// <inheritdoc />
    public bool TryNormalize(string phoneNumber, string countryCode, out string normalizedNumber)
    {
        normalizedNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        try
        {
            normalizedNumber = Normalize(phoneNumber, countryCode);
            return true;
        }
        catch (PhoneNumberNormalizationException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsValidNumber(string phoneNumber, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        try
        {
            var cleanedNumber = CleanPhoneNumber(phoneNumber);
            var parsed = _phoneUtil.Parse(cleanedNumber, countryCode.ToUpperInvariant());
            return _phoneUtil.IsValidNumber(parsed);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get tenant's default country code from DB (with Redis caching).
    /// </summary>
    private async Task<string?> GetTenantCountryAsync(Guid tenantId, CancellationToken ct)
    {
        var cacheKey = $"{TENANT_COUNTRY_CACHE_PREFIX}{tenantId}";

        // Try Redis cache first
        if (_redis != null)
        {
            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.StringGetAsync(cacheKey);
                if (!cached.IsNullOrEmpty)
                {
                    return cached.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PhoneNormalizer] Redis cache read failed, falling back to DB");
            }
        }

        // Query database
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Country })
            .FirstOrDefaultAsync(ct);

        var country = tenant?.Country;

        // Cache the result (even if null, to prevent repeated DB lookups)
        if (_redis != null && country != null)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync(cacheKey, country, CACHE_TTL);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PhoneNormalizer] Redis cache write failed");
            }
        }

        return country;
    }

    /// <summary>
    /// Clean phone number by removing common formatting characters.
    /// </summary>
    private static string CleanPhoneNumber(string phoneNumber)
    {
        // Keep + at the start if present, remove spaces, dashes, parentheses
        var cleaned = phoneNumber.Trim();
        
        // If starts with +, preserve it
        if (cleaned.StartsWith('+'))
        {
            return "+" + new string(cleaned.Skip(1).Where(c => char.IsDigit(c)).ToArray());
        }
        
        // Otherwise just keep digits
        return new string(cleaned.Where(c => char.IsDigit(c)).ToArray());
    }

    /// <summary>
    /// Mask phone number for logging (privacy).
    /// </summary>
    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
            return "****";

        // Show first 4 and last 3 characters
        var prefix = phone.Substring(0, 4);
        var suffix = phone.Substring(phone.Length - 3);
        var masked = new string('*', Math.Max(0, phone.Length - 7));
        return $"{prefix}{masked}{suffix}";
    }

    /// <summary>
    /// Get human-readable validation reason message.
    /// </summary>
    private static string GetValidationReasonMessage(PhoneNumbers.PhoneNumberUtil.ValidationResult reason)
    {
        return reason switch
        {
            PhoneNumberUtil.ValidationResult.IS_POSSIBLE => "Number is valid",
            PhoneNumberUtil.ValidationResult.INVALID_COUNTRY_CODE => "Invalid country code",
            PhoneNumberUtil.ValidationResult.TOO_SHORT => "Number is too short",
            PhoneNumberUtil.ValidationResult.TOO_LONG => "Number is too long",
            PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY => "Number is only valid locally",
            PhoneNumberUtil.ValidationResult.INVALID_LENGTH => "Invalid number length",
            _ => "Unknown validation error"
        };
    }

    /// <summary>
    /// Map libphonenumber validation result to our enum.
    /// </summary>
    private static PhoneNormalizationFailureReason MapValidationReason(PhoneNumbers.PhoneNumberUtil.ValidationResult reason)
    {
        return reason switch
        {
            PhoneNumberUtil.ValidationResult.INVALID_COUNTRY_CODE => PhoneNormalizationFailureReason.InvalidCountryCode,
            PhoneNumberUtil.ValidationResult.TOO_SHORT => PhoneNormalizationFailureReason.TooShort,
            PhoneNumberUtil.ValidationResult.TOO_LONG => PhoneNormalizationFailureReason.TooLong,
            PhoneNumberUtil.ValidationResult.INVALID_LENGTH => PhoneNormalizationFailureReason.InvalidFormat,
            _ => PhoneNormalizationFailureReason.Unknown
        };
    }
}
