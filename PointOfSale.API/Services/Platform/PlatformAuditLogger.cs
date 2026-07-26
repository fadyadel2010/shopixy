using Microsoft.Extensions.Logging;

namespace PointOfSale.API.Services.Platform;

/// <summary>
/// Platform-level audit logging for registration activity.
/// Phase 8.3: Tenant Registration Hardening
/// 
/// Logs are:
/// - Structured (JSON-friendly)
/// - Tenant-agnostic (platform-level)
/// - Secure (no passwords/secrets)
/// - Non-blocking (fire-and-forget)
/// </summary>
public interface IPlatformAuditLogger
{
    void LogRegistrationAttempted(string clientIp, string email, string subdomain);
    void LogRegistrationSucceeded(Guid tenantId, string email, string subdomain, TimeSpan duration);
    void LogRegistrationFailed(string email, string subdomain, string reason, string errorCode);
    void LogRateLimitTriggered(string clientIp, string endpoint, int currentCount, int limit);
    void LogIdempotencyHit(string requestId, Guid tenantId);
    void LogAbuseDetected(string clientIp, string reason, string details);
    void LogSubdomainCheck(string clientIp, string subdomain, bool available);
    void LogEmailCheck(string clientIp, string email, bool registered);
}

public class PlatformAuditLogger : IPlatformAuditLogger
{
    private readonly ILogger<PlatformAuditLogger> _logger;

    public PlatformAuditLogger(ILogger<PlatformAuditLogger> logger)
    {
        _logger = logger;
    }

    public void LogRegistrationAttempted(string clientIp, string email, string subdomain)
    {
        _logger.LogInformation(
            "RegistrationAttempted | IP: {IP} | Email: {Email} | Subdomain: {Subdomain}",
            MaskIp(clientIp),
            MaskEmail(email),
            subdomain);
    }

    public void LogRegistrationSucceeded(Guid tenantId, string email, string subdomain, TimeSpan duration)
    {
        _logger.LogInformation(
            "RegistrationSucceeded | TenantId: {TenantId} | Email: {Email} | Subdomain: {Subdomain} | Duration: {Duration}ms",
            tenantId,
            MaskEmail(email),
            subdomain,
            duration.TotalMilliseconds);
    }

    public void LogRegistrationFailed(string email, string subdomain, string reason, string errorCode)
    {
        _logger.LogWarning(
            "RegistrationFailed | Email: {Email} | Subdomain: {Subdomain} | Reason: {Reason} | ErrorCode: {ErrorCode}",
            MaskEmail(email),
            subdomain,
            reason,
            errorCode);
    }

    public void LogRateLimitTriggered(string clientIp, string endpoint, int currentCount, int limit)
    {
        _logger.LogWarning(
            "RateLimitTriggered | IP: {IP} | Endpoint: {Endpoint} | Count: {Count}/{Limit}",
            MaskIp(clientIp),
            endpoint,
            currentCount,
            limit);
    }

    public void LogIdempotencyHit(string requestId, Guid tenantId)
    {
        _logger.LogInformation(
            "IdempotencyHit | RequestId: {RequestId} | TenantId: {TenantId}",
            MaskRequestId(requestId),
            tenantId);
    }

    public void LogAbuseDetected(string clientIp, string reason, string details)
    {
        _logger.LogWarning(
            "AbuseDetected | IP: {IP} | Reason: {Reason} | Details: {Details}",
            MaskIp(clientIp),
            reason,
            details);
    }

    public void LogSubdomainCheck(string clientIp, string subdomain, bool available)
    {
        _logger.LogDebug(
            "SubdomainCheck | IP: {IP} | Subdomain: {Subdomain} | Available: {Available}",
            MaskIp(clientIp),
            subdomain,
            available);
    }

    public void LogEmailCheck(string clientIp, string email, bool registered)
    {
        _logger.LogDebug(
            "EmailCheck | IP: {IP} | Email: {Email} | Registered: {Registered}",
            MaskIp(clientIp),
            MaskEmail(email),
            registered);
    }

    // ==========================================================================
    // Privacy-preserving masking functions
    // ==========================================================================

    private static string MaskIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return "unknown";
        
        var parts = ip.Split('.');
        if (parts.Length == 4)
        {
            return $"{parts[0]}.{parts[1]}.{parts[2]}.***";
        }
        
        // IPv6 or other format
        if (ip.Length > 10)
        {
            return $"{ip[..6]}...***";
        }
        
        return "***";
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "unknown";
        
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "***@***";
        
        var localPart = email[..atIndex];
        var domainPart = email[(atIndex + 1)..];
        
        var maskedLocal = localPart.Length <= 2 
            ? "***" 
            : $"{localPart[0]}***{localPart[^1]}";
        
        var domainDotIndex = domainPart.LastIndexOf('.');
        var maskedDomain = domainDotIndex > 0
            ? $"***{domainPart[domainDotIndex..]}"
            : "***";
        
        return $"{maskedLocal}@{maskedDomain}";
    }

    private static string MaskRequestId(string requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return "none";
        if (requestId.Length <= 8) return "***";
        return $"{requestId[..4]}...{requestId[^4..]}";
    }
}
