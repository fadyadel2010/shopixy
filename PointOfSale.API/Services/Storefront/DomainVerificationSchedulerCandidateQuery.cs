using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed record DomainVerificationSchedulerCandidate(
    Guid DomainId,
    Guid TenantId,
    DateTime PendingVerificationStartedAtUtc);

public interface IDomainVerificationSchedulerCandidateQuery
{
    Task<IReadOnlyList<DomainVerificationSchedulerCandidate>> GetCandidatesAsync(
        DateTime nowUtc,
        DomainVerificationSchedulerOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class DomainVerificationSchedulerCandidateQuery : IDomainVerificationSchedulerCandidateQuery
{
    private const int MaxFailedAttemptsPerHour = 5;

    private readonly ApplicationDbContext _dbContext;

    public DomainVerificationSchedulerCandidateQuery(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DomainVerificationSchedulerCandidate>> GetCandidatesAsync(
        DateTime nowUtc,
        DomainVerificationSchedulerOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var verificationWindowStartUtc = nowUtc.Subtract(options.MaxVerificationWindow);
        var recentAttemptCutoffUtc = nowUtc.Subtract(options.PollInterval);
        var failedAttemptCutoffUtc = nowUtc.AddHours(-1);
        var reverificationCutoffUtc = nowUtc.AddDays(-90);

        var candidates =
            from domain in _dbContext.Domains.IgnoreQueryFilters().AsNoTracking()
            join tenant in _dbContext.Tenants.IgnoreQueryFilters().AsNoTracking()
                on domain.TenantId equals tenant.Id
            let pendingStartedAtUtc = domain.UpdatedAtUtc ?? domain.CreatedAtUtc
            let hasRecentAttempt = _dbContext.DomainVerifications
                .IgnoreQueryFilters()
                .Any(verification =>
                    verification.DomainId == domain.Id &&
                    !verification.IsDeleted &&
                    verification.AttemptedAtUtc >= recentAttemptCutoffUtc)
            let failedAttemptsLastHour = _dbContext.DomainVerifications
                .IgnoreQueryFilters()
                .Count(verification =>
                    verification.DomainId == domain.Id &&
                    !verification.IsDeleted &&
                    verification.Status == VerificationStatus.Failed &&
                    verification.AttemptedAtUtc > failedAttemptCutoffUtc)
            let isPendingCandidate = domain.LifecycleState == DomainLifecycleState.PendingVerification &&
                pendingStartedAtUtc >= verificationWindowStartUtc
            let isReverificationCandidate = domain.VerifiedAtUtc.HasValue &&
                domain.VerifiedAtUtc.Value <= reverificationCutoffUtc &&
                (domain.LifecycleState == DomainLifecycleState.Verified ||
                 domain.LifecycleState == DomainLifecycleState.SslPending ||
                 domain.LifecycleState == DomainLifecycleState.SslActive ||
                 domain.LifecycleState == DomainLifecycleState.Primary)
            where (isPendingCandidate || isReverificationCandidate) &&
                  !domain.IsDeleted &&
                  domain.TenantId != Guid.Empty &&
                  tenant.IsActive &&
                  tenant.Status == TenantStatus.Active &&
                  tenant.DeletedAtUtc == null &&
                  !hasRecentAttempt &&
                  failedAttemptsLastHour < MaxFailedAttemptsPerHour
            orderby (domain.VerifiedAtUtc ?? pendingStartedAtUtc), domain.Id
            select new DomainVerificationSchedulerCandidate(
                domain.Id,
                domain.TenantId,
                pendingStartedAtUtc);

        return await candidates
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);
    }
}
