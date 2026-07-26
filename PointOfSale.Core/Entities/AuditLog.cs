using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PointOfSale.Core.Entities;

public class AuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long LogId { get; set; }
    
    /// <summary>
    /// Tenant ID for multi-tenant audit trail.
    /// Nullable to support system-level operations without tenant context.
    /// </summary>
    public Guid? TenantId { get; set; }
    
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    
    public Guid? UserId { get; set; }
    
    [MaxLength(150)]
    public string? UserNameSnapshot { get; set; }
    
    [MaxLength(50)]
    public string ActionType { get; set; } = string.Empty; // Create, Update, Delete, etc.
    
    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty; // Product, Customer, etc.
    
    [MaxLength(50)]
    public string EntityId { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    public string? BeforeDataJson { get; set; }
    
    public string? AfterDataJson { get; set; }
    
    [MaxLength(50)]
    public string? IpAddress { get; set; }
    
    [MaxLength(500)]
    public string? UserAgent { get; set; }
    
    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }
    
    [MaxLength(50)]
    public string? Source { get; set; } // Web, API, Background
    
    public bool SuccessFlag { get; set; }
}
