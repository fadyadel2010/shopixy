using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace PointOfSale.API.DTOs.Platform;

/// <summary>
/// Registration request DTO.
/// Phase 8.0/8.1: Tenant Registration System
/// </summary>
public class TenantRegistrationRequest
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
    [StringLength(63, MinimumLength = 3, ErrorMessage = "Store address must be between 3 and 63 characters")]
    [RegularExpression(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", 
        ErrorMessage = "Store address can only contain lowercase letters, numbers, and hyphens")]
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
    // Reserved subdomains that cannot be used
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        // System
        "www", "api", "admin", "app", "portal", "dashboard", "console", "panel",
        // Infrastructure
        "static", "cdn", "assets", "media", "files", "img", "images", "upload", "uploads",
        // Mail
        "mail", "smtp", "imap", "pop", "mx", "email", "webmail",
        // Development
        "dev", "staging", "test", "qa", "uat", "sandbox", "demo", "preview", "beta",
        // Security
        "auth", "oauth", "sso", "login", "logout", "signin", "signout", "secure", "ssl", "certificate",
        // Generic
        "store", "shop", "buy", "sell", "ecommerce", "pos", "online", "web", "website",
        // Local
        "localhost", "local", "internal", "intranet", "private",
        // Marketing
        "help", "support", "contact", "about", "blog", "news", "status", "docs", "documentation",
        // Legal
        "legal", "terms", "privacy", "tos", "gdpr", "dmca",
        // Platform
        "platform", "saas", "tenant", "tenants", "account", "accounts", "billing", "payment"
    };
    
    // Subdomain regex pattern
    private static readonly Regex SubdomainPattern = new(
        @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    
    /// <summary>
    /// Validates a subdomain for format and reserved names
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) Validate(string subdomain)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
            return (false, "Please choose a store address");
        
        subdomain = subdomain.ToLowerInvariant().Trim();
        
        if (subdomain.Length < 3)
            return (false, "Store address must be at least 3 characters");
        
        if (subdomain.Length > 63)
            return (false, "Store address is too long");
        
        if (!SubdomainPattern.IsMatch(subdomain))
        {
            if (subdomain.Contains("--"))
                return (false, "Remove extra hyphens");
            if (!char.IsLetterOrDigit(subdomain[0]))
                return (false, "Store address must start with a letter or number");
            if (!char.IsLetterOrDigit(subdomain[^1]))
                return (false, "Store address must end with a letter or number");
            return (false, "Only letters, numbers, and hyphens are allowed");
        }
        
        if (ReservedSubdomains.Contains(subdomain))
            return (false, "This address is not available");
        
        return (true, null);
    }
    
    /// <summary>
    /// Generates subdomain suggestions based on a taken subdomain
    /// </summary>
    public static List<string> GenerateSuggestions(string baseSubdomain, int count = 3)
    {
        var suggestions = new List<string>();
        
        // Try numeric suffixes
        for (int i = 1; i <= 9 && suggestions.Count < count; i++)
        {
            var candidate = $"{baseSubdomain}-{i}";
            if (candidate.Length <= 63 && !ReservedSubdomains.Contains(candidate))
                suggestions.Add(candidate);
        }
        
        // Try common suffixes
        var suffixes = new[] { "-shop", "-store", "-online" };
        foreach (var suffix in suffixes)
        {
            if (suggestions.Count >= count) break;
            var candidate = $"{baseSubdomain}{suffix}";
            if (candidate.Length <= 63 && !ReservedSubdomains.Contains(candidate))
                suggestions.Add(candidate);
        }
        
        // Try without hyphens
        if (suggestions.Count < count && baseSubdomain.Contains('-'))
        {
            var noHyphens = baseSubdomain.Replace("-", "");
            if (noHyphens.Length >= 3 && !ReservedSubdomains.Contains(noHyphens))
                suggestions.Add(noHyphens);
        }
        
        return suggestions.Take(count).ToList();
    }
}
