using System.Diagnostics;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;

namespace PointOfSale.API.Services.Storefront;

[DisableConcurrentExecution(timeoutInSeconds: 300)]
[AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class CertificateRenewalSchedulerJob
{
    public const string RecurringJobId = "certificate-renewal-monitor";

    private readonly ICertificateRenewalSchedulerCandidateQuery _candidateQuery;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<CertificateRenewalSchedulerOptions> _options;
    private readonly CertificateRenewalTelemetry _telemetry;
    private readonly ILogger<CertificateRenewalSchedulerJob> _logger;

    public CertificateRenewalSchedulerJob(
        ICertificateRenewalSchedulerCandidateQuery candidateQuery,
        IServiceScopeFactory scopeFactory,
        IOptions<CertificateRenewalSchedulerOptions> options,
        CertificateRenewalTelemetry telemetry,
        ILogger<CertificateRenewalSchedulerJob> logger)
    {
        _candidateQuery = candidateQuery;
        _scopeFactory = scopeFactory;
        _options = options;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        PerformContext? performContext = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        var executionId = Guid.NewGuid().ToString("N");
        var jobId = performContext?.BackgroundJob?.Id ?? RecurringJobId;
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "success";
        var totalCandidates = 0;
        var processed = 0;
        var succeeded = 0;
        var failed = 0;

        try
        {
            if (!settings.Enabled)
            {
                outcome = "disabled";
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var candidates = await _candidateQuery.GetCandidatesAsync(DateTime.UtcNow, settings, cancellationToken);
                totalCandidates += candidates.Count;
                _telemetry.RecordScan(candidates.Count);

                if (candidates.Count == 0)
                {
                    break;
                }

                var batch = await ProcessBatchAsync(candidates, executionId, settings.MaxConcurrentRenewals, cancellationToken);
                processed += batch.Processed;
                succeeded += batch.Succeeded;
                failed += batch.Failed;

                if (candidates.Count < settings.BatchSize)
                {
                    break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            _logger.LogWarning(
                "Certificate renewal scheduler cancelled | JobId: {JobId} | CorrelationId: {CorrelationId} | CandidatesFound: {CandidatesFound} | Processed: {Processed} | Succeeded: {Succeeded} | Failed: {Failed}",
                jobId,
                executionId,
                totalCandidates,
                processed,
                succeeded,
                failed);
            throw;
        }
        catch (Exception ex)
        {
            outcome = "infrastructure_failure";
            _logger.LogError(
                ex,
                "Certificate renewal scheduler infrastructure failure | JobId: {JobId} | CorrelationId: {CorrelationId} | CandidatesFound: {CandidatesFound} | Processed: {Processed} | Succeeded: {Succeeded} | Failed: {Failed}",
                jobId,
                executionId,
                totalCandidates,
                processed,
                succeeded,
                failed);
            throw;
        }
        finally
        {
            _telemetry.RecordDuration(Stopwatch.GetElapsedTime(startedAt), outcome);
            _logger.LogInformation(
                "Certificate renewal scheduler completed | JobId: {JobId} | CorrelationId: {CorrelationId} | Outcome: {Outcome} | CandidatesFound: {CandidatesFound} | Processed: {Processed} | Succeeded: {Succeeded} | Failed: {Failed}",
                jobId,
                executionId,
                outcome,
                totalCandidates,
                processed,
                succeeded,
                failed);
        }
    }

    private async Task<BatchResult> ProcessBatchAsync(
        IReadOnlyList<CertificateRenewalSchedulerCandidate> candidates,
        string schedulerCorrelationId,
        int maxConcurrentRenewals,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        var succeeded = 0;
        var failed = 0;

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrentRenewals,
                CancellationToken = cancellationToken,
            },
            async (candidate, candidateCancellationToken) =>
            {
                var candidateCorrelationId = $"{schedulerCorrelationId}:{candidate.DomainId:N}:{candidate.CertificateId:N}";
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var renewalService = scope.ServiceProvider.GetRequiredService<ICertificateRenewalService>();
                    var result = await renewalService.RenewAsync(candidate, candidateCorrelationId, candidateCancellationToken);
                    Interlocked.Increment(ref processed);
                    _telemetry.RecordStarted(result);
                    var isRecovery = candidate.ExpirySeverity.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                        candidate.ExpirySeverity.Contains("recovery", StringComparison.OrdinalIgnoreCase);
                    if (isRecovery)
                    {
                        _telemetry.RecordRecoveryStarted(result);
                    }

                    if (result.Succeeded)
                    {
                        Interlocked.Increment(ref succeeded);
                        _telemetry.RecordSuccess(result);
                        if (isRecovery)
                        {
                            _telemetry.RecordRecoverySuccess(result);
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                        _telemetry.RecordFailure(result);
                        if (isRecovery)
                        {
                            _telemetry.RecordRecoveryFailure(result);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref processed);
                    Interlocked.Increment(ref failed);
                    _logger.LogError(
                        ex,
                        "Certificate renewal candidate failed | CorrelationId: {CorrelationId} | TenantId: {TenantId} | DomainId: {DomainId} | CertificateId: {CertificateId}",
                        candidateCorrelationId,
                        candidate.TenantId,
                        candidate.DomainId,
                        candidate.CertificateId);
                }
            });

        return new BatchResult(processed, succeeded, failed);
    }

    private sealed record BatchResult(int Processed, int Succeeded, int Failed);
}
