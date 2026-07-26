namespace PointOfSale.Core.DTOs.AbandonedCart;

/// <summary>
/// Request model for upserting cart activity.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// Reduces parameter sprawl and improves maintainability.
/// </summary>
public class UpsertCartActivityRequest
{
    public Guid TenantId { get; set; }
    public Guid CartId { get; set; }
    public string? SessionId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? ContactChannel { get; set; }
    public bool PhoneVerified { get; set; }
    public bool EmailVerified { get; set; }
    public string CartSnapshotJson { get; set; } = "{}";
    public DateTime OccurredAtUtc { get; set; }
}
