using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Governance;

public interface IUserGovernanceService
{
    Task EnsureProfileExistsAsync(
        UserGovernanceRole role,
        Guid subjectId,
        CancellationToken cancellationToken = default);

    Task RecordAccessAsync(
        UserGovernanceRole role,
        Guid subjectId,
        CancellationToken cancellationToken = default);

    Task EnsureRegistrationAllowedAsync(
        string? email,
        string? phoneNumber,
        string? ipAddress,
        string? deviceFingerprint,
        CancellationToken cancellationToken = default);

    Task EnsureCustomerCheckoutAllowedAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task EnsureCustomerLoginAllowedAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task EnsureCustomerRefundRequestAllowedAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task EnsureAdminLoginAllowedAsync(
        Guid adminId,
        CancellationToken cancellationToken = default);

    Task EnsureTenantLoginAllowedAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task EnsureVendorListingAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task EnsureVendorOrderAcceptanceAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task EnsureVendorOrderOperationAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task EnsureVendorPayoutAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task EnsureVendorRefundExecutionAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task EnsureVendorRepaymentAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPagedResult<SuperAdminGovernedUserListItemDto>> GetUsersAsync(
        SuperAdminUserGovernanceListQuery query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminGovernedUserDetailsDto?> GetUserDetailsAsync(
        Guid subjectId,
        UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default);

    Task<SuperAdminGovernedUserRiskDto?> GetRiskAsync(
        Guid subjectId,
        UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default);

    Task<SuperAdminGovernedUserDetailsDto> ApplyActionAsync(
        Guid subjectId,
        UserGovernanceRole? role,
        UserGovernanceStatus targetStatus,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<SuperAdminGovernedUserDetailsDto> SetVendorVerificationAsync(
        Guid vendorId,
        bool verified,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<SuperAdminGovernedUserDetailsDto> SetVendorMarketplaceApprovalAsync(
        Guid vendorId,
        bool approved,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPagedResult<SuperAdminGovernanceBlacklistItemDto>> GetBlacklistAsync(
        UserGovernanceBlacklistType? type,
        bool activeOnly,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SuperAdminGovernanceBlacklistItemDto> UpsertBlacklistAsync(
        SuperAdminUpsertBlacklistRequestDto request,
        Guid adminId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminGovernanceBlacklistItemDto> RevokeBlacklistAsync(
        Guid blacklistEntryId,
        string reason,
        Guid adminId,
        CancellationToken cancellationToken = default);
}

public sealed class UserGovernanceException : Exception
{
    public UserGovernanceException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
