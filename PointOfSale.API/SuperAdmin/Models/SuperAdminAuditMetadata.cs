namespace PointOfSale.API.SuperAdmin.Models;

public sealed class SuperAdminAuditMetadata
{
    public string ActorType { get; init; } = SuperAdminSecurityConstants.Role;
    public Guid? ActorId { get; init; }
    public string? ActorEmail { get; init; }
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string? RequestId { get; init; }
    public string? Endpoint { get; init; }
    public string? Method { get; init; }
    public DateTime TimestampUtc { get; init; }
    public object? Details { get; init; }
}
