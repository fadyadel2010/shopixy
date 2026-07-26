using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Jobs;

/// <summary>
/// Hosted service that resiliently initializes Hangfire recurring jobs with retry logic.
///
/// PROBLEM: CanInitializeHangfireStorage() was evaluated once during boot. If that single
/// PostgreSQL probe failed, Hangfire server, dashboard, and recurring jobs were never
/// registered for the lifetime of the process — turning a transient dependency hiccup
/// into permanent background-job loss.
///
/// SOLUTION: Hangfire services (AddHangfire + AddHangfireServer) are now always registered
/// in DI regardless of PostgreSQL availability. The Hangfire server internally retries
/// connecting to storage. This hosted service handles deferred recurring-job registration:
/// if PostgreSQL is available at startup, jobs are registered immediately; otherwise, this
/// service retries with exponential backoff until PostgreSQL recovers, then registers
/// all recurring jobs.
/// </summary>
public class HangfireInitializationHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HangfireInitializationHostedService> _logger;
    private readonly IConfiguration _configuration;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    private readonly string? _connectionString;
    private readonly string _dashboardPath;
    private CancellationTokenSource? _retryCts;
    private Task? _retryTask;
    private bool _isInitialized;
    private bool _disposed;

    // Exponential backoff delays: 5s, 10s, 30s, 60s, 120s, then 5m repeating
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120)
    ];

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    public HangfireInitializationHostedService(
        IServiceProvider serviceProvider,
        ILogger<HangfireInitializationHostedService> logger,
        IConfiguration configuration,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _reliabilityTelemetry = reliabilityTelemetry;
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _dashboardPath = configuration["Hangfire:DashboardPath"] ?? "/hangfire";
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogWarning("Hangfire initialization skipped: DefaultConnection is not configured.");
            _reliabilityTelemetry?.RecordBackgroundJobExecution("hangfire_initialization", "skipped");
            return Task.CompletedTask;
        }

        // Try immediate initialization
        if (CanConnectToPostgres())
        {
            _logger.LogInformation("PostgreSQL is available. Registering Hangfire recurring jobs immediately.");
            RegisterRecurringJobs();
            _isInitialized = true;
            _reliabilityTelemetry?.RecordBackgroundJobExecution("hangfire_initialization", "success");
            return Task.CompletedTask;
        }

        // PostgreSQL not available — start background retry
        _logger.LogWarning(
            "PostgreSQL is not available at startup. Starting background retry for Hangfire recurring job registration.");
        _reliabilityTelemetry?.RecordBackgroundJobExecution("hangfire_initialization", "retry");

        _retryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _retryTask = RetryInitializationAsync(_retryCts.Token);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _retryCts?.Cancel();
        return _retryTask ?? Task.CompletedTask;
    }

    private async Task RetryInitializationAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = attempt < RetryDelays.Length
                ? RetryDelays[attempt]
                : MaxRetryDelay;

            attempt++;

            _logger.LogInformation(
                "Hangfire recurring job registration retry attempt {Attempt}. " +
                "Waiting {DelaySeconds}s before next probe.",
                attempt, delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (CanConnectToPostgres())
                {
                    _logger.LogInformation(
                        "PostgreSQL is now available. Registering Hangfire recurring jobs on retry attempt {Attempt}.",
                        attempt);

                    RegisterRecurringJobs();
                    _isInitialized = true;
                    _reliabilityTelemetry?.RecordBackgroundJobExecution("hangfire_initialization", "success");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Hangfire recurring job registration retry attempt {Attempt} encountered an error.",
                    attempt);
                _reliabilityTelemetry?.RecordBackgroundJobExecution("hangfire_initialization", "probe_failed");
            }
        }
    }

    private bool CanConnectToPostgres()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return false;

        try
        {
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Timeout = 5,
                CommandTimeout = 5
            };

            using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
            connection.Open();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PostgreSQL connection probe failed.");
            return false;
        }
    }

    /// <summary>
    /// Registers all Hangfire recurring jobs. This method is idempotent —
    /// RecurringJob.AddOrUpdate() adds or updates, so calling it multiple times is safe.
    /// </summary>
    private void RegisterRecurringJobs()
    {
        // =============================================================================
        // SHIPPING TRACKING SYNC JOB (P2 Hardening)
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Shipping.BackgroundJobs.ITrackingSyncJob>(
            PointOfSale.Infrastructure.Shipping.BackgroundJobs.TrackingSyncJobRegistration.SyncAllJobId,
            job => job.SyncAllAsync(System.Threading.CancellationToken.None),
            PointOfSale.Infrastructure.Shipping.BackgroundJobs.TrackingSyncJobRegistration.CronExpression);

        // =============================================================================
        // MANUAL WALLET PROOF CLEANUP JOB (P3.9 Hardening)
        // =============================================================================
        RecurringJob.AddOrUpdate<ManualWalletProofCleanupJob>(
            "manual-wallet-proof-cleanup",
            job => job.RunAsync(System.Threading.CancellationToken.None),
            Cron.HourInterval(6));

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.AccountingOutboxCleanupJob>(
            "accounting-outbox-cleanup",
            job => job.RunAsync(System.Threading.CancellationToken.None),
            Cron.Hourly);

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.InventoryReservationCleanupJob>(
            "inventory-reservation-cleanup",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Minutely);

        // =============================================================================
        // REFUND EVIDENCE CLEANUP JOB (Refund Evidence Hardening)
        // =============================================================================
        RecurringJob.AddOrUpdate<RefundEvidenceCleanupJob>(
            "refund-evidence-cleanup",
            job => job.RunAsync(System.Threading.CancellationToken.None),
            Cron.Hourly);

        // =============================================================================
        // WHATSAPP BACKGROUND JOBS (Phase 13)
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.WhatsApp.Jobs.WhatsAppSessionHeartbeatJob>(
            "whatsapp-session-heartbeat",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.MinuteInterval(5));

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.WhatsApp.Jobs.WhatsAppMessageRetryJob>(
            "whatsapp-message-retry",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.MinuteInterval(5));

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.WhatsApp.Jobs.WhatsAppSessionCleanupJob>(
            "whatsapp-session-cleanup",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Daily(3));

        // =============================================================================
        // ABANDONED CART RECOVERY SCANNER
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.AbandonedCartScannerJob>(
            "abandoned-cart-scanner",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.MinuteInterval(5));

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.AbandonedCartCleanupJob>(
            "abandoned-cart-cleanup",
            job => job.ExecuteAsync(),
            Cron.Daily(3));

        // =============================================================================
        // ABANDONED CHECKOUT SESSION SCANNER
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.AbandonedCheckoutScannerJob>(
            "abandoned-checkout-scanner",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.MinuteInterval(5));

        // =============================================================================
        // P2-13: PROCESSED TRUST EVENTS CLEANUP
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.ProcessedTrustEventsCleanupJob>(
            "processed-trust-events-cleanup",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Daily(3));

        // =============================================================================
        // COMMERCEHUB CART CLEANUP
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.CommerceHubCartCleanupJob>(
            "commercehub-cart-cleanup",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.HourInterval(6));

        // =============================================================================
        // ORDER REQUEST IDEMPOTENCY CLEANUP
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.OrderRequestIdempotencyCleanupJob>(
            "order-request-idempotency-cleanup",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Daily(2));

        // =============================================================================
        // MARKETPLACE OPERATIONAL JOBS (Phase CR-2)
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.TrustDriftReconciliationJob>(
            "marketplace-trust-reconciliation",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Hourly);

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.ElasticsearchDriftReconciliationJob>(
            "marketplace-es-drift-reconciliation",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Daily(4));

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.CommerceHubReindexService>(
            "commercehub-search-reconciliation",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Hourly);

        RecurringJob.AddOrUpdate<PointOfSale.API.Services.CommerceHub.CommerceHubFinanceReconciliationJob>(
            "commercehub-finance-reconciliation",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Hourly);

        RecurringJob.AddOrUpdate<PointOfSale.API.Services.CommerceHub.CommerceHubInventoryReconciliationJob>(
            "commercehub-inventory-reconciliation",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.Hourly);

        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.DiskUsageMonitorJob>(
            "marketplace-disk-monitor",
            job => job.Execute(),
            Cron.Hourly);

        // AF-34: Daily ranking score recalculation (midnight UTC)
        RecurringJob.AddOrUpdate<PointOfSale.Infrastructure.Jobs.AffiliateRankingRecalculationJob>(
            "affiliate-ranking-recalculation",
            job => job.Execute(),
            Cron.Daily);

        // =============================================================================
        // STALE RETURN ESCALATION (Audit Fix W4 — Return SLA Enforcement)
        // =============================================================================
        RecurringJob.AddOrUpdate<PointOfSale.API.Services.CommerceHub.StaleReturnEscalationJob>(
            "commercehub-stale-return-escalation",
            job => job.ExecuteAsync(System.Threading.CancellationToken.None),
            Cron.HourInterval(6));

        _logger.LogInformation("All Hangfire recurring jobs registered successfully.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retryCts?.Cancel();
        _retryCts?.Dispose();
    }
}
