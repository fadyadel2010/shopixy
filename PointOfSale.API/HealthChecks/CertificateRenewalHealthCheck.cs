using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.HealthChecks;

public sealed class CertificateRenewalHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOptions<CertificateRenewalSchedulerOptions> _options;

    public CertificateRenewalHealthCheck(
        ApplicationDbContext dbContext,
        IOptions<CertificateRenewalSchedulerOptions> options)
    {
        _dbContext = dbContext;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            return HealthCheckResult.Degraded("Certificate renewal scheduler is disabled.");
        }

        var now = DateTime.UtcNow;
        var warningCutoff = now.Add(options.ExpiryWarningThreshold);
        var urgentCutoff = now.Add(options.ExpiryUrgentThreshold);

        var dueCount = await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(c =>
                !c.IsDeleted &&
                c.Status == CertificateStatus.Active &&
                c.RenewalEligible &&
                ((c.NextRenewalUtc.HasValue && c.NextRenewalUtc.Value <= now) ||
                 (!c.NextRenewalUtc.HasValue && c.NotAfterUtc.HasValue && c.NotAfterUtc.Value <= warningCutoff)),
                cancellationToken);

        var urgentCount = await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(c =>
                !c.IsDeleted &&
                c.Status == CertificateStatus.Active &&
                c.NotAfterUtc.HasValue &&
                c.NotAfterUtc.Value <= urgentCutoff,
                cancellationToken);

        var unsafeAuthorityCount = await (
            from certificate in _dbContext.SslCertificates.IgnoreQueryFilters().AsNoTracking()
            join domain in _dbContext.Domains.IgnoreQueryFilters().AsNoTracking()
                on new { certificate.TenantId, certificate.DomainId } equals new { domain.TenantId, DomainId = domain.Id }
            where !certificate.IsDeleted &&
                  !domain.IsDeleted &&
                  domain.SslCertificateId == certificate.Id &&
                  (domain.LifecycleState == DomainLifecycleState.SslActive ||
                   domain.LifecycleState == DomainLifecycleState.Primary) &&
                  (certificate.Status == CertificateStatus.Revoked ||
                   certificate.Status == CertificateStatus.Expired ||
                   certificate.RevocationReason == CertificateRevocationReasonCodes.SecurityCompromise ||
                   certificate.RevocationReason == CertificateRevocationReasonCodes.CertificateCompromised ||
                   (certificate.NotAfterUtc.HasValue && certificate.NotAfterUtc.Value <= now))
            select certificate.Id)
            .CountAsync(cancellationToken);

        var exhaustedValidCount = await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(c =>
                !c.IsDeleted &&
                c.Status == CertificateStatus.Active &&
                c.FailureCode == CertificateRevocationReasonCodes.RenewalExhausted &&
                c.NotAfterUtc.HasValue &&
                c.NotAfterUtc.Value > now,
                cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["dueCount"] = dueCount,
            ["urgentCount"] = urgentCount,
            ["unsafeAuthorityCount"] = unsafeAuthorityCount,
            ["exhaustedValidCount"] = exhaustedValidCount,
            ["scanIntervalMinutes"] = options.ScanIntervalMinutes,
            ["renewalThresholdDays"] = options.RenewalThresholdDays,
        };

        if (unsafeAuthorityCount > 0)
        {
            return HealthCheckResult.Unhealthy("One or more authoritative certificates are expired, revoked, or compromised.", data: data);
        }

        if (urgentCount > 0)
        {
            return HealthCheckResult.Unhealthy("Certificate renewal backlog has urgent certificates.", data: data);
        }

        return dueCount > options.BatchSize || exhaustedValidCount > 0
            ? HealthCheckResult.Degraded("Certificate renewal backlog exceeds one scheduler batch.", data: data)
            : HealthCheckResult.Healthy("Certificate renewal backlog is within configured bounds.", data);
    }
}
