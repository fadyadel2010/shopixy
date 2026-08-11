using System.Diagnostics;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Models;

namespace PointOfSale.API.Services.Storefront;

[DisableConcurrentExecution(timeoutInSeconds: 300)]
[AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class DomainVerificationSchedulerJob
{
    public const string RecurringJobId = "domain-verification-scheduler";

    private readonly IDomainVerificationSchedulerCandidateQuery _candidateQuery;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DomainVerificationSchedulerOptions> _options;
    private readonly DomainVerificationSchedulerTelemetry _telemetry;
    private readonly ILogger<DomainVerificationSchedulerJob> _logger;

    public DomainVerificationSchedulerJob(
        IDomainVerificationSchedulerCandidateQuery candidateQuery,
        IServiceScopeFactory scopeFactory,
        IOptions<DomainVerificationSchedulerOptions> options,
        DomainVerificationSchedulerTelemetry telemetry,
        ILogger<DomainVerificationSchedulerJob> logger)
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
        var totalCandidates = 0;
        var processed = 0;
        var success = 0;
        var failure = 0;
        var skipped = 0;
        var cancelled = false;
        var outcome = "success";

        _telemetry.AddActiveJob();

        try
        {
            if (!settings.Enabled)
            {
                outcome = "disabled";
                _logger.LogInformation(
                    "Domain verification scheduler skipped because it is disabled | JobId: {JobId} | CorrelationId: {CorrelationId}",
                    jobId,
                    executionId);
                return;
            }

            _logger.LogInformation(
                "Domain verification scheduler started | JobId: {JobId} | CorrelationId: {CorrelationId} | BatchSize: {BatchSize} | MaxConcurrentVerifications: {MaxConcurrentVerifications}",
                jobId,
                executionId,
                settings.BatchSize,
                settings.MaxConcurrentVerifications);

            while (!cancellationToken.IsCancellationRequested)
            {
                var candidates = await _candidateQuery.GetCandidatesAsync(
                    DateTime.UtcNow,
                    settings,
                    cancellationToken);

                totalCandidates += candidates.Count;
                _telemetry.RecordCandidates(candidates.Count);

                if (candidates.Count == 0)
                {
                    break;
                }

                var batchResult = await ProcessBatchAsync(
                    candidates,
                    jobId,
                    executionId,
                    settings.MaxConcurrentVerifications,
                    cancellationToken);

                processed += batchResult.Processed;
                success += batchResult.Success;
                failure += batchResult.Failure;
                skipped += batchResult.Skipped;

                if (candidates.Count < settings.BatchSize)
                {
                    break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            outcome = "cancelled";
            _telemetry.RecordRun(outcome);
            _logger.LogWarning(
                "Domain verification scheduler cancelled | JobId: {JobId} | CorrelationId: {CorrelationId} | CandidatesFound: {CandidatesFound} | ProcessedCount: {ProcessedCount} | SuccessCount: {SuccessCount} | FailureCount: {FailureCount} | SkippedCount: {SkippedCount}",
                jobId,
                executionId,
                totalCandidates,
                processed,
                success,
                failure,
                skipped);
            throw;
        }
        catch (Exception ex)
        {
            outcome = "infrastructure_failure";
            _telemetry.RecordRun(outcome);
            _telemetry.RecordFailure(1, "infrastructure_failure");
            _logger.LogError(
                ex,
                "Domain verification scheduler infrastructure failure | JobId: {JobId} | CorrelationId: {CorrelationId} | CandidatesFound: {CandidatesFound} | ProcessedCount: {ProcessedCount} | SuccessCount: {SuccessCount} | FailureCount: {FailureCount} | SkippedCount: {SkippedCount}",
                jobId,
                executionId,
                totalCandidates,
                processed,
                success,
                failure,
                skipped);
            throw;
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            _telemetry.RecordProcessed(processed);
            _telemetry.RecordSuccess(success);
            _telemetry.RecordFailure(failure, "domain_verification");
            _telemetry.RecordSkipped(skipped, "scheduler_skip");
            _telemetry.RecordDuration(duration, outcome);
            _telemetry.RemoveActiveJob();

            if (!cancelled && outcome is "success" or "disabled")
            {
                _telemetry.RecordRun(outcome);
                _logger.LogInformation(
                    "Domain verification scheduler completed | JobId: {JobId} | CorrelationId: {CorrelationId} | BatchSize: {BatchSize} | CandidatesFound: {CandidatesFound} | ProcessedCount: {ProcessedCount} | SuccessCount: {SuccessCount} | FailureCount: {FailureCount} | SkippedCount: {SkippedCount} | DurationMs: {DurationMs} | Cancelled: {Cancelled}",
                    jobId,
                    executionId,
                    settings.BatchSize,
                    totalCandidates,
                    processed,
                    success,
                    failure,
                    skipped,
                    duration.TotalMilliseconds,
                    cancelled);
            }
        }
    }

    private async Task<BatchResult> ProcessBatchAsync(
        IReadOnlyList<DomainVerificationSchedulerCandidate> candidates,
        string jobId,
        string schedulerCorrelationId,
        int maxConcurrentVerifications,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        var success = 0;
        var failure = 0;
        var skipped = 0;

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrentVerifications,
                CancellationToken = cancellationToken,
            },
            async (candidate, candidateCancellationToken) =>
            {
                var candidateCorrelationId = $"{schedulerCorrelationId}:{candidate.DomainId:N}";
                VerificationResult? result = null;

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var verificationService = scope.ServiceProvider.GetRequiredService<IDomainVerificationService>();
                    result = await verificationService.VerifyAsync(
                        candidate.DomainId,
                        candidate.TenantId,
                        candidateCorrelationId,
                        candidateCancellationToken);

                    Interlocked.Increment(ref processed);

                    if (result.Status == VerificationStatus.Success)
                    {
                        Interlocked.Increment(ref success);
                    }
                    else if (result.FailureReason == DomainVerificationFailureReasons.LifecycleViolation)
                    {
                        Interlocked.Increment(ref skipped);
                    }
                    else
                    {
                        Interlocked.Increment(ref failure);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (DomainNotFoundException ex)
                {
                    Interlocked.Increment(ref processed);
                    Interlocked.Increment(ref skipped);
                    _logger.LogWarning(
                        ex,
                        "Domain verification scheduler skipped missing candidate | JobId: {JobId} | CorrelationId: {CorrelationId} | DomainId: {DomainId} | TenantId: {TenantId}",
                        jobId,
                        candidateCorrelationId,
                        candidate.DomainId,
                        candidate.TenantId);
                }
                catch (DomainOperationException ex)
                {
                    Interlocked.Increment(ref processed);
                    Interlocked.Increment(ref skipped);
                    _logger.LogWarning(
                        ex,
                        "Domain verification scheduler skipped invalid candidate | JobId: {JobId} | CorrelationId: {CorrelationId} | DomainId: {DomainId} | TenantId: {TenantId}",
                        jobId,
                        candidateCorrelationId,
                        candidate.DomainId,
                        candidate.TenantId);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref processed);
                    Interlocked.Increment(ref failure);
                    _logger.LogError(
                        ex,
                        "Domain verification scheduler domain invocation failure | JobId: {JobId} | CorrelationId: {CorrelationId} | DomainId: {DomainId} | TenantId: {TenantId}",
                        jobId,
                        candidateCorrelationId,
                        candidate.DomainId,
                        candidate.TenantId);
                }

                _logger.LogInformation(
                    "Domain verification scheduler candidate processed | JobId: {JobId} | CorrelationId: {CorrelationId} | DomainId: {DomainId} | TenantId: {TenantId} | Status: {Status} | FailureReason: {FailureReason}",
                    jobId,
                    candidateCorrelationId,
                    candidate.DomainId,
                    candidate.TenantId,
                    result?.Status.ToString() ?? "Exception",
                    result?.FailureReason ?? "none");
            });

        return new BatchResult(processed, success, failure, skipped);
    }

    private sealed record BatchResult(int Processed, int Success, int Failure, int Skipped);
}
