using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Infrastructure.Queries;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Reliability;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.Governance;

public sealed partial class UserGovernanceService
{
    private async Task<List<ResolvedSubject>> LoadSubjectsAsync(
        SuperAdminUserGovernanceListQuery query,
        CancellationToken cancellationToken)
    {
        var subjects = new List<ResolvedSubject>();
        var subjectCount = 0;
        var normalizedSearch = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : query.Search.Trim().ToLowerInvariant();

        if (!query.Role.HasValue || query.Role == UserGovernanceRole.Customer)
        {
            var customers = _dbContext.CommerceHubCustomers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(customer => !customer.IsDeleted);

            if (query.CreatedFromUtc.HasValue)
            {
                customers = customers.Where(customer => customer.CreatedAt >= query.CreatedFromUtc.Value);
            }

            if (query.CreatedToUtc.HasValue)
            {
                customers = customers.Where(customer => customer.CreatedAt <= query.CreatedToUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                customers = customers.Where(customer =>
                    customer.Name.ToLower().Contains(normalizedSearch) ||
                    customer.Email.ToLower().Contains(normalizedSearch) ||
                    (customer.PhoneNumber != null && customer.PhoneNumber.ToLower().Contains(normalizedSearch)));
            }

            subjectCount += await customers.CountAsync(cancellationToken);
            QuerySafetyGuard.EnsureWithinLimit(
                subjectCount,
                QuerySafetyGuard.MaxGovernanceSubjects,
                "User governance list request");

            subjects.AddRange(await customers
                .Select(customer => new ResolvedSubject(
                    customer.Id,
                    UserGovernanceRole.Customer,
                    customer.Name,
                    customer.Email,
                    customer.PhoneNumber,
                    null,
                    customer.CreatedAt,
                    null,
                    "EGP",
                    false))
                .ToListAsync(cancellationToken));
        }

        if (!query.Role.HasValue || query.Role == UserGovernanceRole.Vendor)
        {
            var vendors = _dbContext.Tenants
                .AsNoTracking()
                .Where(tenant => tenant.DeletedAtUtc == null);

            if (query.CreatedFromUtc.HasValue)
            {
                vendors = vendors.Where(tenant => tenant.CreatedAtUtc >= query.CreatedFromUtc.Value);
            }

            if (query.CreatedToUtc.HasValue)
            {
                vendors = vendors.Where(tenant => tenant.CreatedAtUtc <= query.CreatedToUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                vendors = vendors.Where(tenant =>
                    tenant.Name.ToLower().Contains(normalizedSearch) ||
                    (tenant.ContactEmail != null && tenant.ContactEmail.ToLower().Contains(normalizedSearch)) ||
                    (tenant.Slug != null && tenant.Slug.ToLower().Contains(normalizedSearch)));
            }

            subjectCount += await vendors.CountAsync(cancellationToken);
            QuerySafetyGuard.EnsureWithinLimit(
                subjectCount,
                QuerySafetyGuard.MaxGovernanceSubjects,
                "User governance list request");

            subjects.AddRange(await vendors
                .Select(tenant => new ResolvedSubject(
                    tenant.Id,
                    UserGovernanceRole.Vendor,
                    tenant.Name,
                    tenant.ContactEmail,
                    null,
                    tenant.Slug,
                    tenant.CreatedAtUtc,
                    null,
                    string.IsNullOrWhiteSpace(tenant.Currency) ? "EGP" : tenant.Currency!,
                    false))
                .ToListAsync(cancellationToken));
        }

        if (!query.Role.HasValue || query.Role == UserGovernanceRole.Admin)
        {
            var admins = _dbContext.SuperAdmins.AsNoTracking();

            if (query.CreatedFromUtc.HasValue)
            {
                admins = admins.Where(admin => admin.CreatedAt >= query.CreatedFromUtc.Value);
            }

            if (query.CreatedToUtc.HasValue)
            {
                admins = admins.Where(admin => admin.CreatedAt <= query.CreatedToUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                admins = admins.Where(admin => admin.Email.ToLower().Contains(normalizedSearch));
            }

            subjectCount += await admins.CountAsync(cancellationToken);
            QuerySafetyGuard.EnsureWithinLimit(
                subjectCount,
                QuerySafetyGuard.MaxGovernanceSubjects,
                "User governance list request");

            subjects.AddRange(await admins
                .Select(admin => new ResolvedSubject(
                    admin.Id,
                    UserGovernanceRole.Admin,
                    admin.Email,
                    admin.Email,
                    null,
                    null,
                    admin.CreatedAt,
                    admin.LastLoginAt,
                    "EGP",
                    admin.IsSystem))
                .ToListAsync(cancellationToken));
        }

        return subjects;
    }

    private static int CountBlacklistMatches(
        ResolvedSubject subject,
        UserGovernanceProfile? profile,
        ILookup<UserGovernanceBlacklistType, string> blacklistLookup)
    {
        var count = 0;
        var email = NormalizeBlacklistValue(UserGovernanceBlacklistType.Email, subject.Email);
        var phone = NormalizeBlacklistValue(UserGovernanceBlacklistType.PhoneNumber, subject.PhoneNumber);
        var ipAddress = NormalizeBlacklistValue(UserGovernanceBlacklistType.IpAddress, profile?.LastIpAddress);
        var deviceId = NormalizeBlacklistValue(UserGovernanceBlacklistType.DeviceId, profile?.DeviceFingerprint);

        if (!string.IsNullOrWhiteSpace(email) && blacklistLookup[UserGovernanceBlacklistType.Email].Contains(email, StringComparer.Ordinal))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(phone) && blacklistLookup[UserGovernanceBlacklistType.PhoneNumber].Contains(phone, StringComparer.Ordinal))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(ipAddress) && blacklistLookup[UserGovernanceBlacklistType.IpAddress].Contains(ipAddress, StringComparer.Ordinal))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(deviceId) && blacklistLookup[UserGovernanceBlacklistType.DeviceId].Contains(deviceId, StringComparer.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static SuperAdminGovernedUserListItemDto MapListItem(EvaluatedSubject evaluation)
    {
        return new SuperAdminGovernedUserListItemDto
        {
            Id = evaluation.Subject.Id,
            Role = evaluation.Subject.Role.ToString(),
            Name = evaluation.Subject.DisplayName,
            Email = evaluation.Subject.Email,
            PhoneNumber = evaluation.Subject.PhoneNumber,
            VendorSlug = evaluation.Subject.VendorSlug,
            Status = evaluation.Status.ToString(),
            RiskLevel = evaluation.RiskLevel.ToString(),
            IsKycVerified = evaluation.IsKycVerified,
            IsMarketplaceApproved = evaluation.IsMarketplaceApproved,
            HasDebt = evaluation.Metrics.VendorDebt > 0m,
            SuspiciousFlags = evaluation.Metrics.SuspiciousFlags,
            VendorDebt = evaluation.Metrics.VendorDebt,
            CreatedAtUtc = evaluation.CreatedAtUtc,
            LastLoginAtUtc = evaluation.LastLoginAtUtc
        };
    }

    private static SuperAdminGovernedUserDetailsDto MapDetails(
        EvaluatedSubject evaluation,
        IReadOnlyList<SuperAdminGovernedUserAuditItemDto> auditTimeline)
    {
        return new SuperAdminGovernedUserDetailsDto
        {
            Id = evaluation.Subject.Id,
            Role = evaluation.Subject.Role.ToString(),
            Status = evaluation.Status.ToString(),
            RiskLevel = evaluation.RiskLevel.ToString(),
            IsKycVerified = evaluation.IsKycVerified,
            IsMarketplaceApproved = evaluation.IsMarketplaceApproved,
            CreatedAtUtc = evaluation.CreatedAtUtc,
            LastLoginAtUtc = evaluation.LastLoginAtUtc,
            StatusReason = evaluation.StatusReason,
            BasicInfo = new SuperAdminGovernedUserBasicInfoDto
            {
                Name = evaluation.Subject.DisplayName,
                PhoneNumber = evaluation.Subject.PhoneNumber,
                Email = evaluation.Subject.Email,
                VendorSlug = evaluation.Subject.VendorSlug
            },
            FinancialSnapshot = new SuperAdminGovernedUserFinancialSnapshotDto
            {
                VendorDebt = evaluation.Metrics.VendorDebt,
                VendorPayable = evaluation.Metrics.VendorPayable,
                TotalRevenue = evaluation.Metrics.TotalRevenue,
                TotalRefunds = evaluation.Metrics.TotalRefunds,
                Currency = evaluation.Metrics.Currency
            },
            Activity = new SuperAdminGovernedUserActivityDto
            {
                OrdersCount = evaluation.Metrics.OrdersCount,
                RefundCount = evaluation.Metrics.RefundCount,
                ReturnCount = evaluation.Metrics.ReturnCount
            },
            Risk = MapRisk(evaluation),
            AccessInfo = new SuperAdminGovernedUserAccessInfoDto
            {
                LastIp = evaluation.LastIpAddress,
                DeviceFingerprint = evaluation.DeviceFingerprint
            },
            AuditTimeline = auditTimeline
        };
    }

    private static SuperAdminGovernedUserRiskDto MapRisk(EvaluatedSubject evaluation)
    {
        return new SuperAdminGovernedUserRiskDto
        {
            RiskLevel = evaluation.RiskLevel.ToString(),
            RefundRate = evaluation.Metrics.RefundRate,
            DisputeRate = evaluation.Metrics.DisputeRate,
            FailedPayments = evaluation.Metrics.FailedPayments,
            SuspiciousFlags = evaluation.Metrics.SuspiciousFlags,
            Signals = evaluation.Metrics.RiskSignals,
            Alerts = evaluation.Metrics.Alerts,
            EvaluatedAtUtc = DateTime.UtcNow
        };
    }

    private static SuperAdminGovernanceBlacklistItemDto MapBlacklistItem(UserGovernanceBlacklistEntry entry)
    {
        return new SuperAdminGovernanceBlacklistItemDto
        {
            Id = entry.Id,
            Type = entry.Type.ToString(),
            ValueDisplay = entry.ValueDisplay,
            Reason = entry.Reason,
            IsActive = entry.IsActive,
            CreatedByAdminId = entry.CreatedByAdminId,
            CreatedAtUtc = entry.CreatedAtUtc,
            RevokedByAdminId = entry.RevokedByAdminId,
            RevokedAtUtc = entry.RevokedAtUtc,
            RevokedReason = entry.RevokedReason
        };
    }

    private static void EnsureStatus(
        EvaluatedSubject evaluation,
        string suspendedCode,
        string suspendedMessage,
        string bannedCode,
        string bannedMessage)
    {
        if (evaluation.Status == UserGovernanceStatus.Banned)
        {
            throw new UserGovernanceException(
                bannedCode,
                bannedMessage,
                StatusCodes.Status403Forbidden);
        }

        if (evaluation.Status == UserGovernanceStatus.Suspended)
        {
            throw new UserGovernanceException(
                suspendedCode,
                suspendedMessage,
                StatusCodes.Status409Conflict);
        }
    }

    private static void EnsureMarketplaceEligibility(
        CommerceHubVendorAuthoritySnapshot authority,
        string operation)
    {
        if (!authority.TenantExists || !authority.TenantActive)
        {
            throw new UserGovernanceException(
                "VENDOR_TENANT_INACTIVE",
                $"Vendor tenant authority is inactive and cannot {operation}.",
                StatusCodes.Status403Forbidden);
        }

        if (!authority.GovernanceProfileExists)
        {
            throw new UserGovernanceException(
                "VENDOR_GOVERNANCE_PROFILE_REQUIRED",
                $"Vendor governance authority is missing and cannot {operation}.",
                StatusCodes.Status409Conflict);
        }

        if (authority.GovernanceStatus == UserGovernanceStatus.Banned)
        {
            throw new UserGovernanceException(
                "VENDOR_BANNED",
                $"Vendor is banned and cannot {operation}.",
                StatusCodes.Status403Forbidden);
        }

        if (authority.GovernanceStatus == UserGovernanceStatus.Suspended)
        {
            throw new UserGovernanceException(
                "VENDOR_SUSPENDED",
                $"Vendor is suspended and cannot {operation}.",
                StatusCodes.Status403Forbidden);
        }

        if (authority.GovernanceStatus == UserGovernanceStatus.FinanciallyLocked)
        {
            throw new UserGovernanceException(
                "VENDOR_FINANCIAL_LOCKED",
                $"Vendor is financially locked and cannot {operation}.",
                StatusCodes.Status409Conflict);
        }

        if (authority.GovernanceStatus != UserGovernanceStatus.Active)
        {
            throw new UserGovernanceException(
                "VENDOR_GOVERNANCE_RESTRICTED",
                $"Vendor governance state does not allow the vendor to {operation}.",
                StatusCodes.Status409Conflict);
        }

        if (!authority.IsKycVerified)
        {
            throw new UserGovernanceException(
                "VENDOR_VERIFICATION_REQUIRED",
                $"Vendor verification is required before the vendor can {operation}.",
                StatusCodes.Status409Conflict);
        }

        if (!authority.IsMarketplaceApproved)
        {
            throw new UserGovernanceException(
                "VENDOR_MARKETPLACE_APPROVAL_REQUIRED",
                $"Marketplace approval is required before the vendor can {operation}.",
                StatusCodes.Status409Conflict);
        }
    }

    private static string BuildActionType(UserGovernanceStatus targetStatus) =>
        targetStatus switch
        {
            UserGovernanceStatus.Active => "ACTIVATE",
            UserGovernanceStatus.Suspended => "SUSPEND",
            UserGovernanceStatus.Banned => "BAN",
            UserGovernanceStatus.FinanciallyLocked => "FINANCIAL_LOCK",
            _ => "UPDATE"
        };

    private static object BuildAuditSnapshot(EvaluatedSubject evaluation) =>
        new
        {
            status = evaluation.Status.ToString(),
            riskLevel = evaluation.RiskLevel.ToString(),
            evaluation.IsKycVerified,
            evaluation.IsMarketplaceApproved,
            evaluation.Metrics.SuspiciousFlags,
            evaluation.LastLoginAtUtc,
            evaluation.StatusReason,
            evaluation.LastIpAddress,
            evaluation.DeviceFingerprint
        };

    private static object BuildAuditSnapshot(UserGovernanceProfile profile) =>
        new
        {
            status = profile.Status.ToString(),
            riskLevel = profile.RiskLevel.ToString(),
            profile.IsKycVerified,
            profile.IsMarketplaceApproved,
            profile.SuspiciousFlags,
            profile.LastLoginAtUtc,
            profile.StatusReason,
            profile.LastIpAddress,
            profile.DeviceFingerprint
        };

    private string? GetClientIpAddress()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return null;
        }

        var clientIp = ClientIpAddressResolver.GetTrustedClientIp(httpContext, string.Empty);
        return string.IsNullOrWhiteSpace(clientIp) ? null : clientIp;
    }

    private string? GetDeviceFingerprint()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return null;
        }

        return httpContext.Request.Headers["X-Device-Fingerprint"].FirstOrDefault()
            ?? httpContext.Request.Headers["X-Device-Id"].FirstOrDefault();
    }

    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new UserGovernanceException(
                "REASON_REQUIRED",
                "A reason is required for governance actions.",
                StatusCodes.Status400BadRequest);
        }

        return reason.Trim();
    }

    private static string? NormalizeBlacklistValue(
        UserGovernanceBlacklistType type,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return type switch
        {
            UserGovernanceBlacklistType.Email => trimmed.ToLowerInvariant(),
            UserGovernanceBlacklistType.PhoneNumber => new string(trimmed.Where(char.IsDigit).ToArray()),
            UserGovernanceBlacklistType.IpAddress => trimmed.ToLowerInvariant(),
            UserGovernanceBlacklistType.DeviceId => trimmed.ToLowerInvariant(),
            _ => trimmed
        };
    }

    private static decimal CalculateRate(int numerator, int denominator)
    {
        if (denominator <= 0 || numerator <= 0)
        {
            return 0m;
        }

        return Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed record ResolvedSubject(
        Guid Id,
        UserGovernanceRole Role,
        string DisplayName,
        string? Email,
        string? PhoneNumber,
        string? VendorSlug,
        DateTime CreatedAtUtc,
        DateTime? SourceLastLoginAtUtc,
        string Currency,
        bool IsSystemAdmin);

    private sealed record GovernanceMetrics(
        decimal VendorDebt,
        decimal VendorPayable,
        decimal TotalRevenue,
        decimal TotalRefunds,
        int OrdersCount,
        int RefundCount,
        int ReturnCount,
        int FailedPayments,
        int RecentRefunds,
        int BlacklistMatches,
        int SuspiciousFlags,
        decimal RefundRate,
        decimal DisputeRate,
        string Currency,
        IReadOnlyList<string> RiskSignals,
        IReadOnlyList<string> Alerts);

    private sealed record EvaluatedSubject(
        ResolvedSubject Subject,
        UserGovernanceProfile? Profile,
        UserGovernanceStatus Status,
        UserGovernanceRiskLevel RiskLevel,
        bool IsKycVerified,
        bool IsMarketplaceApproved,
        DateTime CreatedAtUtc,
        DateTime? LastLoginAtUtc,
        string? StatusReason,
        string? LastIpAddress,
        string? DeviceFingerprint,
        GovernanceMetrics Metrics,
        bool ShouldAutoFinancialLock);

    private sealed record OrderAggregate(int Count, int FailedPayments, decimal TotalAmount)
    {
        public static OrderAggregate Empty { get; } = new(0, 0, 0m);
    }

    private sealed record RefundAggregate(int Count, decimal TotalAmount, int RecentCount)
    {
        public static RefundAggregate Empty { get; } = new(0, 0m, 0);
    }

    private sealed record ReturnAggregate(int Count)
    {
        public static ReturnAggregate Empty { get; } = new(0);
    }
}
