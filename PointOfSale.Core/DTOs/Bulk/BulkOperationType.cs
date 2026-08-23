namespace PointOfSale.Core.DTOs.Bulk;

/// <summary>
/// Server-selected bulk operation. The client never supplies the operation;
/// the request remains action-agnostic. Each future action endpoint binds to
/// one of these constants.
/// </summary>
public enum BulkOperationType
{
    Activate = 0,
    Deactivate = 1,
    Publish = 2,
    Unpublish = 3,
    Delete = 4
}
