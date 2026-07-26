using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

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
        ShipmentStatus.Delivered,
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
        _logger.LogInformation("Starting tracking sync for all tenants");
        
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenantCount = 0;

        await foreach (var tenantId in dbContext.Shipments
            .Where(s => !TerminalStatuses.Contains(s.Status))
            .Where(s => !string.IsNullOrEmpty(s.TrackingNumber))
            .Select(s => s.TenantId)
            .Distinct()
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            tenantCount++;

            try
            {
                await SyncTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing tenant {TenantId}", tenantId);
            }
        }

        _logger.LogInformation("Found {Count} tenants with active shipments", tenantCount);
        
        _logger.LogInformation("Tracking sync completed for all tenants");
    }
    
    public async Task SyncTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Syncing tracking for tenant {TenantId}", tenantId);
        
        using var scope = _scopeFactory.CreateScope();
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
            using var scope = _scopeFactory.CreateScope();
            var carrierService = scope.ServiceProvider.GetRequiredService<ICarrierShipmentService>();

            var result = await carrierService.SyncTrackingAsync(
                tenantId,
                shipmentId,
                cancellationToken);

            return new ShipmentSyncExecution(
                shipmentId,
                result.Success,
                result.StatusChanged,
                result.PreviousStatus,
                result.CurrentStatus,
                result.ErrorMessage,
                null);
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
