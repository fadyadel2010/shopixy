using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.Governance;

public sealed partial class UserGovernanceService
{
    private async Task<EvaluatedSubject> RequireEvaluationAsync(
        Guid subjectId,
        UserGovernanceRole role,
        bool allowAutoFinancialLock,
        CancellationToken cancellationToken)
    {
        var evaluation = await EvaluateSingleSubjectAsync(
            subjectId,
            role,
            persistProfile: true,
            allowAutoFinancialLock: allowAutoFinancialLock,
            cancellationToken);

        return evaluation
            ?? throw new UserGovernanceException(
                "USER_NOT_FOUND",
                "Governed user was not found.",
                StatusCodes.Status404NotFound);
    }

    private async Task<EvaluatedSubject?> EvaluateSingleSubjectAsync(
        Guid subjectId,
        UserGovernanceRole? role,
        bool persistProfile,
        bool allowAutoFinancialLock,
        CancellationToken cancellationToken)
    {
        var subject = await ResolveSubjectAsync(subjectId, role, cancellationToken);
        if (subject == null)
        {
            return null;
        }

        var evaluations = await EvaluateSubjectsAsync([subject], persistProfile, allowAutoFinancialLock, cancellationToken);
        return evaluations.SingleOrDefault();
    }

    private async Task<List<EvaluatedSubject>> EvaluateSubjectsAsync(
        IReadOnlyCollection<ResolvedSubject> subjects,
        bool persistProfiles,
        bool allowAutoFinancialLock,
        CancellationToken cancellationToken)
    {
        if (subjects.Count == 0)
        {
            return new List<EvaluatedSubject>();
        }

        var subjectIds = subjects
            .Select(subject => subject.Id)
            .Distinct()
            .ToList();
        var subjectIdSet = subjectIds.ToHashSet();

        var persistedProfiles = await _dbContext.UserGovernanceProfiles
            .Where(profile => subjectIds.Contains(profile.SubjectId))
            .ToListAsync(cancellationToken);
        var trackedProfiles = _dbContext.UserGovernanceProfiles.Local
            .Where(profile =>
                subjectIdSet.Contains(profile.SubjectId) &&
                _dbContext.Entry(profile).State != EntityState.Deleted &&
                _dbContext.Entry(profile).State != EntityState.Detached)
            .ToList();
        var profiles = persistedProfiles
            .Concat(trackedProfiles)
            .GroupBy(profile => profile.Id)
            .Select(group => group.First())
            .ToList();

        var profilesByKey = profiles
            .GroupBy(profile => (profile.Role, profile.SubjectId))
            .ToDictionary(
                group => group.Key,
                group => SelectPreferredProfile(group)!);
        if (persistProfiles)
        {
            foreach (var subject in subjects)
            {
                if (profilesByKey.ContainsKey((subject.Role, subject.Id)))
                {
                    continue;
                }

                var profile = new UserGovernanceProfile
                {
                    Id = Guid.NewGuid(),
                    SubjectId = subject.Id,
                    Role = subject.Role,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.UserGovernanceProfiles.Add(profile);
                profilesByKey[(subject.Role, subject.Id)] = profile;
            }
        }

        var customerIds = subjects
            .Where(subject => subject.Role == UserGovernanceRole.Customer)
            .Select(subject => subject.Id)
            .ToList();
        var vendorIds = subjects
            .Where(subject => subject.Role == UserGovernanceRole.Vendor)
            .Select(subject => subject.Id)
            .ToList();

        var customerOrderAggregates = await LoadCustomerOrderAggregatesAsync(customerIds, cancellationToken);
        var customerRefundAggregates = await LoadCustomerRefundAggregatesAsync(customerIds, cancellationToken);
        var customerReturnAggregates = await LoadCustomerReturnAggregatesAsync(customerIds, cancellationToken);
        var vendorOrderAggregates = await LoadVendorOrderAggregatesAsync(vendorIds, cancellationToken);
        var vendorRefundAggregates = await LoadVendorRefundAggregatesAsync(vendorIds, cancellationToken);
        var vendorReturnAggregates = await LoadVendorReturnAggregatesAsync(vendorIds, cancellationToken);
        var vendorDebtSnapshots = await _vendorDebtService.GetVendorDebtSnapshotsAsync(vendorIds, cancellationToken);
        var vendorPayables = await LoadVendorPayablesAsync(vendorIds, cancellationToken);
        var vendorLastLogins = await LoadVendorLastLoginsAsync(vendorIds, cancellationToken);
        var blacklistLookup = await LoadActiveBlacklistLookupAsync(cancellationToken);

        var evaluations = new List<EvaluatedSubject>(subjects.Count);
        foreach (var subject in subjects)
        {
            profilesByKey.TryGetValue((subject.Role, subject.Id), out var profile);
            var evaluation = BuildEvaluation(
                subject,
                profile,
                customerOrderAggregates,
                customerRefundAggregates,
                customerReturnAggregates,
                vendorOrderAggregates,
                vendorRefundAggregates,
                vendorReturnAggregates,
                vendorDebtSnapshots,
                vendorPayables,
                vendorLastLogins,
                blacklistLookup);

            if (persistProfiles && profile != null)
            {
                var changed = SyncProfileWithEvaluation(profile, evaluation);
                if (allowAutoFinancialLock &&
                    subject.Role == UserGovernanceRole.Vendor &&
                    profile.Status == UserGovernanceStatus.Active &&
                    evaluation.ShouldAutoFinancialLock)
                {
                    var beforeSnapshot = BuildAuditSnapshot(profile);
                    profile.Status = UserGovernanceStatus.FinanciallyLocked;
                    profile.StatusReason = "Automatically locked because vendor debt exceeded the configured risk threshold.";
                    profile.StatusChangedAtUtc = DateTime.UtcNow;
                    profile.UpdatedAtUtc = DateTime.UtcNow;

                    await AppendAuditLogAsync(
                        subject,
                        actorId: null,
                        actorType: "SYSTEM",
                        actionType: "AUTO_FINANCIAL_LOCK",
                        reason: profile.StatusReason,
                        beforeState: beforeSnapshot,
                        afterState: BuildAuditSnapshot(profile),
                        metadata: new
                        {
                            evaluation.Metrics.VendorDebt,
                            evaluation.Metrics.RiskSignals
                        },
                        cancellationToken);

                    changed = true;
                }

                if (changed)
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    evaluation = evaluation with
                    {
                        Status = profile.Status,
                        RiskLevel = profile.RiskLevel,
                        LastLoginAtUtc = profile.LastLoginAtUtc ?? evaluation.LastLoginAtUtc,
                        StatusReason = profile.StatusReason,
                        LastIpAddress = profile.LastIpAddress,
                        DeviceFingerprint = profile.DeviceFingerprint
                    };
                }
            }

            evaluations.Add(evaluation);
        }

        return evaluations;
    }

    private static EvaluatedSubject BuildEvaluation(
        ResolvedSubject subject,
        UserGovernanceProfile? profile,
        IReadOnlyDictionary<Guid, OrderAggregate> customerOrderAggregates,
        IReadOnlyDictionary<Guid, RefundAggregate> customerRefundAggregates,
        IReadOnlyDictionary<Guid, ReturnAggregate> customerReturnAggregates,
        IReadOnlyDictionary<Guid, OrderAggregate> vendorOrderAggregates,
        IReadOnlyDictionary<Guid, RefundAggregate> vendorRefundAggregates,
        IReadOnlyDictionary<Guid, ReturnAggregate> vendorReturnAggregates,
        IReadOnlyDictionary<Guid, CommerceHubVendorDebtSnapshot> vendorDebtSnapshots,
        IReadOnlyDictionary<Guid, decimal> vendorPayables,
        IReadOnlyDictionary<Guid, DateTime?> vendorLastLogins,
        ILookup<UserGovernanceBlacklistType, string> blacklistLookup)
    {
        var orderAggregate = subject.Role switch
        {
            UserGovernanceRole.Customer => customerOrderAggregates.GetValueOrDefault(subject.Id) ?? OrderAggregate.Empty,
            UserGovernanceRole.Vendor => vendorOrderAggregates.GetValueOrDefault(subject.Id) ?? OrderAggregate.Empty,
            _ => OrderAggregate.Empty
        };

        var refundAggregate = subject.Role switch
        {
            UserGovernanceRole.Customer => customerRefundAggregates.GetValueOrDefault(subject.Id) ?? RefundAggregate.Empty,
            UserGovernanceRole.Vendor => vendorRefundAggregates.GetValueOrDefault(subject.Id) ?? RefundAggregate.Empty,
            _ => RefundAggregate.Empty
        };

        var returnAggregate = subject.Role switch
        {
            UserGovernanceRole.Customer => customerReturnAggregates.GetValueOrDefault(subject.Id) ?? ReturnAggregate.Empty,
            UserGovernanceRole.Vendor => vendorReturnAggregates.GetValueOrDefault(subject.Id) ?? ReturnAggregate.Empty,
            _ => ReturnAggregate.Empty
        };

        var vendorDebtSnapshot = subject.Role == UserGovernanceRole.Vendor
            ? vendorDebtSnapshots.GetValueOrDefault(subject.Id)
            : null;
        var vendorDebt = vendorDebtSnapshot?.AmountOwedByVendor ?? 0m;
        var vendorPayable = subject.Role == UserGovernanceRole.Vendor
            ? vendorPayables.GetValueOrDefault(subject.Id)
            : 0m;

        var refundRate = CalculateRate(refundAggregate.Count, orderAggregate.Count);
        var disputeRate = CalculateRate(returnAggregate.Count, orderAggregate.Count);
        var signals = new List<string>();
        var alerts = new List<string>();
        var blacklistMatches = CountBlacklistMatches(subject, profile, blacklistLookup);

        if (refundRate > HighRefundRateThreshold)
        {
            signals.Add("Refund rate exceeds 40%.");
            alerts.Add("Rapid customer recovery risk detected from elevated refund volume.");
        }

        if (subject.Role == UserGovernanceRole.Vendor &&
            vendorDebtSnapshot != null &&
            vendorDebtSnapshot.AmountOwedByVendor >= vendorDebtSnapshot.DebtThresholdAmount &&
            vendorDebtSnapshot.DebtThresholdAmount > 0m)
        {
            signals.Add("Vendor debt exceeds the financial risk threshold.");
            alerts.Add("Vendor debt crossed the threshold and is eligible for automatic financial lock.");
        }

        if (orderAggregate.FailedPayments >= MediumFailedPaymentsThreshold)
        {
            signals.Add("Multiple failed payments detected.");
        }

        if (refundAggregate.RecentCount >= HighVelocityRefundThreshold)
        {
            signals.Add("Rapid refund activity detected.");
            alerts.Add("Refund velocity alert triggered.");
        }

        if (disputeRate >= HighDisputeRateThreshold && returnAggregate.Count > 0)
        {
            signals.Add("Dispute rate is elevated.");
        }

        if (blacklistMatches > 0)
        {
            signals.Add("One or more active blacklist signals match this user.");
            alerts.Add("Blacklist match requires manual review.");
        }

        var riskLevel = UserGovernanceRiskLevel.Low;
        if (signals.Any(signal =>
                signal.Contains("Refund rate", StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("debt exceeds", StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("Rapid refund", StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("blacklist", StringComparison.OrdinalIgnoreCase)))
        {
            riskLevel = UserGovernanceRiskLevel.High;
        }
        else if (orderAggregate.FailedPayments >= MediumFailedPaymentsThreshold ||
                 disputeRate >= HighDisputeRateThreshold)
        {
            riskLevel = UserGovernanceRiskLevel.Medium;
        }

        var lastLoginAtUtc = profile?.LastLoginAtUtc ??
            (subject.Role == UserGovernanceRole.Vendor
                ? vendorLastLogins.GetValueOrDefault(subject.Id)
                : subject.SourceLastLoginAtUtc);

        var metrics = new GovernanceMetrics(
            VendorDebt: RoundCurrency(vendorDebt),
            VendorPayable: RoundCurrency(vendorPayable),
            TotalRevenue: RoundCurrency(orderAggregate.TotalAmount),
            TotalRefunds: RoundCurrency(refundAggregate.TotalAmount),
            OrdersCount: orderAggregate.Count,
            RefundCount: refundAggregate.Count,
            ReturnCount: returnAggregate.Count,
            FailedPayments: orderAggregate.FailedPayments,
            RecentRefunds: refundAggregate.RecentCount,
            BlacklistMatches: blacklistMatches,
            SuspiciousFlags: signals.Count,
            RefundRate: refundRate,
            DisputeRate: disputeRate,
            Currency: subject.Currency,
            RiskSignals: signals.AsReadOnly(),
            Alerts: alerts.AsReadOnly());

        return new EvaluatedSubject(
            Subject: subject,
            Profile: profile,
            Status: profile?.Status ?? UserGovernanceStatus.Active,
            RiskLevel: riskLevel,
            IsKycVerified: profile?.IsKycVerified ?? false,
            IsMarketplaceApproved: profile?.IsMarketplaceApproved ?? false,
            CreatedAtUtc: subject.CreatedAtUtc,
            LastLoginAtUtc: lastLoginAtUtc,
            StatusReason: profile?.StatusReason,
            LastIpAddress: profile?.LastIpAddress,
            DeviceFingerprint: profile?.DeviceFingerprint,
            Metrics: metrics,
            ShouldAutoFinancialLock:
                subject.Role == UserGovernanceRole.Vendor &&
                vendorDebtSnapshot != null &&
                vendorDebtSnapshot.AmountOwedByVendor >= vendorDebtSnapshot.DebtThresholdAmount &&
                vendorDebtSnapshot.DebtThresholdAmount > 0m);
    }

    private bool SyncProfileWithEvaluation(UserGovernanceProfile profile, EvaluatedSubject evaluation)
    {
        var changed = false;
        if (profile.RiskLevel != evaluation.RiskLevel)
        {
            profile.RiskLevel = evaluation.RiskLevel;
            changed = true;
        }

        if (profile.SuspiciousFlags != evaluation.Metrics.SuspiciousFlags)
        {
            profile.SuspiciousFlags = evaluation.Metrics.SuspiciousFlags;
            changed = true;
        }

        var riskReason = string.Join(" | ", evaluation.Metrics.RiskSignals);
        if (!string.Equals(profile.LastRiskReason, riskReason, StringComparison.Ordinal))
        {
            profile.LastRiskReason = string.IsNullOrWhiteSpace(riskReason) ? null : riskReason;
            changed = true;
        }

        profile.LastRiskEvaluatedAtUtc = DateTime.UtcNow;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        return changed;
    }

    private async Task<ResolvedSubject?> ResolveSubjectAsync(
        Guid subjectId,
        UserGovernanceRole? role,
        CancellationToken cancellationToken)
    {
        if (subjectId == Guid.Empty)
        {
            return null;
        }

        if (!role.HasValue || role == UserGovernanceRole.Customer)
        {
            var customer = await _dbContext.CommerceHubCustomers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.Id == subjectId && !item.IsDeleted)
                .Select(item => new ResolvedSubject(
                    item.Id,
                    UserGovernanceRole.Customer,
                    item.Name,
                    item.Email,
                    item.PhoneNumber,
                    null,
                    item.CreatedAt,
                    null,
                    "EGP",
                    false))
                .SingleOrDefaultAsync(cancellationToken);

            if (customer != null || role == UserGovernanceRole.Customer)
            {
                return customer;
            }
        }

        if (!role.HasValue || role == UserGovernanceRole.Vendor)
        {
            var vendor = await _dbContext.Tenants
                .AsNoTracking()
                .Where(item => item.Id == subjectId && item.DeletedAtUtc == null)
                .Select(item => new ResolvedSubject(
                    item.Id,
                    UserGovernanceRole.Vendor,
                    item.Name,
                    item.ContactEmail,
                    null,
                    item.Slug,
                    item.CreatedAtUtc,
                    null,
                    string.IsNullOrWhiteSpace(item.Currency) ? "EGP" : item.Currency!,
                    false))
                .SingleOrDefaultAsync(cancellationToken);

            if (vendor != null || role == UserGovernanceRole.Vendor)
            {
                return vendor;
            }
        }

        if (!role.HasValue || role == UserGovernanceRole.Admin)
        {
            return await _dbContext.SuperAdmins
                .AsNoTracking()
                .Where(item => item.Id == subjectId)
                .Select(item => new ResolvedSubject(
                    item.Id,
                    UserGovernanceRole.Admin,
                    item.Email,
                    item.Email,
                    null,
                    null,
                    item.CreatedAt,
                    item.LastLoginAt,
                    "EGP",
                    item.IsSystem))
                .SingleOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    private async Task EnsureSubjectExistsAsync(
        UserGovernanceRole role,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        var subject = await ResolveSubjectAsync(subjectId, role, cancellationToken);
        if (subject == null)
        {
            throw new UserGovernanceException(
                "USER_NOT_FOUND",
                "Governed user was not found.",
                StatusCodes.Status404NotFound);
        }
    }

    private async Task<UserGovernanceProfile> GetOrCreateTrackedProfileAsync(
        UserGovernanceRole role,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        var localTrackedProfile = SelectPreferredProfile(
            _dbContext.UserGovernanceProfiles.Local.Where(profile =>
                profile.Role == role &&
                profile.SubjectId == subjectId &&
                _dbContext.Entry(profile).State != EntityState.Added));
        if (localTrackedProfile != null)
        {
            return localTrackedProfile;
        }

        var existing = SelectPreferredProfile(await _dbContext.UserGovernanceProfiles
            .Where(profile => profile.Role == role && profile.SubjectId == subjectId)
            .ToListAsync(cancellationToken));

        if (existing != null)
        {
            return existing;
        }

        var pendingProfile = SelectPreferredProfile(
            _dbContext.UserGovernanceProfiles.Local.Where(profile =>
                profile.Role == role &&
                profile.SubjectId == subjectId));
        if (pendingProfile != null)
        {
            return pendingProfile;
        }

        var profile = new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Role = role,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.UserGovernanceProfiles.Add(profile);
        return profile;
    }

    private UserGovernanceProfile? SelectPreferredProfile(IEnumerable<UserGovernanceProfile> profiles)
    {
        return profiles
            .Where(profile =>
            {
                var state = _dbContext.Entry(profile).State;
                return state != EntityState.Deleted && state != EntityState.Detached;
            })
            .OrderBy(GetProfileSelectionRank)
            .ThenByDescending(profile => profile.UpdatedAtUtc ?? profile.CreatedAtUtc)
            .ThenByDescending(profile => profile.Id)
            .FirstOrDefault();
    }

    private int GetProfileSelectionRank(UserGovernanceProfile profile)
    {
        return _dbContext.Entry(profile).State == EntityState.Added ? 1 : 0;
    }

    private async Task AppendAuditLogAsync(
        ResolvedSubject subject,
        Guid? actorId,
        string actorType,
        string actionType,
        string reason,
        object beforeState,
        object afterState,
        object? metadata,
        CancellationToken cancellationToken)
    {
        _dbContext.UserGovernanceAuditLogs.Add(new UserGovernanceAuditLog
        {
            Id = Guid.NewGuid(),
            TargetUserId = subject.Id,
            TargetRole = (int)subject.Role,
            AdminId = actorId,
            ActorType = actorType,
            ActionType = actionType,
            Reason = reason,
            BeforeState = JsonSerializer.Serialize(beforeState, JsonOptions),
            AfterState = JsonSerializer.Serialize(afterState, JsonOptions),
            Metadata = metadata == null ? null : JsonSerializer.Serialize(metadata, JsonOptions),
            TimestampUtc = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }
}
