using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Shipping;

/// <summary>Read-only, fail-closed readiness check; never migrates or replays webhooks.</summary>
public static class ShippingWebhookClaimStartupValidator
{
    public const string RequiredMigration = "20261012160000_ShippingWebhookOwnerClaims";
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public static async Task ValidateAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<ShippingWebhookClaimOptions>>().Value;
        if (!options.Enabled)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        var db = services.GetRequiredService<ApplicationDbContext>();
        if (!db.Database.IsNpgsql())
            throw new InvalidOperationException("Enabled shipping webhook claims require PostgreSQL.");

        // Cold service/model construction can initialize carrier adapters,
        // MassTransit, and EF metadata. Do it before the SQL deadline/transaction.
        _ = services.GetRequiredService<ICarrierShipmentService>();
        _ = db.Model;
        cancellationToken.ThrowIfCancellationRequested();

        await ValidateDatabaseAsync(db, logger, DatabaseTimeout, cancellationToken);
    }

    internal static async Task ValidateDatabaseAsync(
        ApplicationDbContext db,
        ILogger logger,
        TimeSpan databaseTimeout,
        CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(databaseTimeout);
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var stage = "opening the read-only transaction";
        IDbContextTransaction? transaction = null;
        Exception? validationFailure = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(probe.Token);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", probe.Token);
            stage = "checking the applied migration history";
            var migrations = await db.Database.GetAppliedMigrationsAsync(probe.Token);
            if (!migrations.Contains(RequiredMigration, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Enabled shipping webhook claims require migration {RequiredMigration}. Apply it before enabling Shipping:WebhookClaims:Enabled.");

            stage = "reading the shipping webhook claims schema";
            _ = await db.ShippingWebhookClaims.IgnoreQueryFilters().AsNoTracking()
                .Take(1).ToListAsync(probe.Token);
            probe.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            validationFailure = new TimeoutException(
                $"Shipping webhook claim database readiness timed out after {databaseTimeout.TotalSeconds:0.###} seconds while {stage}. " +
                "Check PostgreSQL connectivity, locks, and schema readiness. Startup remains blocked; no migration or webhook replay was performed.", ex);
            throw validationFailure;
        }
        catch (Exception ex)
        {
            validationFailure = ex;
            throw;
        }
        finally
        {
            if (transaction != null)
            {
                try
                {
                    // Neither the SQL deadline nor shutdown may cancel cleanup.
                    using var cleanup = new CancellationTokenSource(CleanupTimeout);
                    await transaction.RollbackAsync(cleanup.Token);
                }
                catch (Exception ex)
                {
                    if (validationFailure == null)
                    {
                        validationFailure = new InvalidOperationException(
                            "Shipping webhook claim readiness transaction could not be rolled back. Startup remains blocked.", ex);
                        throw validationFailure;
                    }
                    logger.LogWarning(ex,
                        "Shipping webhook claim startup rollback failed; preserving the original readiness failure at {Stage}", stage);
                }
                finally
                {
                    try
                    {
                        await transaction.DisposeAsync();
                    }
                    catch (Exception ex) when (validationFailure != null)
                    {
                        logger.LogWarning(ex,
                            "Shipping webhook claim startup transaction disposal failed; preserving the original failure");
                    }
                }
            }
        }
    }
}
