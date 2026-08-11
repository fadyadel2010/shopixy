using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Storefront;

[DisableConcurrentExecution(timeoutInSeconds: 300)]
[AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class CertificateProvisioningSchedulerJob
{
    public const string RecurringJobId = "certificate-provisioning-reconciler";

    private readonly ICertificateProvisioningSchedulerCandidateQuery _candidateQuery;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<CertificateProvisioningSchedulerOptions> _options;
    private readonly ILogger<CertificateProvisioningSchedulerJob> _logger;

    public CertificateProvisioningSchedulerJob(
        ICertificateProvisioningSchedulerCandidateQuery candidateQuery,
        IServiceScopeFactory scopeFactory,
        IOptions<CertificateProvisioningSchedulerOptions> options,
        ILogger<CertificateProvisioningSchedulerJob> logger)
    {
        _candidateQuery = candidateQuery;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task ExecuteAsync(PerformContext? context = null, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        var candidates = await _candidateQuery.GetCandidatesAsync(DateTime.UtcNow, settings, cancellationToken);
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.MaxConcurrentOperations,
                CancellationToken = cancellationToken,
            },
            async (candidate, candidateCancellationToken) =>
            {
                var correlationId = $"cert-reconcile:{candidate.DomainId:N}:{candidate.CertificateId:N}";
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var shouldActivate = candidate.ProvisioningComplete;

                    if (!shouldActivate)
                    {
                        var provisioning = scope.ServiceProvider.GetRequiredService<ICertificateProvisioningService>();
                        var result = await provisioning.ProvisionAsync(
                            candidate.DomainId,
                            new CertificateProvisioningRequest { IdempotencyKey = candidate.IdempotencyKey },
                            candidate.TenantId,
                            actorUserId: null,
                            correlationId,
                            candidateCancellationToken);
                        shouldActivate = result.CertificateStatus == CertificateStatus.Issued;
                    }

                    if (shouldActivate)
                    {
                        var activation = scope.ServiceProvider.GetRequiredService<ICertificateActivationService>();
                        await activation.ActivateAsync(
                            candidate.DomainId,
                            new CertificateActivationRequest
                            {
                                CertificateId = candidate.CertificateId,
                                IdempotencyKey = $"auto-activate:{candidate.CertificateId:N}",
                            },
                            candidate.TenantId,
                            actorUserId: null,
                            correlationId,
                            candidateCancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Certificate provisioning reconciliation failed | CorrelationId: {CorrelationId} | DomainId: {DomainId} | CertificateId: {CertificateId}",
                        correlationId,
                        candidate.DomainId,
                        candidate.CertificateId);
                }
            });
    }
}
