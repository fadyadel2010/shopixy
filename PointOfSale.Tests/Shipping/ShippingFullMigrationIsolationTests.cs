using Microsoft.EntityFrameworkCore;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Shipping;

public sealed class ShippingFullMigrationIsolationTests
{
    [Fact]
    public async Task FullMigrationChain_AppliesAndIsIdempotent_OnRestrictedDisposableDatabaseOnly()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var connection = await ShippingIsolatedPostgres.VerifyAsync("SHIPPING_MANUAL_TEST_CONNECTION", ManualShippingRuntimeFixtureTests.DatabaseName, deadline.Token);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection, options => options.CommandTimeout(120)).Options);
        await db.Database.MigrateAsync(deadline.Token);
        var applied = (await db.Database.GetAppliedMigrationsAsync(deadline.Token)).ToArray();
        Assert.Contains("20261013210000_CanonicalAddressEvidenceFoundation", applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(deadline.Token));
        await db.Database.MigrateAsync(deadline.Token);
        Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(deadline.Token));
    }
}
