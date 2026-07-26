using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public sealed class CommerceHubPayoutReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProcessingPayoutMaxLag = TimeSpan.FromHours(2);
    private const int SweepBatchSize = 25;
    private const int MaxSweepPassesPerTick = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubPayoutReconciliationHostedService> _logger;

    public CommerceHubPayoutReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubPayoutReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var quarantined = 0;
                for (var pass = 0; pass < MaxSweepPassesPerTick; pass++)
                {
                    var updated = await QuarantineStaleProcessingPayoutsAsync(stoppingToken);
                    if (updated == 0)
                    {
                        break;
                    }

                    quarantined += updated;
                }

                if (quarantined > 0)
                {
                    _logger.LogCritical(
                        "CommerceHub payout reconciliation sweep quarantined {Count} stale processing payout(s) into ReconciliationRequired.",
                        quarantined);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommerceHub payout reconciliation sweep failed.");
            }
        }
    }

    private async Task<int> QuarantineStaleProcessingPayoutsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var staleCutoffUtc = DateTime.UtcNow.Subtract(ProcessingPayoutMaxLag);

        var payoutIds = await dbContext.CommerceHubPayouts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payout =>
                !payout.IsDeleted &&
                payout.Status == CommerceHubPayoutStatus.Processing &&
                (payout.ProcessedAtUtc ?? payout.CreatedAt) < staleCutoffUtc)
            .OrderBy(payout => payout.ProcessedAtUtc ?? payout.CreatedAt)
            .Select(payout => payout.Id)
            .Take(SweepBatchSize)
            .ToListAsync(cancellationToken);

        if (payoutIds.Count == 0)
        {
            return 0;
        }

        var nowUtc = DateTime.UtcNow;
        var updatedCount = 0;
        using var payoutBoundary = CommerceHubBoundaryMutationScope.BeginPayout();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        foreach (var payoutId in payoutIds.OrderBy(id => id))
        {
            var payout = await LoadPayoutForReconciliationAsync(dbContext, payoutId, cancellationToken);
            if (payout == null)
            {
                continue;
            }

            if (payout.IsDeleted ||
                payout.Status != CommerceHubPayoutStatus.Processing ||
                (payout.ProcessedAtUtc ?? payout.CreatedAt) >= staleCutoffUtc)
            {
                continue;
            }

            var reconciliationCurrency = FinancialCurrencyGuard.RequireEgpCurrency(
                payout.Currency,
                "commercehub.payout.reconciliation.currency",
                $"payoutId:{payout.Id:D};tenantId:{payout.TenantId:D}");

            payout.Status = CommerceHubPayoutStatus.ReconciliationRequired;
            payout.ProcessedBy ??= "system:payout-reconciliation";
            payout.FailureReason =
                "External transfer outcome is unknown after the processing handoff exceeded the safe reconciliation window. Complete with execution evidence or fail with confirmed non-execution evidence.";
            payout.UpdatedAt = nowUtc;

            dbContext.AuditLogs.Add(new AuditLog
            {
                TenantId = payout.TenantId,
                TimestampUtc = nowUtc,
                ActionType = "PayoutReconciliationRequired",
                EntityType = nameof(CommerceHubPayout),
                EntityId = payout.Id.ToString(),
                Description = "Stale processing payout was quarantined into reconciliation-required state after external handoff exceeded the safe window.",
                AfterDataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    payout.Id,
                    payout.TenantId,
                    payout.Amount,
                    Currency = reconciliationCurrency,
                    payout.Reference,
                    payout.ExternalReferenceId,
                    payout.BankTransactionId,
                    payout.ExternalTransferIdempotencyKey,
                    payout.ExternalTransferHandoffToken,
                    payout.ExternalTransferHandoffCommittedAtUtc,
                    PriorStatus = CommerceHubPayoutStatus.Processing.ToString(),
                    NewStatus = CommerceHubPayoutStatus.ReconciliationRequired.ToString(),
                    ProcessedAtUtc = payout.ProcessedAtUtc,
                    payout.CreatedAt
                }),
                Source = "Background",
                SuccessFlag = true
            });

            updatedCount++;
        }

        if (updatedCount == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updatedCount;
    }

    private async Task<CommerceHubPayout?> LoadPayoutForReconciliationAsync(
        ApplicationDbContext dbContext,
        Guid payoutId,
        CancellationToken cancellationToken)
    {
        var providerName = dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.CommerceHubPayouts
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""CommerceHubPayouts""
                    WHERE ""Id"" = {payoutId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.CommerceHubPayouts
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [CommerceHubPayouts] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {payoutId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await dbContext.CommerceHubPayouts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(payout => payout.Id == payoutId && !payout.IsDeleted, cancellationToken);
    }
}
