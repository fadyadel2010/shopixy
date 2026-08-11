using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed record CertificateProvisioningSchedulerCandidate(
    Guid TenantId,
    Guid DomainId,
    Guid CertificateId,
    string IdempotencyKey,
    bool ProvisioningComplete);

public interface ICertificateProvisioningSchedulerCandidateQuery
{
    Task<IReadOnlyList<CertificateProvisioningSchedulerCandidate>> GetCandidatesAsync(
        DateTime nowUtc,
        CertificateProvisioningSchedulerOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class CertificateProvisioningSchedulerCandidateQuery : ICertificateProvisioningSchedulerCandidateQuery
{
    private readonly ApplicationDbContext _dbContext;

    public CertificateProvisioningSchedulerCandidateQuery(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CertificateProvisioningSchedulerCandidate>> GetCandidatesAsync(
        DateTime nowUtc,
        CertificateProvisioningSchedulerOptions options,
        CancellationToken cancellationToken = default)
    {
        var candidates = await (
            from operation in _dbContext.SslCertificateOperations.IgnoreQueryFilters().AsNoTracking()
            join certificate in _dbContext.SslCertificates.IgnoreQueryFilters().AsNoTracking()
                on new { operation.TenantId, Id = operation.CertificateId }
                equals new { certificate.TenantId, Id = (Guid?)certificate.Id }
            join domain in _dbContext.Domains.IgnoreQueryFilters().AsNoTracking()
                on new { operation.TenantId, operation.DomainId }
                equals new { domain.TenantId, DomainId = domain.Id }
            where !operation.IsDeleted && !certificate.IsDeleted && !domain.IsDeleted
                && operation.OperationType == CertificateOperationType.Provisioning
                && domain.VerifiedAtUtc != null
                && domain.LifecycleState == DomainLifecycleState.Verified
                && (
                    operation.Status == CertificateOperationStatus.Running
                    || (operation.Status == CertificateOperationStatus.Failed
                        && operation.Retryable
                        && (!operation.RetryAfterUtc.HasValue || operation.RetryAfterUtc <= nowUtc))
                    || (operation.Status == CertificateOperationStatus.Succeeded
                        && (certificate.Status == CertificateStatus.Issued
                            || certificate.Status == CertificateStatus.Activating
                            || (certificate.Status == CertificateStatus.Failed
                                && certificate.Retryable
                                && (!certificate.RetryAfterUtc.HasValue || certificate.RetryAfterUtc <= nowUtc)))))
            orderby operation.UpdatedAtUtc, operation.CreatedAtUtc
            select new CertificateProvisioningSchedulerCandidate(
                operation.TenantId,
                operation.DomainId,
                certificate.Id,
                operation.IdempotencyKey,
                operation.Status == CertificateOperationStatus.Succeeded))
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        return candidates;
    }
}
