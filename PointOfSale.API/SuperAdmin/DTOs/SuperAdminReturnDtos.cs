using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminReturnStatusUpdateRequest
{
    public DateTime? TimestampUtc { get; init; }
    public string? TrackingNumber { get; init; }
    public string? CourierName { get; init; }
    public string? Notes { get; init; }
}

public sealed class SuperAdminOverrideReturnStatusRequest
{
    public CommerceHubReturnStatus Status { get; init; }
    public DateTime? TimestampUtc { get; init; }
    public string? TrackingNumber { get; init; }
    public string? CourierName { get; init; }
    public string? Notes { get; init; }
}
