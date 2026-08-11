using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed record CertificateRenewalSchedulerCandidate(
    Guid TenantId,
    Guid DomainId,
    Guid CertificateId,
    ProviderType ProviderType,
    int Generation,
    string NormalizedHostName,
    DateTime? NextRenewalUtc,
    DateTime? NotAfterUtc,
    string ExpirySeverity);

public interface ICertificateRenewalSchedulerCandidateQuery
{
    Task<IReadOnlyList<CertificateRenewalSchedulerCandidate>> GetCandidatesAsync(
        DateTime nowUtc,
        CertificateRenewalSchedulerOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class CertificateRenewalSchedulerCandidateQuery : ICertificateRenewalSchedulerCandidateQuery
{
    private readonly ApplicationDbContext _dbContext;

    public CertificateRenewalSchedulerCandidateQuery(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CertificateRenewalSchedulerCandidate>> GetCandidatesAsync(
        DateTime nowUtc,
        CertificateRenewalSchedulerOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var renewalCutoffUtc = nowUtc.Add(options.RenewalThreshold);
        var urgentCutoffUtc = nowUtc.Add(options.ExpiryUrgentThreshold);
        var warningCutoffUtc = nowUtc.Add(options.ExpiryWarningThreshold);

        var candidates =
            from certificate in _dbContext.SslCertificates.IgnoreQueryFilters().AsNoTracking()
            join domain in _dbContext.Domains.IgnoreQueryFilters().AsNoTracking()
                on new { certificate.TenantId, certificate.DomainId } equals new { domain.TenantId, DomainId = domain.Id }
            join tenant in _dbContext.Tenants.IgnoreQueryFilters().AsNoTracking()
                on domain.TenantId equals tenant.Id
            where !certificate.IsDeleted &&
                  !domain.IsDeleted &&
                  (domain.SslCertificateId == certificate.Id ||
                   (certificate.Status == CertificateStatus.Expired &&
                    certificate.FailureCode == CertificateRevocationReasonCodes.Expired)) &&
                  (domain.LifecycleState == DomainLifecycleState.SslActive ||
                   domain.LifecycleState == DomainLifecycleState.Primary ||
                   (certificate.Status == CertificateStatus.Expired &&
                    domain.LifecycleState == DomainLifecycleState.Verified))
            where certificate.TenantId != Guid.Empty &&
                  tenant.IsActive &&
                  tenant.Status == TenantStatus.Active &&
                  tenant.DeletedAtUtc == null &&
                  certificate.Provider != ProviderType.CustomUpload &&
                  (
                      (certificate.Status == CertificateStatus.Active &&
                       certificate.RenewalEligible &&
                       certificate.FailureCode != CertificateRevocationReasonCodes.RenewalExhausted &&
                       ((certificate.NextRenewalUtc.HasValue && certificate.NextRenewalUtc.Value <= nowUtc) ||
                        (!certificate.NextRenewalUtc.HasValue && certificate.NotAfterUtc.HasValue && certificate.NotAfterUtc.Value <= renewalCutoffUtc) ||
                        (certificate.RetryAfterUtc.HasValue && certificate.RetryAfterUtc.Value <= nowUtc))) ||
                      (certificate.Status == CertificateStatus.Expired &&
                       certificate.FailureCode == CertificateRevocationReasonCodes.Expired &&
                       (!certificate.RetryAfterUtc.HasValue || certificate.RetryAfterUtc.Value <= nowUtc))
                  )
            orderby certificate.NextRenewalUtc ?? certificate.NotAfterUtc ?? DateTime.MaxValue, certificate.Id
            select new CertificateRenewalSchedulerCandidate(
                certificate.TenantId,
                domain.Id,
                certificate.Id,
                certificate.Provider,
                certificate.Generation,
                certificate.NormalizedHostName,
                certificate.NextRenewalUtc,
                certificate.NotAfterUtc,
                certificate.Status == CertificateStatus.Expired
                    ? "expired_recovery"
                    : certificate.NotAfterUtc.HasValue && certificate.NotAfterUtc.Value <= nowUtc
                    ? "expired"
                    : certificate.NotAfterUtc.HasValue && certificate.NotAfterUtc.Value <= urgentCutoffUtc
                        ? "urgent"
                        : certificate.NotAfterUtc.HasValue && certificate.NotAfterUtc.Value <= warningCutoffUtc
                            ? "warning"
                            : "renewal_due");

        return await candidates
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);
    }
}
