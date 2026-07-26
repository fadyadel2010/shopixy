using PointOfSale.API.Services.Governance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Pagination;

namespace PointOfSale.Tests.TestDoubles;

internal sealed class NoOpUserGovernanceService : IUserGovernanceService
{
    public Task EnsureProfileExistsAsync(UserGovernanceRole role, Guid subjectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordAccessAsync(UserGovernanceRole role, Guid subjectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureRegistrationAllowedAsync(string? email, string? phoneNumber, string? ipAddress, string? deviceFingerprint, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureCustomerCheckoutAllowedAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureCustomerLoginAllowedAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureCustomerRefundRequestAllowedAsync(Guid customerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureAdminLoginAllowedAsync(Guid adminId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureTenantLoginAllowedAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureVendorListingAllowedAsync(Guid vendorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureVendorOrderAcceptanceAllowedAsync(Guid vendorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureVendorOrderOperationAllowedAsync(Guid vendorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureVendorPayoutAllowedAsync(Guid vendorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureVendorRefundExecutionAllowedAsync(Guid vendorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureVendorRepaymentAllowedAsync(Guid vendorId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SuperAdminPagedResult<SuperAdminGovernedUserListItemDto>> GetUsersAsync(
        SuperAdminUserGovernanceListQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SuperAdminPagedResult<SuperAdminGovernedUserListItemDto>());

    public Task<SuperAdminGovernedUserDetailsDto?> GetUserDetailsAsync(
        Guid subjectId,
        UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SuperAdminGovernedUserDetailsDto?>(null);

    public Task<SuperAdminGovernedUserRiskDto?> GetRiskAsync(
        Guid subjectId,
        UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SuperAdminGovernedUserRiskDto?>(null);

    public Task<SuperAdminGovernedUserDetailsDto> ApplyActionAsync(
        Guid subjectId,
        UserGovernanceRole? role,
        UserGovernanceStatus targetStatus,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SuperAdminGovernedUserDetailsDto());

    public Task<SuperAdminGovernedUserDetailsDto> SetVendorVerificationAsync(
        Guid vendorId,
        bool verified,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SuperAdminGovernedUserDetailsDto());

    public Task<SuperAdminGovernedUserDetailsDto> SetVendorMarketplaceApprovalAsync(
        Guid vendorId,
        bool approved,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SuperAdminGovernedUserDetailsDto());

    public Task<SuperAdminPagedResult<SuperAdminGovernanceBlacklistItemDto>> GetBlacklistAsync(
        UserGovernanceBlacklistType? type,
        bool activeOnly,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = PaginationGuard.NormalizePage(page);
        var normalizedPageSize = PaginationGuard.NormalizePageSize(pageSize);

        return Task.FromResult(new SuperAdminPagedResult<SuperAdminGovernanceBlacklistItemDto>
        {
            Items = Array.Empty<SuperAdminGovernanceBlacklistItemDto>(),
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = 0,
            TotalPages = 0
        });
    }

    public Task<SuperAdminGovernanceBlacklistItemDto> UpsertBlacklistAsync(
        SuperAdminUpsertBlacklistRequestDto request,
        Guid adminId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SuperAdminGovernanceBlacklistItemDto());

    public Task<SuperAdminGovernanceBlacklistItemDto> RevokeBlacklistAsync(
        Guid blacklistEntryId,
        string reason,
        Guid adminId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SuperAdminGovernanceBlacklistItemDto());
}
