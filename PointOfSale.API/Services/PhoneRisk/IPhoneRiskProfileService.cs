using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.PhoneRisk;

public interface IPhoneRiskProfileService
{
    Task<string> NormalizePhoneAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default);

    Task<AdminPhoneRiskStatusDto?> GetStatusAsync(Guid tenantId, string? phoneNumber, CancellationToken cancellationToken = default);

    Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken cancellationToken = default);

    Task<int> GetGlobalSpamReportsAsync(Guid tenantId, string? phoneNumber, CancellationToken cancellationToken = default);

    Task<bool> HasReportedSpamAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken = default);

    Task<AdminPhoneRiskStatusDto> BlockAsync(
        Guid tenantId,
        string phoneNumber,
        string? reason,
        Guid? blockedByUserId,
        CancellationToken cancellationToken = default);

    Task<AdminPhoneRiskStatusDto> UnblockAsync(
        Guid tenantId,
        string phoneNumber,
        CancellationToken cancellationToken = default);

    Task<AdminPhoneRiskStatusDto> ReportSpamAsync(
        Guid tenantId,
        Guid orderId,
        string phoneNumber,
        Guid? reportedByUserId,
        CancellationToken cancellationToken = default);

    Task<AdminPagedResult<AdminBlockedPhoneListItemDto>> ListBlockedAsync(
        Guid tenantId,
        AdminBlockedPhoneQuery query,
        CancellationToken cancellationToken = default);
}
