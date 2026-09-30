using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

public sealed class AbandonedCartRecoveryRuntimeHealthService : IAbandonedCartRecoveryRuntimeHealthService
{
    internal const string ScannerRecurringJobId = "abandoned-cart-scanner";
    internal const string DispatcherRecurringJobId = "abandoned-cart-native-dispatcher";

    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoDatabase _mongoDatabase;
    private readonly IConfiguration _configuration;

    public AbandonedCartRecoveryRuntimeHealthService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _mongoDatabase = mongoDatabase;
        _configuration = configuration;
    }

    public async Task<AbandonedCartRecoveryRuntimeHealth> GetHealthAsync(CancellationToken ct = default)
    {
        if (!_configuration.GetValue("AbandonedCart:NativeDispatchEnabled", true))
        {
            return new(false, false, "native_recovery_disabled", "Native abandoned-cart recovery is disabled by configuration.");
        }

        if (!await _dbContext.Database.CanConnectAsync(ct))
        {
            return new(false, false, "native_database_unavailable", "The recovery database is unavailable.");
        }

        var schedulerOperational = false;
        try
        {
            var threshold = DateTime.UtcNow.AddMinutes(-5);
            var storage = JobStorage.Current;
            var hasHealthyServer = storage
                .GetMonitoringApi()
                .Servers()
                .Any(server => server.Heartbeat >= threshold);
            using var connection = storage.GetConnection();
            schedulerOperational = hasHealthyServer && HasRequiredRecurringJobs(
                connection.GetRecurringJobs().Select(job => job.Id));
        }
        catch (Exception)
        {
            schedulerOperational = false;
        }

        if (!schedulerOperational)
        {
            return new(false, false, "native_scheduler_unavailable", "The Hangfire recovery worker or required recurring jobs are unavailable.");
        }

        try
        {
            await _mongoDatabase.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: ct);
        }
        catch (Exception)
        {
            return new(true, false, "native_dispatcher_unavailable", "The audited WhatsApp dispatch log is unavailable.");
        }

        return new(true, true);
    }

    internal static bool HasRequiredRecurringJobs(IEnumerable<string> jobIds)
    {
        var ids = jobIds.ToHashSet(StringComparer.Ordinal);
        return ids.Contains(ScannerRecurringJobId) && ids.Contains(DispatcherRecurringJobId);
    }
}
