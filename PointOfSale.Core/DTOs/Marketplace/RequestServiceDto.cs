namespace PointOfSale.Core.DTOs.Marketplace;

public record RequestServiceDto(
    Guid ServiceId,
    string? RequestMessage = null
);

public sealed record ServiceRequestEntryAuthority(
    bool CanRequest,
    string Reason,
    Guid? ActiveRequestId = null);
