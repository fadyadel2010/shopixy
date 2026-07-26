using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Background service that runs once on application startup to ensure
/// all existing tenants have their Chart of Accounts seeded.
/// 
/// This handles the migration scenario where existing tenants were created
/// before the automatic seeding was implemented.
/// 
/// After the initial run, this service completes and does not run again
/// until the next application restart.
/// </summary>
public class ChartOfAccountsStartupSeeder : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChartOfAccountsStartupSeeder> _logger;

    public ChartOfAccountsStartupSeeder(
        IServiceProvider serviceProvider,
        ILogger<ChartOfAccountsStartupSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the application finish starting
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        
        _logger.LogInformation("[COA Startup Seeder] Starting Chart of Accounts verification...");
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seeder = scope.ServiceProvider.GetRequiredService<ITenantAccountSeeder>();
            
            // Get all active tenants
            var tenants = await db.Tenants
                .AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(stoppingToken);
            
            _logger.LogInformation("[COA Startup Seeder] Checking {Count} active tenants...", tenants.Count);
            
            var seededCount = 0;
            foreach (var tenant in tenants)
            {
                try
                {
                    // Always run the seeder — it's idempotent (skips existing accounts).
                    // This ensures newly-added system accounts (e.g., AF-14 accounts 2105/6110)
                    // are automatically created for existing tenants on the next app restart.
                    await seeder.SeedAccountsAsync(tenant.Id, stoppingToken);
                    seededCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, 
                        "[COA Startup Seeder] Failed to seed tenant {TenantId} ({TenantName})",
                        tenant.Id, tenant.Name);
                }
            }
            
            if (seededCount > 0)
            {
                _logger.LogInformation(
                    "[COA Startup Seeder] Completed: Seeded {SeededCount}/{TotalCount} tenants",
                    seededCount, tenants.Count);
            }
            else
            {
                _logger.LogInformation(
                    "[COA Startup Seeder] Completed: All {Count} tenants already have accounts",
                    tenants.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COA Startup Seeder] Failed to complete startup seeding");
        }
    }
}
