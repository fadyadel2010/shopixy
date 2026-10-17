using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

// Lifecycle tests only: SQLite executes the reads and transaction; the PostgreSQL
// SET statement is intercepted. These do not certify the PostgreSQL claim protocol.
public sealed class ShippingWebhookClaimStartupValidatorTests
{
    [Fact]
    public async Task Disabled_DoesNotResolveDatabaseOrCarrierServices()
    {
        var services = Services(enabled: false);
        await using var provider = services.BuildServiceProvider();
        await ShippingWebhookClaimStartupValidator.ValidateAsync(provider, NullLogger.Instance);
    }

    [Fact]
    public async Task AlreadyCanceled_DoesNotResolveDatabaseOrCarrierServices()
    {
        await using var provider = Services().BuildServiceProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ShippingWebhookClaimStartupValidator.ValidateAsync(provider, NullLogger.Instance, new CancellationToken(true)));
    }

    [Fact]
    public async Task NonPostgres_FailsClosedBeforeResolvingCarrierServices()
    {
        var services = Services();
        services.AddScoped(_ => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ShippingWebhookClaimStartupValidator.ValidateAsync(provider, NullLogger.Instance));
        Assert.Contains("require PostgreSQL", error.Message);
    }

    [Fact]
    public async Task CarrierConstructionFailure_IsPreservedBeforeAnyDatabaseConnection()
    {
        var services = Services();
        services.AddScoped(_ => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1").Options));
        var expected = new InvalidOperationException("Synthetic carrier construction failure");
        services.AddScoped<ICarrierShipmentService>(_ => throw expected);
        await using var provider = services.BuildServiceProvider();
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ShippingWebhookClaimStartupValidator.ValidateAsync(provider, NullLogger.Instance));
        Assert.Same(expected, actual);
        Assert.Null(provider.GetRequiredService<ApplicationDbContext>().Database.CurrentTransaction);
    }

    [Fact]
    public async Task CancellationDuringCarrierConstruction_IsObservedBeforeOpeningDatabase()
    {
        using var shutdown = new CancellationTokenSource();
        var services = Services();
        services.AddScoped(_ => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1").Options));
        services.AddScoped<ICarrierShipmentService>(_ =>
        {
            shutdown.Cancel();
            return Mock.Of<ICarrierShipmentService>();
        });
        await using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ShippingWebhookClaimStartupValidator.ValidateAsync(provider, NullLogger.Instance, shutdown.Token));
        Assert.Null(provider.GetRequiredService<ApplicationDbContext>().Database.CurrentTransaction);
    }

    [Fact]
    public async Task PreparedSchema_IsReadWithoutTrackingAndRolledBack_NotCommitted()
    {
        await using var fixture = await ProbeFixture.CreateAsync();
        await fixture.ValidateAsync();
        Assert.True(fixture.Commands.ReadOnlyRequested);
        Assert.True(fixture.Commands.ClaimsRead);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        Assert.Equal(1, fixture.Transactions.Rollbacks);
        Assert.Equal(0, fixture.Transactions.Commits);
        Assert.False(fixture.Transactions.RollbackToken.IsCancellationRequested);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task MissingMigration_FailsClosedAndStillRollsBack()
    {
        await using var fixture = await ProbeFixture.CreateAsync(migrated: false);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ValidateAsync());
        Assert.Contains(ShippingWebhookClaimStartupValidator.RequiredMigration, error.Message);
        Assert.False(fixture.Commands.ClaimsRead);
        Assert.Equal(1, fixture.Transactions.Rollbacks);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task MissingClaimsTable_IsNotMistakenForReadiness()
    {
        await using var fixture = await ProbeFixture.CreateAsync(claimsTable: false);
        await Assert.ThrowsAsync<SqliteException>(() => fixture.ValidateAsync());
        Assert.Equal(1, fixture.Transactions.Rollbacks);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseDeadline_ProducesActionableTimeoutAndUsesIndependentRollbackToken(bool rollbackFails)
    {
        await using var fixture = await ProbeFixture.CreateAsync();
        if (rollbackFails)
            fixture.Transactions.RollbackFailure = new IOException("Synthetic rollback failure after timeout");
        CancellationToken queryToken = default;
        fixture.Commands.BeforeClaimsRead = async ct =>
        {
            queryToken = ct;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            fixture.ValidateAsync(timeout: TimeSpan.FromSeconds(1)).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("reading the shipping webhook claims schema", error.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.True(queryToken.IsCancellationRequested);
        Assert.Equal(1, fixture.Transactions.Rollbacks);
        Assert.NotEqual(queryToken, fixture.Transactions.RollbackToken);
        Assert.False(fixture.Transactions.RollbackToken.IsCancellationRequested);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownCancellation_IsNotRelabeledAsTimeout_AndCannotCancelRollback(bool rollbackFails)
    {
        await using var fixture = await ProbeFixture.CreateAsync();
        if (rollbackFails)
            fixture.Transactions.RollbackFailure = new IOException("Synthetic rollback failure after shutdown");
        using var shutdown = new CancellationTokenSource();
        fixture.Commands.BeforeClaimsRead = ct =>
        {
            shutdown.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ValidateAsync(shutdown.Token));
        Assert.Equal(1, fixture.Transactions.Rollbacks);
        Assert.NotEqual(shutdown.Token, fixture.Transactions.RollbackToken);
        Assert.False(fixture.Transactions.RollbackToken.IsCancellationRequested);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task RollbackFailure_CannotReplaceOriginalMigrationFailure()
    {
        await using var fixture = await ProbeFixture.CreateAsync(migrated: false);
        fixture.Transactions.RollbackFailure = new IOException("Synthetic rollback failure");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ValidateAsync());
        Assert.Contains(ShippingWebhookClaimStartupValidator.RequiredMigration, error.Message);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task RollbackFailure_AfterSuccessfulReads_StillBlocksStartup()
    {
        await using var fixture = await ProbeFixture.CreateAsync();
        var expected = new IOException("Synthetic rollback failure");
        fixture.Transactions.RollbackFailure = expected;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ValidateAsync());
        Assert.Same(expected, error.InnerException);
        Assert.Contains("Startup remains blocked", error.Message);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    private static ServiceCollection Services(bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new ShippingWebhookClaimOptions { Enabled = enabled }));
        return services;
    }

    private sealed class ProbeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public ProbeCommands Commands { get; } = new();
        public ProbeTransactions Transactions { get; } = new();
        public ApplicationDbContext Db { get; private set; } = null!;

        public static async Task<ProbeFixture> CreateAsync(bool migrated = true, bool claimsTable = true)
        {
            var fixture = new ProbeFixture();
            await fixture.connection.OpenAsync();
            await using var setup = fixture.connection.CreateCommand();
            setup.CommandText = """
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT PRIMARY KEY, "ProductVersion" TEXT NOT NULL);
                """;
            if (migrated)
                setup.CommandText += $"INSERT INTO \"__EFMigrationsHistory\" VALUES ('{ShippingWebhookClaimStartupValidator.RequiredMigration}', '9.0.1');";
            if (claimsTable)
            {
                setup.CommandText += """
                    CREATE TABLE "ShippingWebhookClaims" (
                        "LogId" TEXT NOT NULL, "Generation" INTEGER NOT NULL, "TenantId" TEXT NOT NULL,
                        "Owner" TEXT NOT NULL, "AcquiredAt" TEXT NOT NULL, "LeaseUntil" TEXT NOT NULL,
                        "FinishedAt" TEXT, "Outcome" TEXT NOT NULL, "Error" TEXT,
                        PRIMARY KEY ("LogId", "Generation"));
                    """;
                setup.CommandText += $"""
                    INSERT INTO "ShippingWebhookClaims" VALUES (
                        '{Guid.NewGuid()}', 1, '{Guid.NewGuid()}', '{Guid.NewGuid()}',
                        '2026-10-15 00:00:00', '2026-10-15 00:01:00', NULL, 'Processing', NULL);
                    """;
            }
            await setup.ExecuteNonQueryAsync();
            fixture.Db = new ProbeDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(fixture.connection).AddInterceptors(fixture.Commands, fixture.Transactions).Options);
            _ = fixture.Db.Model; // Model construction is not part of the short synthetic SQL deadline.
            return fixture;
        }

        public Task ValidateAsync(CancellationToken ct = default, TimeSpan? timeout = null) =>
            ShippingWebhookClaimStartupValidator.ValidateDatabaseAsync(Db, NullLogger.Instance, timeout ?? TimeSpan.FromSeconds(5), ct);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class ProbeDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToArray())
                modelBuilder.Ignore(entity.ClrType);
            modelBuilder.Entity<ShippingWebhookClaim>().ToTable("ShippingWebhookClaims")
                .HasKey(c => new { c.LogId, c.Generation });
            modelBuilder.Entity<ShippingWebhookClaim>().HasQueryFilter(c => c.TenantId == Guid.Empty);
        }
    }

    private sealed class ProbeCommands : DbCommandInterceptor
    {
        public bool ReadOnlyRequested { get; private set; }
        public bool ClaimsRead { get; private set; }
        public Func<CancellationToken, Task>? BeforeClaimsRead { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Assert.Equal("SET TRANSACTION READ ONLY", command.CommandText);
            ReadOnlyRequested = true;
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Assert.True(ReadOnlyRequested);
            if (command.CommandText.Contains("ShippingWebhookClaims", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("WHERE", command.CommandText, StringComparison.OrdinalIgnoreCase);
                ClaimsRead = true;
                if (BeforeClaimsRead != null)
                    await BeforeClaimsRead(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ProbeTransactions : DbTransactionInterceptor
    {
        public int Rollbacks { get; private set; }
        public int Commits { get; private set; }
        public CancellationToken RollbackToken { get; private set; }
        public Exception? RollbackFailure { get; set; }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Rollbacks++;
            RollbackToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (RollbackFailure != null)
                throw RollbackFailure;
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Commits++;
            return Task.CompletedTask;
        }
    }
}
