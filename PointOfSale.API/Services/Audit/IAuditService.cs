using System.Threading.Tasks;

namespace PointOfSale.API.Services.Audit;

public interface IAuditService
{
    Task LogCreate(string entityType, string entityId, string description, object? afterData, string? reference = null);
    Task LogUpdate(string entityType, string entityId, string description, object? beforeData, object? afterData, string? reference = null);
    Task LogDelete(string entityType, string entityId, string description, object? beforeData, string? reference = null);
    Task LogInactivate(string entityType, string entityId, string description, string? reference = null);
    Task LogPost(string entityType, string entityId, string description, string? reference = null);
    Task LogUnpost(string entityType, string entityId, string description, string? reference = null);
    Task LogError(string description, string? reference = null);
    Task LogLogin(string? userId, bool successFlag, string description);
    Task LogStockMovement(string productId, decimal qty, Guid warehouseId, string reference, string description);
    Task LogAction(string actionType, string entityType, string entityId, string description, object? beforeData, object? afterData, string? reference, bool success, string? source = "Web");
}
