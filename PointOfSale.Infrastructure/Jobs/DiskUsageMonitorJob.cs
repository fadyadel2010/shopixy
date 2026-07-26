using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Operational Guard: Monitors disk usage for user-generated content.
/// Prevents silent storage exhaustion attacks or leaks.
/// </summary>
public class DiskUsageMonitorJob
{
    private readonly MarketplaceTelemetry _telemetry;
    private readonly ILogger<DiskUsageMonitorJob> _logger;
    private readonly IHostEnvironment _env;

    // Hard limits for local storage (Phase CR-2)
    private const long CriticalThresholdBytes = 10L * 1024 * 1024 * 1024; // 10GB
    private const long WarningThresholdBytes = 5L * 1024 * 1024 * 1024; // 5GB

    public DiskUsageMonitorJob(
        MarketplaceTelemetry telemetry,
        ILogger<DiskUsageMonitorJob> logger,
        IHostEnvironment env)
    {
        _telemetry = telemetry;
        _logger = logger;
        _env = env;
    }

    public void Execute()
    {
        try
        {
            var uploadsPath = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "chat");
            
            if (!Directory.Exists(uploadsPath))
            {
                // No uploads yet
                _logger.LogDebug("Upload directory empty or missing: {Path}", uploadsPath);
                return;
            }

            var size = GetDirectorySize(new DirectoryInfo(uploadsPath));
            
            // Record metric via ObservableGauge
            _telemetry.UpdateDiskUsage(size);
            
            if (size > CriticalThresholdBytes)
            {
                _logger.LogCritical("CRITICAL STORAGE ALERT | Uploads directory > 10GB | Size: {Size} bytes", size);
                // Alerting handled by metric threshold or log monitor
            }
            else if (size > WarningThresholdBytes)
            {
                _logger.LogWarning("STORAGE ALERT | Uploads directory > 5GB | Size: {Size} bytes", size);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to monitor disk usage");
        }
    }

    private static long GetDirectorySize(DirectoryInfo dir)
    {
        long size = 0;
        try
        {
            foreach (var file in dir.GetFiles())
                size += file.Length;
            foreach (var subDir in dir.GetDirectories())
                size += GetDirectorySize(subDir);
        }
        catch (UnauthorizedAccessException) 
        { 
            // Skip inaccessible dirs
        }
        return size;
    }
}
