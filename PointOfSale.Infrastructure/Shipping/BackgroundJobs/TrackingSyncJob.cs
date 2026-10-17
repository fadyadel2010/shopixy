using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Infrastructure.Shipping.BackgroundJobs;

/// <summary>
/// Background job for syncing tracking status from carriers.
/// 
/// ARCHITECTURAL RULES:
/// - Runs on schedule (every 30 minutes recommended)
/// - Only syncs shipments not in terminal states
/// - Falls back to carrier API when webhooks unavailable
/// - Respects carrier rate limits
/// </summary>
public interface ITrackingSyncJob
{
    /// <summary>
    /// Sync all active shipments for all tenants.
    /// </summary>
    Task SyncAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sync active shipments for specific tenant.
    /// </summary>
    Task SyncTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of tracking sync job.
/// </summary>
public class TrackingSyncJob : ITrackingSyncJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrackingSyncJob> _logger;
    private const int ShipmentBatchSize = 50;
    private const int MaxConcurrentShipmentSyncs = 5;
    
    // Terminal statuses that don't need sync
    private static readonly ShipmentStatus[] TerminalStatuses =
    {
        ShipmentStatus.Failed,
        ShipmentStatus.Cancelled,
        ShipmentStatus.Returned
    };
    
    // Minimum time between syncs for same shipment (avoid rate limits)
    private static readonly TimeSpan MinSyncInterval = TimeSpan.FromMinutes(15);
    
    public TrackingSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<TrackingSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    
    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting tracking sync for all tenants");

        IReadOnlyList<Guid> tenantIds;
        await using (var directoryScope = _scopeFactory.CreateAsyncScope())
        {
            // Bootstrap can read directory IDs only; never discover tenants by
            // bypassing the mandatory filters on Shipments in a tenantless scope.
            var directory = directoryScope.ServiceProvider.GetRequiredService<ITrackingTenantDirectory>();
            tenantIds = await directory.ListEligibleTenantIdsAsync(cancellationToken);
        }

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SyncTenantAsync(tenantId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing tenant {TenantId}", tenantId);
            }
        }

        _logger.LogInformation("Examined {Count} eligible tenants for tracking sync", tenantIds.Count);
        
        _logger.LogInformation("Tracking sync completed for all tenants");
    }
    
    public async Task SyncTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Syncing tracking for tenant {TenantId}", tenantId);
        
        await using var scope = await CreateTenantScopeAsync(tenantId, cancellationToken);
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        // Get shipments that need sync
        var syncCutoff = DateTime.UtcNow - MinSyncInterval;
        
        var shipmentIds = await dbContext.Shipments
            .Where(s => s.TenantId == tenantId)
            .Where(s => !TerminalStatuses.Contains(s.Status))
            .Where(s => !string.IsNullOrEmpty(s.TrackingNumber))
            .Where(s => s.LastCarrierSyncAtUtc == null || s.LastCarrierSyncAtUtc < syncCutoff)
            .OrderBy(s => s.LastCarrierSyncAtUtc ?? DateTime.MinValue) // Oldest first
            .Select(s => s.Id)
            .Take(ShipmentBatchSize)
            .ToListAsync(cancellationToken);
        
        if (!shipmentIds.Any())
        {
            _logger.LogDebug("No shipments to sync for tenant {TenantId}", tenantId);
            return;
        }
        
        _logger.LogInformation(
            "Syncing {Count} shipments for tenant {TenantId}",
            shipmentIds.Count, tenantId);
        
        var successCount = 0;
        var failCount = 0;
        var changedCount = 0;
        
        foreach (var chunk in shipmentIds.Chunk(MaxConcurrentShipmentSyncs))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchResults = await Task.WhenAll(
                chunk.Select(shipmentId => SyncShipmentAsync(tenantId, shipmentId, cancellationToken)));

            foreach (var batchResult in batchResults)
            {
                if (batchResult.Success)
                {
                    successCount++;
                    if (batchResult.StatusChanged)
                    {
                        changedCount++;
                        _logger.LogInformation(
                            "Shipment {ShipmentId} status changed: {PreviousStatus} -> {NewStatus}",
                            batchResult.ShipmentId, batchResult.PreviousStatus, batchResult.CurrentStatus);
                    }
                }
                else
                {
                    failCount++;
                    if (batchResult.Exception != null)
                    {
                        _logger.LogError(
                            batchResult.Exception,
                            "Error syncing shipment {ShipmentId}",
                            batchResult.ShipmentId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to sync shipment {ShipmentId}: {Error}",
                            batchResult.ShipmentId, batchResult.ErrorMessage);
                    }
                }
            }
        }
        
        _logger.LogInformation(
            "Tenant {TenantId} sync complete: {Success} success, {Failed} failed, {Changed} status changes",
            tenantId, successCount, failCount, changedCount);
    }

    private async Task<ShipmentSyncExecution> SyncShipmentAsync(
        Guid tenantId,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = await CreateTenantScopeAsync(tenantId, cancellationToken);
            var carrierService = scope.ServiceProvider.GetRequiredService<ICarrierShipmentService>();

            var result = await carrierService.SyncTrackingAsync(
                tenantId,
                shipmentId,
                cancellationToken);

            // The existing carrier service returns a failure for some cancelled
            // operations. Scheduler shutdown must still propagate cancellation.
            cancellationToken.ThrowIfCancellationRequested();

            return new ShipmentSyncExecution(
                shipmentId,
                result.Success,
                result.StatusChanged,
                result.PreviousStatus,
                result.CurrentStatus,
                result.ErrorMessage,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ShipmentSyncExecution(
                shipmentId,
                false,
                false,
                null,
                null,
                ex.Message,
                ex);
        }
    }

    private async Task<AsyncServiceScope> CreateTenantScopeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("TRACKING_TENANT_REQUIRED");

        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            // Establish identity before DbContext/options/repositories/services
            // can capture it. Never reuse or mutate a request's ambient context.
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var tenantAuthority = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            if (!ReferenceEquals(tenantContext, tenantAuthority) || tenantContext.TenantId.HasValue)
                throw new InvalidOperationException("TRACKING_TENANT_CONTEXT_INVALID");
            tenantContext.SetTenantId(tenantId);

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // A supplied ID is only a locator. Qualify SQL lifecycle before any
            // shipment selection/provider work, also rechecking each worker scope.
            if (!await dbContext.Tenants.AsNoTracking().AnyAsync(tenant =>
                    tenant.Id == tenantId && tenant.IsActive &&
                    tenant.Status == TenantStatus.Active && tenant.DeletedAtUtc == null, cancellationToken))
                throw new InvalidOperationException("TRACKING_TENANT_NOT_ELIGIBLE");

            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }
}

internal sealed record ShipmentSyncExecution(
    Guid ShipmentId,
    bool Success,
    bool StatusChanged,
    ShipmentStatus? PreviousStatus,
    ShipmentStatus? CurrentStatus,
    string? ErrorMessage,
    Exception? Exception);

/// <summary>
/// Hangfire job registration helper.
/// 
/// To register the tracking sync job in Program.cs:
/// 
/// <code>
/// // After configuring Hangfire...
/// var recurringJobs = app.Services.GetRequiredService&lt;IRecurringJobManager&gt;();
/// recurringJobs.AddOrUpdate&lt;ITrackingSyncJob&gt;(
///     "shipping-tracking-sync",
///     job => job.SyncAllAsync(CancellationToken.None),
///     "*/30 * * * *"); // Every 30 minutes
/// </code>
/// </summary>
public static class TrackingSyncJobRegistration
{
    /// <summary>
    /// Job ID for recurring sync job.
    /// </summary>
    public const string SyncAllJobId = "shipping-tracking-sync";
    
    /// <summary>
    /// Cron expression for every 30 minutes.
    /// </summary>
    public const string CronExpression = "*/30 * * * *";
}
