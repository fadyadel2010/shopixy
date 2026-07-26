using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Configuration for the optional storefront CAPTCHA guard.
/// Disabled by default; when enabled, a provider-specific validator must be wired in.
/// </summary>
public sealed class StorefrontCaptchaOptions
{
    public const string SectionName = "Storefront:Security:Captcha";

    public bool Enabled { get; set; }

    public string RequestHeaderName { get; set; } = "X-Storefront-Captcha-Token";
}

public sealed class StorefrontCaptchaValidationResult
{
    public bool IsValid { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static StorefrontCaptchaValidationResult Success() => new()
    {
        IsValid = true
    };

    public static StorefrontCaptchaValidationResult Failure(string errorCode, string errorMessage) => new()
    {
        IsValid = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Provider-specific CAPTCHA validator abstraction.
/// Implement this interface to integrate a CAPTCHA provider (e.g., reCAPTCHA, hCaptcha, Turnstile).
/// </summary>
public interface IStorefrontCaptchaValidator
{
    /// <summary>
    /// Validates a CAPTCHA token from the client.
    /// </summary>
    /// <param name="token">The CAPTCHA token to validate</param>
    /// <param name="httpContext">The current HTTP context for additional validation context (IP, user agent, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result indicating success or failure with error details</returns>
    Task<StorefrontCaptchaValidationResult> ValidateAsync(
        string token,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

public interface IStorefrontCaptchaGuard
{
    Task<StorefrontCaptchaValidationResult> ValidateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default CAPTCHA guard.
/// The guard is inert while disabled. When enabled, it delegates to an injected
/// validator implementation or fails closed if no validator is configured.
/// </summary>
public sealed class StorefrontCaptchaGuard : IStorefrontCaptchaGuard
{
    private readonly IOptions<StorefrontCaptchaOptions> _options;
    private readonly IStorefrontCaptchaValidator? _validator;
    private readonly ILogger<StorefrontCaptchaGuard> _logger;

    public StorefrontCaptchaGuard(
        IOptions<StorefrontCaptchaOptions> options,
        ILogger<StorefrontCaptchaGuard> logger,
        IStorefrontCaptchaValidator? validator = null)
    {
        _options = options;
        _logger = logger;
        _validator = validator;
    }

    public async Task<StorefrontCaptchaValidationResult> ValidateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var options = _options.Value;
        if (!options.Enabled)
        {
            return StorefrontCaptchaValidationResult.Success();
        }

        var headerName = string.IsNullOrWhiteSpace(options.RequestHeaderName)
            ? "X-Storefront-Captcha-Token"
            : options.RequestHeaderName.Trim();

        var token = httpContext.Request.Headers[headerName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            return StorefrontCaptchaValidationResult.Failure(
                "CAPTCHA_REQUIRED",
                "CAPTCHA validation is required.");
        }

        // Delegate to the injected validator if available
        if (_validator != null)
        {
            return await _validator.ValidateAsync(token, httpContext, cancellationToken);
        }

        // Fail closed if CAPTCHA is enabled but no validator is configured
        _logger.LogError(
            "Storefront CAPTCHA is enabled but no provider-specific validator is configured | HeaderName: {HeaderName}",
            headerName);

        return StorefrontCaptchaValidationResult.Failure(
            "CAPTCHA_PROVIDER_NOT_CONFIGURED",
            "CAPTCHA validation is enabled but no provider is configured.");
    }
}
