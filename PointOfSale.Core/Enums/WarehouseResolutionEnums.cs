namespace PointOfSale.Core.Enums;

public enum WarehouseResolutionStatus
{
    Unresolved = 0,
    Resolved = 1,
    Locked = 2,
    Ordered = 3,
    Invalidated = 4,
    Expired = 5,
    Failed = 6
}

public enum WarehouseResolutionSource
{
    StorefrontCheckout = 1,
    StorefrontCheckoutRefresh = 2,
    FinalCheckout = 3,
    SystemInvalidation = 4
}
