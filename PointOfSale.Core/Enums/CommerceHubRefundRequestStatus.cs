namespace PointOfSale.Core.Enums;

public enum CommerceHubRefundRequestStatus
{
    Requested = 0,
    UnderReview = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Processing = 5,
    Completed = 6,
    Failed = 7,
    WaitingForReturn = 8,
    AwaitingPayout = 9,
    ReadyForExecution = AwaitingPayout,
    Paid = 10,
    Executed = Paid
}
