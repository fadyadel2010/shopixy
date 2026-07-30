using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Npgsql;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class GlobalProductMutationIdempotencyIntegrationTests
{
    public static TheoryData<string> ProductMutationOperations => new()
    {
        "VARIANT_DELETE",
        "VARIANT_RESTORE",
        "VARIANT_DEFAULT",
        "VARIANT_STOCK",
        "PRODUCT_STOCK",
        "PRODUCT_LEGACY_MULTI_UNIT",
        "PRODUCT_MEDIA_ADD",
        "PRODUCT_MEDIA_DELETE",
        "PRODUCT_MEDIA_REORDER"
    };

    [Theory]
    [MemberData(nameof(ProductMutationOperations))]
    public async Task SameKeySamePayload_ReplaysWithoutDuplicateAuditOutboxOrMutation(string operation)
    {
        await using var scope = await PostgresScope.TryCreateAsync();
        if (scope is null)
        {
            return;
        }

        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        await using var context = scope.CreateDbContext(tenantContext);
        var service = new ProductMutationIdempotencyService(context);
        var request = Request(service, tenantId, operation, "same-key", "same-payload");

        await CommitMutationAsync(context, service, request, operation);
        context.ChangeTracker.Clear();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var replay = await service.TryClaimAsync(request);
            Assert.Null(replay.Record);
            Assert.NotNull(replay.Replay);
            Assert.Equal(ProductMutationIdempotencyReplayKind.Completed, replay.Replay!.Kind);
            await transaction.RollbackAsync();
        }

        Assert.Equal(1, await CountAuditAsync(context, tenantId, operation));
        Assert.Equal(1, await CountOutboxAsync(context, tenantId, operation));
        Assert.Equal(1, await CountIdempotencyAsync(context, tenantId, operation, request.Key));
    }

    [Theory]
    [MemberData(nameof(ProductMutationOperations))]
    public async Task SameKeyDifferentPayload_ConflictsWithoutDuplicateAuditOutboxOrMutation(string operation)
    {
        await using var scope = await PostgresScope.TryCreateAsync();
        if (scope is null)
        {
            return;
        }

        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        await using var context = scope.CreateDbContext(tenantContext);
        var service = new ProductMutationIdempotencyService(context);
        var original = Request(service, tenantId, operation, "conflict-key", "original");
        var conflicting = Request(service, tenantId, operation, "conflict-key", "changed");

        await CommitMutationAsync(context, service, original, operation);
        context.ChangeTracker.Clear();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var conflict = await service.TryClaimAsync(conflicting);
            Assert.Null(conflict.Record);
            Assert.NotNull(conflict.Replay);
            Assert.Equal(ProductMutationIdempotencyReplayKind.Conflict, conflict.Replay!.Kind);
            await transaction.RollbackAsync();
        }

        Assert.Equal(1, await CountAuditAsync(context, tenantId, operation));
        Assert.Equal(1, await CountOutboxAsync(context, tenantId, operation));
    }

    [Fact]
    public async Task ConcurrentRetries_SameKeySamePayload_ProduceExactlyOneDurableOutcome()
    {
        await using var scope = await PostgresScope.TryCreateAsync();
        if (scope is null)
        {
            return;
        }

        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        await using var context = scope.CreateDbContext(tenantContext);
        var service = new ProductMutationIdempotencyService(context);
        var request = Request(service, tenantId, "VARIANT_STOCK", "concurrent-key", "same-payload");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var sibling = scope.CreateDbContext(tenantContext!);
            var siblingService = new ProductMutationIdempotencyService(sibling);
            await using var transaction = await sibling.Database.BeginTransactionAsync();
            var claim = await siblingService.TryClaimAsync(request);
            if (claim.Record is null)
            {
                await transaction.RollbackAsync();
                return claim.Replay?.Kind.ToString() ?? "None";
            }

            StageMutationMarkers(sibling, claim.Record, "VARIANT_STOCK");
            siblingService.Complete(claim.Record, StatusCodes.Status200OK, new { ok = true }, request.ProductId, request.ProductVariantId);
            await sibling.SaveChangesAsync();
            await transaction.CommitAsync();
            return "Mutated";
        }));

        Assert.Single(outcomes, outcome => outcome == "Mutated");
        Assert.Equal(1, await CountAuditAsync(context, tenantId, "VARIANT_STOCK"));
        Assert.Equal(1, await CountOutboxAsync(context, tenantId, "VARIANT_STOCK"));
        Assert.Equal(1, await CountIdempotencyAsync(context, tenantId, "VARIANT_STOCK", request.Key));
    }

    private static async Task CommitMutationAsync(
        DbContext context,
        ProductMutationIdempotencyService service,
        ProductMutationIdempotencyRequest request,
        string operation)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var claim = await service.TryClaimAsync(request);
        Assert.NotNull(claim.Record);
        Assert.Null(claim.Replay);

        StageMutationMarkers(context, claim.Record!, operation);
        service.Complete(claim.Record, StatusCodes.Status200OK, new { ok = true, operation }, request.ProductId, request.ProductVariantId);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void StageMutationMarkers(DbContext context, ProductRequestIdempotency record, string operation)
    {
        context.Set<AuditLog>().Add(new AuditLog
        {
            TenantId = record.TenantId,
            ActionType = operation,
            EntityType = "Product",
            EntityId = record.ProductId.ToString(),
            Description = operation,
            Source = "GlobalProductMutationIdempotencyTests",
            SuccessFlag = true
        });

        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            TenantId = record.TenantId,
            AggregateId = record.ProductVariantId ?? record.ProductId,
            EventType = $"GlobalProductMutationIdempotencyTests.{operation}",
            EventPayload = "{}",
            EventVersion = 1
        });
    }

    private static ProductMutationIdempotencyRequest Request(
        ProductMutationIdempotencyService service,
        Guid tenantId,
        string operation,
        string key,
        string payload) =>
        new(
            tenantId,
            service.ScopeKey("product", key),
            operation,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DeterministicRequestHasher.ComputeHash(new { operation, payload }));

    private static Task<int> CountAuditAsync(DbContext context, Guid tenantId, string operation) =>
        context.Set<AuditLog>().CountAsync(audit =>
            audit.TenantId == tenantId &&
            audit.Source == "GlobalProductMutationIdempotencyTests" &&
            audit.ActionType == operation);

    private static Task<int> CountOutboxAsync(DbContext context, Guid tenantId, string operation) =>
        context.Set<OutboxMessage>().CountAsync(message =>
            message.TenantId == tenantId &&
            message.EventType == $"GlobalProductMutationIdempotencyTests.{operation}");

    private static Task<int> CountIdempotencyAsync(DbContext context, Guid tenantId, string operation, string key) =>
        context.Set<ProductRequestIdempotency>().CountAsync(record =>
            record.TenantId == tenantId &&
            record.Operation == operation &&
            record.Key == key &&
            record.ResponseStatusCode == StatusCodes.Status200OK);

    private sealed class PostgresScope : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _databaseName;

        private PostgresScope(
            string adminConnectionString,
            string databaseName,
            DbContextOptions<ApplicationDbContext> options)
        {
            _adminConnectionString = adminConnectionString;
            _databaseName = databaseName;
            Options = options;
        }

        private DbContextOptions<ApplicationDbContext> Options { get; }

        public static async Task<PostgresScope?> TryCreateAsync()
        {
            string baseConnectionString;
            try
            {
                baseConnectionString = TestConfiguration.GetRequiredConnectionString();
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("ConnectionStrings:DefaultConnection", StringComparison.Ordinal) &&
                !RealPostgresTestSupport.RequiresRealPostgres())
            {
                return null;
            }

            var baseConnection = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Pooling = false,
                IncludeErrorDetail = true,
                SslMode = SslMode.Disable
            };
            var adminConnection = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
            {
                Database = "postgres"
            };
            var databaseName = $"pos_global_product_idempotency_{Guid.NewGuid():N}";
            var testConnection = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
            {
                Database = databaseName
            };

            try
            {
                await using (var connection = new NpgsqlConnection(adminConnection.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using var create = new NpgsqlCommand(
                        $"CREATE DATABASE \"{databaseName}\" WITH TEMPLATE template0 ENCODING 'UTF8';",
                        connection);
                    await create.ExecuteNonQueryAsync();
                }

                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(testConnection.ConnectionString)
                    .EnableDetailedErrors()
                    .Options;

                await using (var context = new GlobalProductMutationIdempotencyDbContext(
                    options,
                    new TestTenantContext(Guid.NewGuid())))
                {
                    await context.Database.EnsureCreatedAsync();
                }

                return new PostgresScope(adminConnection.ConnectionString, databaseName, options);
            }
            catch when (!RealPostgresTestSupport.RequiresRealPostgres())
            {
                return null;
            }
        }

        public GlobalProductMutationIdempotencyDbContext CreateDbContext(TestTenantContext tenantContext) =>
            new(Options, tenantContext);

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName
                  AND pid <> pg_backend_pid();
                """,
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", _databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\";", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class GlobalProductMutationIdempotencyDbContext : ApplicationDbContext
    {
        public GlobalProductMutationIdempotencyDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var keepTypes = new HashSet<Type>
            {
                typeof(AuditLog),
                typeof(OutboxMessage),
                typeof(ProductRequestIdempotency)
            };

            var discoveredEntityTypes = typeof(ApplicationDbContext)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.PropertyType.IsGenericType &&
                    property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(property => property.PropertyType.GetGenericArguments()[0])
                .Where(entityType => !keepTypes.Contains(entityType))
                .Distinct()
                .ToArray();

            foreach (var entityType in discoveredEntityTypes)
            {
                modelBuilder.Ignore(entityType);
            }

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(audit => audit.LogId);
                entity.Property(audit => audit.LogId).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.ToTable("OutboxMessages");
                entity.HasKey(message => message.Id);
                entity.Property(message => message.EventType).IsRequired();
                entity.Property(message => message.EventPayload).IsRequired();
            });

            modelBuilder.Entity<ProductRequestIdempotency>(entity =>
            {
                entity.ToTable("ProductRequestIdempotency");
                entity.HasKey(record => record.Id);
                entity.HasIndex(record => new { record.TenantId, record.Key, record.Operation }).IsUnique();
                entity.HasIndex(record => new { record.TenantId, record.ProductId });
                entity.HasIndex(record => new { record.TenantId, record.ProductVariantId });
                entity.Property(record => record.Key).HasMaxLength(200).IsRequired();
                entity.Property(record => record.Operation).HasMaxLength(32).IsRequired();
                entity.Property(record => record.RequestFingerprint).HasMaxLength(64).IsRequired();
                entity.Property(record => record.ResponseJson).HasColumnType("text");
            });
        }
    }
}
