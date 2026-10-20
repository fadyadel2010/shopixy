using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.AbandonedCart;

// Existing database only. No managed fixture, migrations, reset, or real transport.
internal sealed class NativeSchedulerConfiguredScope : IAsyncDisposable
{
    private readonly IDbContextTransaction _transaction;
    private readonly string _evidence;
    private readonly string _test;
    internal Guid TenantId { get; } = Guid.NewGuid();
    internal ApplicationDbContext Context { get; }

    private NativeSchedulerConfiguredScope(ApplicationDbContext context, IDbContextTransaction transaction, string evidence, string test, Guid tenant)
    {
        Context = context; _transaction = transaction; _evidence = evidence; _test = test; TenantId = tenant;
    }

    internal static async Task<NativeSchedulerConfiguredScope> OpenAsync(DateTime clock, [CallerMemberName] string test = "")
    {
        var evidence = Environment.GetEnvironmentVariable("PHASE8_EVIDENCE")
            ?? throw new InvalidOperationException("PHASE8_EVIDENCE is required.");
        Directory.CreateDirectory(evidence);
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString(), p => p.CommandTimeout(30)).Options;
        var context = new ApplicationDbContext(options, new TestTenantContext(tenant));
        try
        {
            // DispatchDueAsync intentionally scans all tenants. Refuse execution
            // rather than changing or suppressing any existing merchant schedule.
            var horizon = clock.AddDays(1);
            Assert.False(await context.AbandonedCartRecoveryLogs.IgnoreQueryFilters().AnyAsync(x =>
                (x.FirstMessageDueAtUtc != null && x.FirstMessageDueAtUtc <= horizon) ||
                (x.SecondMessageDueAtUtc != null && x.SecondMessageDueAtUtc <= horizon)));
            await File.WriteAllTextAsync(Path.Combine(evidence, "scheduler-" + test + "-manifest.json"), JsonSerializer.Serialize(new { tenant, test, clock, rollbackOnly = true, controlledDispatcher = true }));
            var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'");
            return new NativeSchedulerConfiguredScope(context, transaction, evidence, test, tenant);
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"); }
        finally { await _transaction.RollbackAsync(); await _transaction.DisposeAsync(); await Context.DisposeAsync(); }
        await using var observer = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == TenantId));
        Assert.False(await observer.AbandonedCarts.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId));
        Assert.False(await observer.AbandonedCartRecoveryLogs.IgnoreQueryFilters().AnyAsync(x => x.TenantId == TenantId));
        await File.WriteAllTextAsync(Path.Combine(_evidence, "scheduler-" + _test + "-observer.json"), JsonSerializer.Serialize(new { tenant = TenantId, freshObserverAbsent = true, rollbackOnly = true }));
    }
}

public sealed class NativeSchedulerConfiguredFactAttribute : FactAttribute
{
    public NativeSchedulerConfiguredFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PHASE8_CONFIGURED") != "1")
            Skip = "Explicit existing configured PostgreSQL opt-in PHASE8_CONFIGURED=1 required; no provisioning fallback.";
    }
}
