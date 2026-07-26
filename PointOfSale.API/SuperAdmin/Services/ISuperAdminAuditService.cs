namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminAuditService
{
    Task LogActionAsync(
        string action,
        string entityType,
        string entityId,
        string description,
        bool success,
        object? details = null,
        string? reference = null,
        Guid? actorId = null,
        string? actorEmail = null,
        CancellationToken cancellationToken = default);
}
