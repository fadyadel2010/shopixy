namespace PointOfSale.Core.Enums;

/// <summary>
/// State machine for ServiceRequest lifecycle.
/// Valid transitions:
/// Pending → Accepted → CompletedByProvider → CompletedByBuyer
/// Pending → Rejected
/// Pending → Cancelled
/// Accepted → Cancelled
/// </summary>
public enum ServiceRequestStatus
{
    Pending = 0,
    Accepted = 1,
    CompletedByProvider = 2,
    CompletedByBuyer = 3,
    Cancelled = 4,
    Rejected = 5
}
