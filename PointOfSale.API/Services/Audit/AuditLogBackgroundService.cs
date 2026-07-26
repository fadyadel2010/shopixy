using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;

namespace PointOfSale.API.Services.Audit;

public class AuditLogBackgroundService : BackgroundService
{
    private readonly IAuditQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogBackgroundService> _logger;

    public AuditLogBackgroundService(
        IAuditQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditLogBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditLog Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            AuditLogItem? logItem = null;
            try
            {
                logItem = await _queue.DequeueAsync(stoppingToken);

                // Use a scope to resolve DbContext
                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var auditLog = AuditService.MapAuditLog(logItem);

                    await dbContext.AuditLogs.AddAsync(auditLog, stoppingToken);
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                if (logItem != null)
                {
                    _queue.Retry(logItem);
                }

                _logger.LogError(
                    ex,
                    "Error occurred while processing audit log. Retrying without evidence loss. QueueDepth: {QueueDepth} RetryCount: {RetryCount}",
                    _queue.QueueDepth,
                    _queue.RetryCount);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        _logger.LogInformation("AuditLog Background Service is stopping.");
    }
}
