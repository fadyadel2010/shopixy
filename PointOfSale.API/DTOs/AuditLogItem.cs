using System;

namespace PointOfSale.API.DTOs;

public class AuditLogItem
{
    /// <summary>
    /// Tenant ID for multi-tenant audit trail
    /// </summary>
    public Guid? TenantId { get; set; }
    
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string? UserNameSnapshot { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? BeforeDataJson { get; set; }
    public string? AfterDataJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Source { get; set; }
    public bool SuccessFlag { get; set; }
}
