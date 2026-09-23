using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Tenancy;

namespace PointOfSale.API.DTOs.Platform;

/// <summary>
/// Registration request DTO.
/// Phase 8.0/8.1: Tenant Registration System
/// </summary>
public class TenantRegistrationRequest : IValidatableObject
{
    /// <summary>
    /// Business name (displayed to customers)
    /// </summary>
    [Required(ErrorMessage = "Please enter your business name")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Business name must be between 2 and 100 characters")]
    public string BusinessName { get; set; } = string.Empty;
    
    /// <summary>
    /// Owner's email address (used for login)
    /// </summary>
    [Required(ErrorMessage = "Please enter your email")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Password for the owner account
    /// </summary>
    [Required(ErrorMessage = "Please create a password")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = string.Empty;
    
    /// <summary>
    /// Store address (subdomain)
    /// </summary>
    [Required(ErrorMessage = "Please choose a store address")]
    public string Subdomain { get; set; } = string.Empty;
    
    /// <summary>
    /// Country code (optional, ISO 3166-1 alpha-2)
    /// </summary>
    [StringLength(2)]
    public string? Country { get; set; }
    
    /// <summary>
    /// Currency code (optional, ISO 4217)
    /// </summary>
    [StringLength(3)]
    public string? Currency { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!StoreSlug.TryNormalize(Subdomain, out _, out var errorMessage))
        {
            yield return new ValidationResult(errorMessage, [nameof(Subdomain)]);
        }
    }
}

/// <summary>
/// Registration success response DTO.
/// </summary>
public class TenantRegistrationResponse
{
    /// <summary>
    /// The created tenant's ID
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// The confirmed subdomain
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;
    
    /// <summary>
    /// Store name
    /// </summary>
    public string StoreName { get; set; } = string.Empty;
    
    /// <summary>
    /// Full storefront URL
    /// </summary>
    public string StorefrontUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// POS dashboard URL
    /// </summary>
    public string PosUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Tenant status
    /// </summary>
    public string Status { get; set; } = "active";
    
    /// <summary>
    /// Success message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// User info for auto-login
    /// </summary>
    public RegistrationUserDto User { get; set; } = null!;
    
    /// <summary>
    /// Access token for immediate login
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Refresh token for session management
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// User info included in registration response
/// </summary>
public class RegistrationUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = "Owner";
    public Guid TenantId { get; set; }
}

/// <summary>
/// Subdomain availability check request
/// </summary>
public class SubdomainCheckRequest
{
    [Required]
    [StringLength(63, MinimumLength = 3)]
    public string Slug { get; set; } = string.Empty;
}

/// <summary>
/// Subdomain availability check response
/// </summary>
public class SubdomainCheckResponse
{
    /// <summary>
    /// Whether the subdomain is available
    /// </summary>
    public bool Available { get; set; }
    
    /// <summary>
    /// The checked subdomain
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Backend-resolved storefront origin when the slug is valid and available.
    /// </summary>
    public string? StorefrontUrl { get; set; }
    
    /// <summary>
    /// Alternative suggestions if not available
    /// </summary>
    public List<string> Suggestions { get; set; } = new();
    
    /// <summary>
    /// Reason if not available (for UX messaging)
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Registration error response
/// </summary>
public class RegistrationErrorResponse
{
    /// <summary>
    /// Error code for programmatic handling
    /// </summary>
    public string Error { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-friendly error message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Field-specific errors (for form validation)
    /// </summary>
    public Dictionary<string, string>? FieldErrors { get; set; }
    
    /// <summary>
    /// Suggestions (e.g., for subdomain)
    /// </summary>
    public List<string>? Suggestions { get; set; }
}

/// <summary>
/// Static helper for subdomain validation
/// </summary>
public static class SubdomainValidator
{
    /// <summary>
    /// Validates a subdomain for format and reserved names
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) Validate(string subdomain)
    {
        var isValid = StoreSlug.TryNormalize(subdomain, out _, out var errorMessage);
        return (isValid, errorMessage);
    }
    
    /// <summary>
    /// Generates subdomain suggestions based on a taken subdomain
    /// </summary>
    public static List<string> GenerateSuggestions(string baseSubdomain, int count = 3)
    {
        return StoreSlug.GenerateSuggestions(baseSubdomain, count).ToList();
    }
}
