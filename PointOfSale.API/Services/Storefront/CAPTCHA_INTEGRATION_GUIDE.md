# Storefront CAPTCHA Integration Guide

## Overview

The Storefront CAPTCHA guard provides an extensible framework for protecting sensitive endpoints (like password recovery) with CAPTCHA validation. The guard is disabled by default and requires a provider-specific validator implementation to function when enabled.

## Architecture

- **`IStorefrontCaptchaGuard`**: Main guard interface used by endpoints
- **`IStorefrontCaptchaValidator`**: Provider-specific validator abstraction (implement this)
- **`StorefrontCaptchaOptions`**: Configuration for enabling/disabling and header name
- **`StorefrontCaptchaValidationResult`**: Validation result with success/failure details

## Configuration

Add to `appsettings.json`:

```json
{
  "Storefront": {
    "Security": {
      "Captcha": {
        "Enabled": true,
        "RequestHeaderName": "X-Storefront-Captcha-Token"
      }
    }
  }
}
```

## Implementing a CAPTCHA Provider

### 1. Create a Validator Implementation

Example for Google reCAPTCHA v3:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PointOfSale.API.Services.Storefront.Captcha;

public class RecaptchaV3Options
{
    public const string SectionName = "Storefront:Security:Captcha:RecaptchaV3";
    
    public string SecretKey { get; set; } = string.Empty;
    public double MinimumScore { get; set; } = 0.5;
    public string VerifyUrl { get; set; } = "https://www.google.com/recaptcha/api/siteverify";
}

public class RecaptchaV3Validator : IStorefrontCaptchaValidator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<RecaptchaV3Options> _options;
    private readonly ILogger<RecaptchaV3Validator> _logger;

    public RecaptchaV3Validator(
        IHttpClientFactory httpClientFactory,
        IOptions<RecaptchaV3Options> options,
        ILogger<RecaptchaV3Validator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<StorefrontCaptchaValidationResult> ValidateAsync(
        string token,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            _logger.LogError("reCAPTCHA secret key is not configured");
            return StorefrontCaptchaValidationResult.Failure(
                "CAPTCHA_MISCONFIGURED",
                "CAPTCHA validation is not properly configured.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
            
            var response = await client.PostAsync(
                $"{options.VerifyUrl}?secret={options.SecretKey}&response={token}&remoteip={remoteIp}",
                null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("reCAPTCHA verification request failed | StatusCode: {StatusCode}", 
                    response.StatusCode);
                return StorefrontCaptchaValidationResult.Failure(
                    "CAPTCHA_VERIFICATION_FAILED",
                    "Failed to verify CAPTCHA.");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RecaptchaResponse>(content);

            if (result == null)
            {
                _logger.LogError("Failed to parse reCAPTCHA response");
                return StorefrontCaptchaValidationResult.Failure(
                    "CAPTCHA_VERIFICATION_FAILED",
                    "Failed to verify CAPTCHA.");
            }

            if (!result.Success)
            {
                _logger.LogWarning("reCAPTCHA validation failed | Errors: {Errors}", 
                    string.Join(", ", result.ErrorCodes ?? Array.Empty<string>()));
                return StorefrontCaptchaValidationResult.Failure(
                    "CAPTCHA_INVALID",
                    "CAPTCHA validation failed.");
            }

            if (result.Score < options.MinimumScore)
            {
                _logger.LogWarning("reCAPTCHA score too low | Score: {Score} | Minimum: {Minimum}", 
                    result.Score, options.MinimumScore);
                return StorefrontCaptchaValidationResult.Failure(
                    "CAPTCHA_SCORE_TOO_LOW",
                    "CAPTCHA validation failed.");
            }

            return StorefrontCaptchaValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "reCAPTCHA validation exception");
            return StorefrontCaptchaValidationResult.Failure(
                "CAPTCHA_VERIFICATION_ERROR",
                "An error occurred during CAPTCHA verification.");
        }
    }

    private class RecaptchaResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("challenge_ts")]
        public string? ChallengeTimestamp { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}
```

### 2. Register in Dependency Injection

In `Program.cs` or your service registration:

```csharp
// Configure options
builder.Services.Configure<RecaptchaV3Options>(
    builder.Configuration.GetSection(RecaptchaV3Options.SectionName));

// Register the validator
builder.Services.AddSingleton<IStorefrontCaptchaValidator, RecaptchaV3Validator>();

// The guard is already registered, it will automatically use the validator
```

### 3. Add Configuration

In `appsettings.json`:

```json
{
  "Storefront": {
    "Security": {
      "Captcha": {
        "Enabled": true,
        "RequestHeaderName": "X-Storefront-Captcha-Token",
        "RecaptchaV3": {
          "SecretKey": "your-secret-key-here",
          "MinimumScore": 0.5,
          "VerifyUrl": "https://www.google.com/recaptcha/api/siteverify"
        }
      }
    }
  }
}
```

## Other CAPTCHA Providers

### hCaptcha

Similar implementation, verify endpoint: `https://hcaptcha.com/siteverify`

### Cloudflare Turnstile

Similar implementation, verify endpoint: `https://challenges.cloudflare.com/turnstile/v0/siteverify`

### Custom Provider

Implement `IStorefrontCaptchaValidator` with your custom validation logic.

## Testing

### Disable CAPTCHA in Development

```json
{
  "Storefront": {
    "Security": {
      "Captcha": {
        "Enabled": false
      }
    }
  }
}
```

### Mock Validator for Testing

```csharp
public class MockCaptchaValidator : IStorefrontCaptchaValidator
{
    public Task<StorefrontCaptchaValidationResult> ValidateAsync(
        string token,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        // Always succeed for testing
        return Task.FromResult(StorefrontCaptchaValidationResult.Success());
        
        // Or test failure scenarios:
        // return Task.FromResult(StorefrontCaptchaValidationResult.Failure(
        //     "CAPTCHA_INVALID", "Test failure"));
    }
}
```

## Security Considerations

1. **Secret Key Protection**: Never commit secret keys to source control. Use environment variables or secure configuration providers.

2. **Fail Closed**: The guard fails closed when enabled but no validator is configured, preventing accidental exposure.

3. **Rate Limiting**: CAPTCHA should be combined with rate limiting for comprehensive protection.

4. **Score Thresholds**: For reCAPTCHA v3, adjust the minimum score based on your security requirements (0.0-1.0).

5. **IP Validation**: Pass the client IP to the CAPTCHA provider for additional validation context.

## Troubleshooting

### "CAPTCHA_PROVIDER_NOT_CONFIGURED" Error

- Ensure `IStorefrontCaptchaValidator` is registered in DI
- Check that the validator is being injected into `StorefrontCaptchaGuard`
- Verify configuration is loaded correctly

### "CAPTCHA_REQUIRED" Error

- Client must send the CAPTCHA token in the configured header
- Default header: `X-Storefront-Captcha-Token`

### Validation Always Fails

- Check secret key configuration
- Verify the CAPTCHA provider endpoint is accessible
- Review logs for specific error messages
- Ensure client is sending valid tokens from the CAPTCHA provider
