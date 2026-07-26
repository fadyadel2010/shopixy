using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.CommerceHub;

/// <summary>
/// W3 FIX: Canonical status names for API responses.
/// 
/// The CommerceHubRefundRequestStatus enum has aliases:
///   - ReadyForExecution = AwaitingPayout (value 9)
///   - Executed = Paid (value 10)
///
/// Since JsonStringEnumConverter serializes to the *first* name defined for a value,
/// the API will always return "AwaitingPayout" and "Paid" (the primary names).
///
/// This helper provides explicit canonical display names for frontend consumption,
/// ensuring consistent UI labels regardless of which alias the backend uses internally.
/// </summary>
public static class CommerceHubStatusDisplayNames
{
    /// <summary>
    /// Returns the canonical display name for a refund request status.
    /// Use this in DTOs when you want to guarantee a specific label.
    /// </summary>
    public static string ToDisplayName(this CommerceHubRefundRequestStatus status) => status switch
    {
        CommerceHubRefundRequestStatus.Requested => "Requested",
        CommerceHubRefundRequestStatus.UnderReview => "Under Review",
        CommerceHubRefundRequestStatus.Approved => "Approved",
        CommerceHubRefundRequestStatus.Rejected => "Rejected",
        CommerceHubRefundRequestStatus.Cancelled => "Cancelled",
        CommerceHubRefundRequestStatus.Processing => "Processing",
        CommerceHubRefundRequestStatus.Completed => "Completed",
        CommerceHubRefundRequestStatus.Failed => "Failed",
        CommerceHubRefundRequestStatus.WaitingForReturn => "Waiting For Return",
        CommerceHubRefundRequestStatus.AwaitingPayout => "Awaiting Payout",
        // ReadyForExecution == AwaitingPayout, so this arm is unreachable (same int value)
        CommerceHubRefundRequestStatus.Paid => "Paid",
        // Executed == Paid, so this arm is unreachable (same int value)
        _ => status.ToString()
    };

    /// <summary>
    /// Returns the canonical display name for a return status.
    /// </summary>
    public static string ToDisplayName(this CommerceHubReturnStatus status) => status switch
    {
        CommerceHubReturnStatus.Pending => "Pending",
        CommerceHubReturnStatus.PickupScheduled => "Pickup Scheduled",
        CommerceHubReturnStatus.PickedUp => "Picked Up",
        CommerceHubReturnStatus.InTransit => "In Transit",
        CommerceHubReturnStatus.DeliveredToVendor => "Delivered To Vendor",
        CommerceHubReturnStatus.Confirmed => "Confirmed",
        CommerceHubReturnStatus.Failed => "Failed",
        _ => status.ToString()
    };

    /// <summary>
    /// Returns a machine-readable canonical status code (no aliases).
    /// The enum value 9 always returns "AwaitingPayout" (never "ReadyForExecution").
    /// The enum value 10 always returns "Paid" (never "Executed").
    /// </summary>
    public static string ToCanonicalCode(this CommerceHubRefundRequestStatus status) => status switch
    {
        CommerceHubRefundRequestStatus.AwaitingPayout => "AwaitingPayout",
        CommerceHubRefundRequestStatus.Paid => "Paid",
        _ => status.ToString()
    };
}
