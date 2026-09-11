using System.Text.Json;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Pagination;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.Governance;

public sealed partial class UserGovernanceService : IUserGovernanceService
{
    private const decimal HighRefundRateThreshold = 40m;
    private const decimal HighDisputeRateThreshold = 25m;
    private const int MediumFailedPaymentsThreshold = 3;
    private const int HighVelocityRefundThreshold = 3;
    private const int DefaultDetailAuditWindow = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;
    private readonly ICommerceHubVendorVisibilityService _vendorVisibilityService;
    private readonly ICommerceHubGovernanceCacheInvalidationService _governanceCacheInvalidationService;
    private readonly ILogger<UserGovernanceService> _logger;

    public UserGovernanceService(
        ApplicationDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        ICommerceHubVendorDebtService vendorDebtService,
        ICommerceHubVendorVisibilityService vendorVisibilityService,
        ICommerceHubGovernanceCacheInvalidationService governanceCacheInvalidationService,
        ILogger<UserGovernanceService> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _vendorDebtService = vendorDebtService;
        _vendorVisibilityService = vendorVisibilityService;
        _governanceCacheInvalidationService = governanceCacheInvalidationService;
        _logger = logger;
    }

    public async Task EnsureProfileExistsAsync(
        UserGovernanceRole role,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubjectExistsAsync(role, subjectId, cancellationToken);
        _ = await GetOrCreateTrackedProfileAsync(role, subjectId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAccessAsync(
        UserGovernanceRole role,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSubjectExistsAsync(role, subjectId, cancellationToken);

        var profile = await GetOrCreateTrackedProfileAsync(role, subjectId, cancellationToken);
        profile.LastLoginAtUtc = DateTime.UtcNow;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        var ipAddress = NormalizeBlacklistValue(
            UserGovernanceBlacklistType.IpAddress,
            GetClientIpAddress());
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            profile.LastIpAddress = ipAddress;
        }

        var deviceFingerprint = NormalizeBlacklistValue(
            UserGovernanceBlacklistType.DeviceId,
            GetDeviceFingerprint());
        if (!string.IsNullOrWhiteSpace(deviceFingerprint))
        {
            profile.DeviceFingerprint = deviceFingerprint;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureRegistrationAllowedAsync(
        string? email,
        string? phoneNumber,
        string? ipAddress,
        string? deviceFingerprint,
        CancellationToken cancellationToken = default)
    {
        var normalizedCandidates = new Dictionary<UserGovernanceBlacklistType, string?>
        {
            [UserGovernanceBlacklistType.Email] = NormalizeBlacklistValue(UserGovernanceBlacklistType.Email, email),
            [UserGovernanceBlacklistType.PhoneNumber] = NormalizeBlacklistValue(UserGovernanceBlacklistType.PhoneNumber, phoneNumber),
            [UserGovernanceBlacklistType.IpAddress] = NormalizeBlacklistValue(UserGovernanceBlacklistType.IpAddress, ipAddress ?? GetClientIpAddress()),
            [UserGovernanceBlacklistType.DeviceId] = NormalizeBlacklistValue(UserGovernanceBlacklistType.DeviceId, deviceFingerprint ?? GetDeviceFingerprint())
        };

        var activeBlacklistEntries = await _dbContext.UserGovernanceBlacklistEntries
            .AsNoTracking()
            .Where(entry => entry.IsActive)
            .ToListAsync(cancellationToken);

        var matches = activeBlacklistEntries
            .Where(entry =>
                normalizedCandidates.TryGetValue(entry.Type, out var candidate) &&
                !string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(candidate, entry.ValueNormalized, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
        {
            return;
        }

        var types = string.Join(
            ", ",
            matches.Select(entry => entry.Type.ToString()).Distinct(StringComparer.OrdinalIgnoreCase));

        throw new UserGovernanceException(
            "BLACKLISTED_REGISTRATION",
            $"Registration is blocked because one or more submitted signals are blacklisted: {types}.",
            StatusCodes.Status409Conflict);
    }

    public async Task EnsureCustomerCheckoutAllowedAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            customerId,
            UserGovernanceRole.Customer,
            allowAutoFinancialLock: false,
            cancellationToken);

        EnsureStatus(
            evaluation,
            "CUSTOMER_SUSPENDED",
            "Customer account is suspended and cannot place new CommerceHub orders.",
            "CUSTOMER_BANNED",
            "Customer account is banned from CommerceHub.");
    }

    public async Task EnsureCustomerLoginAllowedAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            customerId,
            UserGovernanceRole.Customer,
            allowAutoFinancialLock: false,
            cancellationToken);

        if (evaluation.Status == UserGovernanceStatus.Banned)
        {
            throw new UserGovernanceException(
                "CUSTOMER_BANNED",
                "Customer account is banned from CommerceHub.",
                StatusCodes.Status403Forbidden);
        }

        if (evaluation.Status == UserGovernanceStatus.Suspended)
        {
            throw new UserGovernanceException(
                "CUSTOMER_SUSPENDED",
                "Customer account is suspended and cannot sign in.",
                StatusCodes.Status403Forbidden);
        }
    }

    public async Task EnsureCustomerRefundRequestAllowedAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            customerId,
            UserGovernanceRole.Customer,
            allowAutoFinancialLock: false,
            cancellationToken);

        if (evaluation.Status == UserGovernanceStatus.Banned)
        {
            throw new UserGovernanceException(
                "CUSTOMER_BANNED",
                "Customer account is banned from CommerceHub.",
                StatusCodes.Status403Forbidden);
        }

        if (evaluation.Status == UserGovernanceStatus.FinanciallyLocked)
        {
            throw new UserGovernanceException(
                "CUSTOMER_FINANCIAL_LOCKED",
                "Customer refund activity is temporarily blocked by a financial lock.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task EnsureAdminLoginAllowedAsync(
        Guid adminId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            adminId,
            UserGovernanceRole.Admin,
            allowAutoFinancialLock: false,
            cancellationToken);

        switch (evaluation.Status)
        {
            case UserGovernanceStatus.Banned:
                throw new UserGovernanceException(
                    "SUPER_ADMIN_BANNED",
                    "This Super Admin account is banned and cannot sign in.",
                    StatusCodes.Status403Forbidden);
            case UserGovernanceStatus.Suspended:
                throw new UserGovernanceException(
                    "SUPER_ADMIN_SUSPENDED",
                    "This Super Admin account is suspended and cannot sign in.",
                    StatusCodes.Status403Forbidden);
            case UserGovernanceStatus.FinanciallyLocked:
                throw new UserGovernanceException(
                    "SUPER_ADMIN_FINANCIAL_LOCKED",
                    "This Super Admin account is financially locked and cannot sign in.",
                    StatusCodes.Status403Forbidden);
        }
    }

    public async Task EnsureTenantLoginAllowedAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenantAuthority = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => new
            {
                tenant.Status,
                tenant.DeletedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantAuthority == null ||
            tenantAuthority.Status != TenantStatus.Active ||
            tenantAuthority.DeletedAtUtc != null)
        {
            throw new UserGovernanceException(
                "TENANT_AUTHORITY_REDUCED",
                "Tenant access is not active and login is disabled.",
                StatusCodes.Status403Forbidden);
        }

        var evaluation = await RequireEvaluationAsync(
            tenantId,
            UserGovernanceRole.Vendor,
            allowAutoFinancialLock: true,
            cancellationToken);

        if (evaluation.Status == UserGovernanceStatus.Banned)
        {
            throw new UserGovernanceException(
                "VENDOR_BANNED",
                "Vendor access is banned and login is disabled.",
                StatusCodes.Status403Forbidden);
        }

        if (evaluation.Status == UserGovernanceStatus.Suspended)
        {
            throw new UserGovernanceException(
                "VENDOR_SUSPENDED",
                "Vendor access is suspended and login is disabled.",
                StatusCodes.Status403Forbidden);
        }
    }

    public async Task EnsureVendorListingAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var authority = await CommerceHubVendorAuthority.LoadAsync(
            _dbContext,
            vendorId,
            cancellationToken);
        EnsureMarketplaceEligibility(authority, "manage CommerceHub listings");
    }

    public async Task EnsureVendorOrderAcceptanceAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var authority = await CommerceHubVendorAuthority.LoadAsync(
            _dbContext,
            vendorId,
            cancellationToken);
        EnsureMarketplaceEligibility(authority, "receive new CommerceHub orders");
    }

    public async Task EnsureVendorOrderOperationAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            vendorId,
            UserGovernanceRole.Vendor,
            allowAutoFinancialLock: true,
            cancellationToken);

        switch (evaluation.Status)
        {
            case UserGovernanceStatus.Banned:
                throw new UserGovernanceException(
                    "VENDOR_BANNED",
                    "Vendor is banned and order operations are frozen.",
                    StatusCodes.Status403Forbidden);
            case UserGovernanceStatus.Suspended:
                throw new UserGovernanceException(
                    "VENDOR_SUSPENDED",
                    "Vendor is suspended and order operations are frozen.",
                    StatusCodes.Status403Forbidden);
        }

        var authority = await CommerceHubVendorAuthority.LoadAsync(
            _dbContext,
            vendorId,
            cancellationToken);
        EnsureMarketplaceEligibility(authority, "operate CommerceHub orders");
    }

    public async Task EnsureVendorPayoutAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            vendorId,
            UserGovernanceRole.Vendor,
            allowAutoFinancialLock: true,
            cancellationToken);

        switch (evaluation.Status)
        {
            case UserGovernanceStatus.Banned:
                throw new UserGovernanceException(
                    "VENDOR_BANNED",
                    "Vendor is banned and all financial operations are frozen.",
                    StatusCodes.Status403Forbidden);
            case UserGovernanceStatus.Suspended:
                throw new UserGovernanceException(
                    "VENDOR_SUSPENDED",
                    "Vendor is suspended and payouts are blocked.",
                    StatusCodes.Status409Conflict);
            case UserGovernanceStatus.FinanciallyLocked:
                throw new UserGovernanceException(
                    "VENDOR_FINANCIAL_LOCKED",
                    "Vendor payouts are blocked by a financial lock.",
                    StatusCodes.Status409Conflict);
        }

        var authority = await CommerceHubVendorAuthority.LoadAsync(
            _dbContext,
            vendorId,
            cancellationToken);
        EnsureMarketplaceEligibility(authority, "receive CommerceHub payouts");
    }

    public async Task EnsureVendorRefundExecutionAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            vendorId,
            UserGovernanceRole.Vendor,
            allowAutoFinancialLock: true,
            cancellationToken);

        switch (evaluation.Status)
        {
            case UserGovernanceStatus.Banned:
                throw new UserGovernanceException(
                    "VENDOR_BANNED",
                    "Vendor is banned and all financial operations are frozen.",
                    StatusCodes.Status403Forbidden);
            case UserGovernanceStatus.FinanciallyLocked:
                throw new UserGovernanceException(
                    "VENDOR_FINANCIAL_LOCKED",
                    "Vendor refund execution is blocked by a financial lock.",
                    StatusCodes.Status409Conflict);
        }
    }

    public async Task EnsureVendorRepaymentAllowedAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await RequireEvaluationAsync(
            vendorId,
            UserGovernanceRole.Vendor,
            allowAutoFinancialLock: true,
            cancellationToken);

        if (evaluation.Status == UserGovernanceStatus.Banned)
        {
            throw new UserGovernanceException(
                "VENDOR_BANNED",
                "Vendor is banned and all financial operations are frozen.",
                StatusCodes.Status403Forbidden);
        }
    }

    public async Task<SuperAdminPagedResult<SuperAdminGovernedUserListItemDto>> GetUsersAsync(
        SuperAdminUserGovernanceListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = PaginationGuard.NormalizePage(query.Page);
        var pageSize = PaginationGuard.NormalizePageSize(query.PageSize);

        var subjects = await LoadSubjectsAsync(query, cancellationToken);
        if (subjects.Count == 0)
        {
            return new SuperAdminPagedResult<SuperAdminGovernedUserListItemDto>
            {
                Items = Array.Empty<SuperAdminGovernedUserListItemDto>(),
                Page = page,
                PageSize = pageSize,
                TotalCount = 0,
                TotalPages = 0
            };
        }

        var evaluations = await EvaluateSubjectsAsync(subjects, persistProfiles: false, allowAutoFinancialLock: false, cancellationToken);

        if (query.Status.HasValue)
        {
            evaluations = evaluations.Where(item => item.Status == query.Status.Value).ToList();
        }

        if (query.RiskLevel.HasValue)
        {
            evaluations = evaluations.Where(item => item.RiskLevel == query.RiskLevel.Value).ToList();
        }

        if (query.HasDebt.HasValue)
        {
            evaluations = evaluations
                .Where(item => (item.Metrics.VendorDebt > 0m) == query.HasDebt.Value)
                .ToList();
        }

        var totalCount = evaluations.Count;
        var skip = PaginationGuard.GetSkip(page, pageSize);
        var pageItems = evaluations
            .OrderByDescending(item => item.Metrics.SuspiciousFlags)
            .ThenByDescending(item => item.Metrics.VendorDebt)
            .ThenByDescending(item => item.Subject.CreatedAtUtc)
            .ThenBy(item => item.Subject.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(pageSize)
            .Select(MapListItem)
            .ToList();

        return new SuperAdminPagedResult<SuperAdminGovernedUserListItemDto>
        {
            Items = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<SuperAdminGovernedUserDetailsDto?> GetUserDetailsAsync(
        Guid subjectId,
        UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await EvaluateSingleSubjectAsync(
            subjectId,
            role,
            persistProfile: true,
            allowAutoFinancialLock: true,
            cancellationToken);

        if (evaluation == null)
        {
            return null;
        }

        var auditTimeline = await _dbContext.UserGovernanceAuditLogs
            .AsNoTracking()
            .Where(entry =>
                entry.TargetUserId == evaluation.Subject.Id &&
                entry.TargetRole == (int)evaluation.Subject.Role)
            .OrderByDescending(entry => entry.TimestampUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(DefaultDetailAuditWindow)
            .Select(entry => new SuperAdminGovernedUserAuditItemDto
            {
                Id = entry.Id,
                ActionType = entry.ActionType,
                Reason = entry.Reason,
                AdminId = entry.AdminId,
                ActorType = entry.ActorType,
                BeforeState = entry.BeforeState,
                AfterState = entry.AfterState,
                TimestampUtc = entry.TimestampUtc
            })
            .ToListAsync(cancellationToken);

        return MapDetails(evaluation, auditTimeline);
    }

    public async Task<SuperAdminGovernedUserRiskDto?> GetRiskAsync(
        Guid subjectId,
        UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await EvaluateSingleSubjectAsync(
            subjectId,
            role,
            persistProfile: true,
            allowAutoFinancialLock: true,
            cancellationToken);

        return evaluation == null ? null : MapRisk(evaluation);
    }

    public async Task<SuperAdminGovernedUserDetailsDto> ApplyActionAsync(
        Guid subjectId,
        UserGovernanceRole? role,
        UserGovernanceStatus targetStatus,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (adminId == Guid.Empty)
        {
            throw new UserGovernanceException(
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for governance actions.",
                StatusCodes.Status403Forbidden);
        }

        await using var governanceTransaction =
            _dbContext.Database.IsRelational() &&
            _dbContext.Database.CurrentTransaction == null
                ? await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken)
                : null;

        var normalizedReason = NormalizeReason(reason);
        var subject = await ResolveSubjectAsync(subjectId, role, cancellationToken)
            ?? throw new UserGovernanceException(
                "USER_NOT_FOUND",
                "Governed user was not found.",
                StatusCodes.Status404NotFound);

        var profile = await GetOrCreateTrackedProfileAsync(subject.Role, subject.Id, cancellationToken);
        var evaluationBefore = await RequireEvaluationAsync(
            subject.Id,
            subject.Role,
            allowAutoFinancialLock: false,
            cancellationToken);

        if (subject.Role == UserGovernanceRole.Admin &&
            targetStatus == UserGovernanceStatus.Banned &&
            adminId == subject.Id)
        {
            throw new UserGovernanceException(
                "SELF_BAN_FORBIDDEN",
                "Super Admins cannot ban themselves.",
                StatusCodes.Status409Conflict);
        }

        if (subject.IsSystemAdmin && targetStatus != UserGovernanceStatus.Active)
        {
            throw new UserGovernanceException(
                "SYSTEM_ADMIN_PROTECTED",
                "The built-in system Super Admin cannot be suspended, banned, or financially locked.",
                StatusCodes.Status409Conflict);
        }

        if (profile.Status == targetStatus &&
            string.Equals(profile.StatusReason, normalizedReason, StringComparison.Ordinal))
        {
            return await GetUserDetailsAsync(subject.Id, subject.Role, cancellationToken)
                ?? throw new UserGovernanceException(
                    "USER_NOT_FOUND",
                    "Governed user was not found.",
                    StatusCodes.Status404NotFound);
        }

        var beforeSnapshot = BuildAuditSnapshot(evaluationBefore);
        VendorGovernanceActionAlerts? operationalAlerts = null;
        if (subject.Role == UserGovernanceRole.Vendor &&
            targetStatus is UserGovernanceStatus.Suspended or UserGovernanceStatus.Banned)
        {
            operationalAlerts = await LoadVendorGovernanceActionAlertsAsync(
                subject.Id,
                targetStatus,
                cancellationToken);
        }

        profile.Status = targetStatus;
        profile.StatusReason = normalizedReason;
        profile.StatusChangedAtUtc = DateTime.UtcNow;
        profile.LastModifiedByAdminId = adminId;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (subject.Role == UserGovernanceRole.Admin &&
            targetStatus != UserGovernanceStatus.Active)
        {
            var platformActor = await _dbContext.SuperAdmins
                .FirstOrDefaultAsync(item => item.Id == subject.Id, cancellationToken);
            platformActor?.ClearRefreshToken();
        }

        CommerceHubVendorVisibilitySyncResult? visibilityResult = null;
        CommerceHubGovernanceCacheInvalidationResult? cacheInvalidation = null;

        if (subject.Role == UserGovernanceRole.Vendor &&
            targetStatus is UserGovernanceStatus.Suspended or UserGovernanceStatus.Banned or UserGovernanceStatus.Active)
        {
            visibilityResult = await _vendorVisibilityService.SyncVendorVisibilityAsync(
                subject.Id,
                targetStatus,
                cancellationToken);

            cacheInvalidation = await _governanceCacheInvalidationService.InvalidateVisibilityAsync(
                subject.Id,
                $"Vendor governance action: {BuildActionType(targetStatus)}",
                cancellationToken);
        }

        await AppendAuditLogAsync(
            subject,
            actorId: adminId,
            actorType: "SUPER_ADMIN",
            actionType: BuildActionType(targetStatus),
            reason: normalizedReason,
            beforeState: beforeSnapshot,
            afterState: BuildAuditSnapshot(profile),
            metadata: new
            {
                actorEmail = adminEmail,
                operationalAlerts = operationalAlerts == null
                    ? null
                    : new
                    {
                        operationalAlerts.ActiveOrderCount,
                        operationalAlerts.PendingPayoutCount,
                        operationalAlerts.PendingPayoutAmount
                    },
                visibility = visibilityResult == null
                    ? null
                    : new
                    {
                        visibilityResult.HiddenCount,
                        visibilityResult.RestoredCount,
                        visibilityResult.ClearedCount,
                        visibilityResult.SearchVerificationEnqueuedCount,
                        listingSnapshot = visibilityResult.ListingSnapshot.Select(item => new
                        {
                            item.ListingId,
                            item.ProductId,
                            item.VariantId,
                            item.WasPublished,
                            item.RestorePublicationOnActivateBefore
                        })
                    },
                cacheInvalidation = cacheInvalidation == null
                    ? null
                    : new
                    {
                        cacheInvalidation.VendorId,
                        cacheInvalidation.Version,
                        cacheInvalidation.UsedRedis,
                        cacheInvalidation.UsedFallback,
                        cacheInvalidation.FailureReason
                    }
            },
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = await GetUserDetailsAsync(subject.Id, subject.Role, cancellationToken)
            ?? throw new UserGovernanceException(
                "USER_NOT_FOUND",
                "Governed user was not found.",
                StatusCodes.Status404NotFound);

        if (governanceTransaction != null)
        {
            await governanceTransaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public Task<SuperAdminGovernedUserDetailsDto> SetVendorVerificationAsync(
        Guid vendorId,
        bool verified,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default) =>
        SetVendorCertificationStateAsync(
            vendorId,
            setVerification: true,
            value: verified,
            reason,
            adminId,
            adminEmail,
            cancellationToken);

    public Task<SuperAdminGovernedUserDetailsDto> SetVendorMarketplaceApprovalAsync(
        Guid vendorId,
        bool approved,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken = default) =>
        SetVendorCertificationStateAsync(
            vendorId,
            setVerification: false,
            value: approved,
            reason,
            adminId,
            adminEmail,
            cancellationToken);

    private async Task<SuperAdminGovernedUserDetailsDto> SetVendorCertificationStateAsync(
        Guid vendorId,
        bool setVerification,
        bool value,
        string reason,
        Guid adminId,
        string? adminEmail,
        CancellationToken cancellationToken)
    {
        if (adminId == Guid.Empty)
        {
            throw new UserGovernanceException(
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for vendor certification changes.",
                StatusCodes.Status403Forbidden);
        }

        await using var transaction =
            _dbContext.Database.IsRelational() &&
            _dbContext.Database.CurrentTransaction == null
                ? await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken)
                : null;

        var normalizedReason = NormalizeReason(reason);
        var subject = await ResolveSubjectAsync(
                vendorId,
                UserGovernanceRole.Vendor,
                cancellationToken)
            ?? throw new UserGovernanceException(
                "VENDOR_NOT_FOUND",
                "Vendor was not found.",
                StatusCodes.Status404NotFound);

        var profile = await GetOrCreateTrackedProfileAsync(
            UserGovernanceRole.Vendor,
            vendorId,
            cancellationToken);
        var beforeSnapshot = BuildAuditSnapshot(profile);
        var now = DateTime.UtcNow;

        var isExactReplay = setVerification
            ? profile.IsKycVerified == value &&
              string.Equals(profile.KycVerificationReason, normalizedReason, StringComparison.Ordinal) &&
              (value || !profile.IsMarketplaceApproved)
            : profile.IsMarketplaceApproved == value &&
              string.Equals(profile.MarketplaceApprovalReason, normalizedReason, StringComparison.Ordinal) &&
              (!value || profile.IsKycVerified);
        if (isExactReplay)
        {
            return await GetUserDetailsAsync(
                    vendorId,
                    UserGovernanceRole.Vendor,
                    cancellationToken)
                ?? throw new UserGovernanceException(
                    "VENDOR_NOT_FOUND",
                    "Vendor was not found.",
                    StatusCodes.Status404NotFound);
        }

        using var certificationMutation =
            UserGovernanceMutationScope.BeginCertificationMutation();

        if (setVerification)
        {
            profile.IsKycVerified = value;
            profile.KycVerifiedAtUtc = value ? now : null;
            profile.KycVerifiedByAdminId = value ? adminId : null;
            profile.KycVerificationReason = normalizedReason;

            if (!value)
            {
                profile.IsMarketplaceApproved = false;
                profile.MarketplaceApprovedAtUtc = null;
                profile.MarketplaceApprovedByAdminId = null;
                profile.MarketplaceApprovalReason =
                    "Marketplace approval revoked because vendor verification was revoked.";
            }
        }
        else
        {
            if (value && !profile.IsKycVerified)
            {
                throw new UserGovernanceException(
                    "VENDOR_VERIFICATION_REQUIRED",
                    "Vendor verification must be completed before marketplace approval.",
                    StatusCodes.Status409Conflict);
            }

            profile.IsMarketplaceApproved = value;
            profile.MarketplaceApprovedAtUtc = value ? now : null;
            profile.MarketplaceApprovedByAdminId = value ? adminId : null;
            profile.MarketplaceApprovalReason = normalizedReason;
        }

        profile.LastModifiedByAdminId = adminId;
        profile.UpdatedAtUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var authority = await CommerceHubVendorAuthority.LoadAsync(
            _dbContext,
            vendorId,
            cancellationToken);
        var visibility = await _vendorVisibilityService.SyncVendorVisibilityAsync(
            vendorId,
            authority.IsMarketplaceEligible
                ? UserGovernanceStatus.Active
                : UserGovernanceStatus.Suspended,
            cancellationToken);
        var cacheInvalidation = await _governanceCacheInvalidationService.InvalidateVisibilityAsync(
            vendorId,
            setVerification
                ? $"Vendor verification changed: {value}"
                : $"Vendor marketplace approval changed: {value}",
            cancellationToken);

        await AppendAuditLogAsync(
            subject,
            actorId: adminId,
            actorType: "SUPER_ADMIN",
            actionType: setVerification
                ? value ? "VERIFY_VENDOR" : "REVOKE_VENDOR_VERIFICATION"
                : value ? "APPROVE_VENDOR_MARKETPLACE" : "REJECT_VENDOR_MARKETPLACE",
            reason: normalizedReason,
            beforeState: beforeSnapshot,
            afterState: BuildAuditSnapshot(profile),
            metadata: new
            {
                actorEmail = adminEmail,
                visibility.HiddenCount,
                visibility.RestoredCount,
                visibility.ClearedCount,
                visibility.SearchVerificationEnqueuedCount,
                cacheInvalidation.Version
            },
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = await GetUserDetailsAsync(
                vendorId,
                UserGovernanceRole.Vendor,
                cancellationToken)
            ?? throw new UserGovernanceException(
                "VENDOR_NOT_FOUND",
                "Vendor was not found.",
                StatusCodes.Status404NotFound);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<SuperAdminPagedResult<SuperAdminGovernanceBlacklistItemDto>> GetBlacklistAsync(
        UserGovernanceBlacklistType? type,
        bool activeOnly,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var query = _dbContext.UserGovernanceBlacklistEntries
            .AsNoTracking()
            .AsQueryable();

        if (type.HasValue)
        {
            query = query.Where(entry => entry.Type == type.Value);
        }

        if (activeOnly)
        {
            query = query.Where(entry => entry.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(entry =>
                entry.ValueDisplay.ToLower().Contains(normalizedSearch) ||
                entry.Reason.ToLower().Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var entries = await query
            .OrderByDescending(entry => entry.IsActive)
            .ThenByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SuperAdminPagedResult<SuperAdminGovernanceBlacklistItemDto>
        {
            Items = entries.Select(MapBlacklistItem).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<SuperAdminGovernanceBlacklistItemDto> UpsertBlacklistAsync(
        SuperAdminUpsertBlacklistRequestDto request,
        Guid adminId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (adminId == Guid.Empty)
        {
            throw new UserGovernanceException(
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for blacklist changes.",
                StatusCodes.Status403Forbidden);
        }

        var normalizedValue = NormalizeBlacklistValue(request.Type, request.Value)
            ?? throw new UserGovernanceException(
                "BLACKLIST_VALUE_INVALID",
                "Blacklist value is invalid.",
                StatusCodes.Status400BadRequest);

        var existing = await _dbContext.UserGovernanceBlacklistEntries
            .FirstOrDefaultAsync(
                entry => entry.Type == request.Type && entry.ValueNormalized == normalizedValue,
                cancellationToken);

        if (existing == null)
        {
            existing = new UserGovernanceBlacklistEntry
            {
                Id = Guid.NewGuid(),
                Type = request.Type,
                ValueNormalized = normalizedValue,
                ValueDisplay = request.Value.Trim(),
                Reason = NormalizeReason(request.Reason),
                IsActive = true,
                CreatedByAdminId = adminId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.UserGovernanceBlacklistEntries.Add(existing);
        }
        else
        {
            existing.ValueDisplay = request.Value.Trim();
            existing.Reason = NormalizeReason(request.Reason);
            existing.IsActive = true;
            existing.RevokedAtUtc = null;
            existing.RevokedByAdminId = null;
            existing.RevokedReason = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapBlacklistItem(existing);
    }

    public async Task<SuperAdminGovernanceBlacklistItemDto> RevokeBlacklistAsync(
        Guid blacklistEntryId,
        string reason,
        Guid adminId,
        CancellationToken cancellationToken = default)
    {
        if (adminId == Guid.Empty)
        {
            throw new UserGovernanceException(
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for blacklist changes.",
                StatusCodes.Status403Forbidden);
        }

        var entry = await _dbContext.UserGovernanceBlacklistEntries
            .FirstOrDefaultAsync(item => item.Id == blacklistEntryId, cancellationToken);

        if (entry == null)
        {
            throw new UserGovernanceException(
                "BLACKLIST_ENTRY_NOT_FOUND",
                "Blacklist entry was not found.",
                StatusCodes.Status404NotFound);
        }

        entry.IsActive = false;
        entry.RevokedAtUtc = DateTime.UtcNow;
        entry.RevokedByAdminId = adminId;
        entry.RevokedReason = NormalizeReason(reason);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapBlacklistItem(entry);
    }

    private async Task<VendorGovernanceActionAlerts> LoadVendorGovernanceActionAlertsAsync(
        Guid vendorId,
        UserGovernanceStatus targetStatus,
        CancellationToken cancellationToken)
    {
        var activeOrderCount = await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder =>
                subOrder.StoreId == vendorId &&
                !subOrder.IsDeleted &&
                !subOrder.MarketplaceOrder.IsDeleted &&
                subOrder.Status != OrderStatus.Completed &&
                subOrder.Status != OrderStatus.Cancelled &&
                subOrder.Status != OrderStatus.Refunded)
            .CountAsync(cancellationToken);

        var pendingPayouts = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(payout =>
                payout.TenantId == vendorId &&
                !payout.IsDeleted &&
                (payout.Status == CommerceHubPayoutStatus.Pending ||
                 payout.Status == CommerceHubPayoutStatus.Processing ||
                 payout.Status == CommerceHubPayoutStatus.ReconciliationRequired))
            .Select(payout => payout.Amount)
            .ToListAsync(cancellationToken);

        var alerts = new VendorGovernanceActionAlerts(
            activeOrderCount,
            pendingPayouts.Count,
            RoundCurrency(pendingPayouts.Sum()));

        if (alerts.ActiveOrderCount > 0 &&
            targetStatus is UserGovernanceStatus.Suspended or UserGovernanceStatus.Banned)
        {
            _logger.LogWarning(
                "Vendor governance action applied while active CommerceHub orders remain | VendorId: {VendorId} | TargetStatus: {TargetStatus} | ActiveOrderCount: {ActiveOrderCount}",
                vendorId,
                targetStatus,
                alerts.ActiveOrderCount);
        }

        if (alerts.PendingPayoutCount > 0 && targetStatus == UserGovernanceStatus.Banned)
        {
            _logger.LogWarning(
                "Vendor ban applied while pending payouts remain | VendorId: {VendorId} | PendingPayoutCount: {PendingPayoutCount} | PendingPayoutAmount: {PendingPayoutAmount}",
                vendorId,
                alerts.PendingPayoutCount,
                alerts.PendingPayoutAmount);
        }

        return alerts;
    }

    private sealed record VendorGovernanceActionAlerts(
        int ActiveOrderCount,
        int PendingPayoutCount,
        decimal PendingPayoutAmount);
}
