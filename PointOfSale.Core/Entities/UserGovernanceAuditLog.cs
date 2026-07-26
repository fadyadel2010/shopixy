namespace PointOfSale.Core.Entities;

public sealed class UserGovernanceAuditLog
{
    public Guid Id { get; set; }
    public Guid TargetUserId { get; set; }
    public int TargetRole { get; set; }
    public Guid? AdminId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string BeforeState { get; set; } = "{}";
    public string AfterState { get; set; } = "{}";
    public string? Metadata { get; set; }
    public DateTime TimestampUtc { get; set; }
}
