using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// DTO for getting automation settings.
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// </summary>
public class AutomationSettingsResponse
{
    public bool EnableAbandonedRecovery { get; set; }
    public int FirstMessageDelayMinutes { get; set; }
    public int SecondMessageDelayMinutes { get; set; }
    public string WorkingHoursFrom { get; set; } = string.Empty; // HH:mm format
    public string WorkingHoursTo { get; set; } = string.Empty; // HH:mm format
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// DTO for updating automation settings.
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// </summary>
public class UpdateAutomationSettingsRequest
{
    [Required]
    public bool EnableAbandonedRecovery { get; set; }
    
    [Required]
    [Range(0, 10080, ErrorMessage = "First message delay must be between 0 and 10080 minutes (7 days)")]
    public int FirstMessageDelayMinutes { get; set; }
    
    [Required]
    [Range(0, 20160, ErrorMessage = "Second message delay must be between 0 and 20160 minutes (14 days)")]
    public int SecondMessageDelayMinutes { get; set; }
    
    [Required]
    [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Working hours from must be in HH:mm format")]
    public string WorkingHoursFrom { get; set; } = string.Empty;
    
    [Required]
    [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Working hours to must be in HH:mm format")]
    public string WorkingHoursTo { get; set; } = string.Empty;
}

/// <summary>
/// DTO for eligibility status response.
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// </summary>
public class AbandonedCartEligibilityResponse
{
    public bool IsEligible { get; set; }
    public bool HasWhatsAppAccount { get; set; }
    public bool WhatsAppAccountEnabledForRecovery { get; set; }
    public bool WhatsAppAccountConnected { get; set; }
    public bool AutomationEnabled { get; set; }
    public string? IneligibilityReason { get; set; }
    public DateTime CheckedAtUtc { get; set; }
}

/// <summary>
/// Standard error response for automation APIs.
/// </summary>
public class AutomationApiErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string TraceId { get; set; } = string.Empty;

    public static AutomationApiErrorResponse ValidationError(string message, string traceId)
    {
        return new AutomationApiErrorResponse
        {
            Error = "Validation failed",
            Details = message,
            TraceId = traceId
        };
    }

    public static AutomationApiErrorResponse InternalError(string traceId)
    {
        return new AutomationApiErrorResponse
        {
            Error = "Internal server error",
            Details = "An unexpected error occurred. Please contact support.",
            TraceId = traceId
        };
    }

    public static AutomationApiErrorResponse NotFound(string message, string traceId)
    {
        return new AutomationApiErrorResponse
        {
            Error = "Not found",
            Details = message,
            TraceId = traceId
        };
    }
}

/// <summary>
/// DTO for abandoned cart recovery statistics.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// </summary>
public class AbandonedCartStatsResponse
{
    public int TotalAbandoned { get; set; }
    public int MessagesSent { get; set; }
    public int Recovered { get; set; }
    public decimal RecoveryRate { get; set; }
}

