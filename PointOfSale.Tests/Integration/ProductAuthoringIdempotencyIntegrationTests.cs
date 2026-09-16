using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class ProductAuthoringIdempotencyIntegrationTests
{
    [Fact]
    public async Task SameKeySamePayload_PersistsOneProductOneProductCreatedOutboxOneAuditAndReplays()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        var command = harness.CreateCreateCommand("same-key-same-payload");

        var first = await harness.CreateOrchestrator().AuthorAsync(command);
        harness.Clear();
        var replay = await harness.CreateOrchestrator().AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, first.Status);
        Assert.Equal(first.Status, replay.Status);
        Assert.Equal(first.ProductId, replay.ProductId);
        Assert.Equal(first.MutationVersion, replay.MutationVersion);
        Assert.Equal(first.PublicationState, replay.PublicationState);
        Assert.Equal(first.CorrelationId, replay.CorrelationId);
        Assert.Empty(replay.ValidationErrors);
        Assert.Single(await harness.CountProductsAsync());
        Assert.Equal(1, await harness.CountProductCreatedOutboxRowsAsync());
        Assert.Equal(1, await harness.CountProductAuthoringAuditRowsAsync());
        Assert.Equal(1, await harness.CountProductIdempotencyRowsAsync("same-key-same-payload"));
        Assert.Equal("idempotent-product", (await harness.CountProductsAsync()).Single().Slug);
    }

    [Fact]
    public async Task SameKeyDifferentPayload_ReturnsConflictAndDoesNotMutateAgain()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        var firstCommand = harness.CreateCreateCommand("same-key-different-payload");
        var conflictingCommand = firstCommand with
        {
            Content = firstCommand.Content with { Name = "Changed product name" }
        };

        var first = await harness.CreateOrchestrator().AuthorAsync(firstCommand);
        harness.Clear();
        var conflict = await harness.CreateOrchestrator().AuthorAsync(conflictingCommand);

        Assert.Equal(ProductAuthoringStatus.Accepted, first.Status);
        Assert.Equal(ProductAuthoringStatus.ValidationFailed, conflict.Status);
        Assert.Contains(conflict.ValidationErrors, error => error.Code == "PRODUCT_AUTHORING_IDEMPOTENCY_CONFLICT");
        Assert.Single(await harness.CountProductsAsync());
        Assert.Equal(1, await harness.CountProductCreatedOutboxRowsAsync());
        Assert.Equal(1, await harness.CountProductAuthoringAuditRowsAsync());
    }

    [Fact]
    public async Task DifferentKeysSamePayload_CreateIndependentOperations()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        var firstCommand = harness.CreateCreateCommand("different-key-one");
        var secondCommand = firstCommand with
        {
            Metadata = firstCommand.Metadata with
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                IdempotencyKey = "different-key-two"
            }
        };

        var first = await harness.CreateOrchestrator().AuthorAsync(firstCommand);
        var second = await harness.CreateOrchestrator().AuthorAsync(secondCommand);

        Assert.Equal(ProductAuthoringStatus.Accepted, first.Status);
        Assert.Equal(ProductAuthoringStatus.Accepted, second.Status);
        Assert.NotEqual(first.ProductId, second.ProductId);
        Assert.Equal(2, (await harness.CountProductsAsync()).Count);
        Assert.Equal(2, await harness.CountProductCreatedOutboxRowsAsync());
        Assert.Equal(2, await harness.CountProductAuthoringAuditRowsAsync());
    }

    [Fact]
    public async Task LostHttpResponseAfterCommit_RetryReplaysWithoutDuplicateMutation()
    {
        var observer = new ThrowAfterCommitObserver();
        await using var harness = await Harness.TryCreateAsync(observer);
        if (harness is null) return;

        var command = harness.CreateCreateCommand("lost-response-after-commit");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CreateOrchestrator().AuthorAsync(command));
        observer.Disable();
        harness.Clear();

        var replay = await harness.CreateOrchestrator().AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.Accepted, replay.Status);
        Assert.Single(await harness.CountProductsAsync());
        Assert.Equal(1, await harness.CountProductCreatedOutboxRowsAsync());
        Assert.Equal(1, await harness.CountProductAuthoringAuditRowsAsync());
    }

    [Fact]
    public async Task ConcurrentRetries_WithSameKeySamePayload_CreateExactlyOneMutation()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        var command = harness.CreateCreateCommand("concurrent-retry-key");

        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(async () =>
            {
                await using var scopedHarness = harness.CreateSiblingHarness();
                return await scopedHarness.CreateOrchestrator().AuthorAsync(command);
            })));

        Assert.Contains(results, result => result.Status == ProductAuthoringStatus.Accepted);
        Assert.All(results, result => Assert.Contains(
            result.Status,
            new[] { ProductAuthoringStatus.Accepted, ProductAuthoringStatus.ConcurrencyConflict }));
        Assert.Single(await harness.CountProductsAsync());
        Assert.Equal(1, await harness.CountProductCreatedOutboxRowsAsync());
        Assert.Equal(1, await harness.CountProductAuthoringAuditRowsAsync());

        harness.Clear();
        var replay = await harness.CreateOrchestrator().AuthorAsync(command);
        Assert.Equal(ProductAuthoringStatus.Accepted, replay.Status);
    }

    [Fact]
    public async Task ConcurrentCreates_WithDifferentKeysAndSameBaseSlug_PersistDistinctSlugs()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        var firstCommand = harness.CreateCreateCommand("concurrent-slug-one");
        var secondCommand = firstCommand with
        {
            Metadata = firstCommand.Metadata with
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                IdempotencyKey = "concurrent-slug-two"
            }
        };

        var results = await Task.WhenAll(
            Task.Run(async () =>
            {
                await using var scopedHarness = harness.CreateSiblingHarness();
                return await scopedHarness.CreateOrchestrator().AuthorAsync(firstCommand);
            }),
            Task.Run(async () =>
            {
                await using var scopedHarness = harness.CreateSiblingHarness();
                return await scopedHarness.CreateOrchestrator().AuthorAsync(secondCommand);
            }));

        Assert.All(results, result => Assert.Equal(ProductAuthoringStatus.Accepted, result.Status));
        var slugs = (await harness.CountProductsAsync())
            .Select(product => product.Slug)
            .OrderBy(slug => slug, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["idempotent-product", "idempotent-product-2"], slugs);
    }

    [Fact]
    public async Task Create_WhenHistoricalSlugDiffersOnlyByCase_AppendsCollisionSuffix()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        await harness.SeedHistoricalProductAsync("Idempotent-Product");

        var result = await harness.CreateOrchestrator().AuthorAsync(
            harness.CreateCreateCommand("mixed-case-slug"));

        Assert.Equal(ProductAuthoringStatus.Accepted, result.Status);
        var created = (await harness.CountProductsAsync()).Single(product => product.Id == result.ProductId);
        Assert.Equal("idempotent-product-2", created.Slug);
    }

    [Fact]
    public async Task Create_SameSlugInDifferentTenants_DoesNotCreateCrossTenantCollision()
    {
        await using var firstTenant = await Harness.TryCreateAsync();
        if (firstTenant is null) return;
        await using var secondTenant = await firstTenant.CreateAdditionalTenantHarnessAsync(Guid.NewGuid());

        var first = await firstTenant.CreateOrchestrator().AuthorAsync(
            firstTenant.CreateCreateCommand("tenant-one-slug"));
        var second = await secondTenant.CreateOrchestrator().AuthorAsync(
            secondTenant.CreateCreateCommand("tenant-two-slug"));

        Assert.Equal(ProductAuthoringStatus.Accepted, first.Status);
        Assert.Equal(ProductAuthoringStatus.Accepted, second.Status);
        Assert.Equal("idempotent-product", (await firstTenant.CountProductsAsync()).Single().Slug);
        Assert.Equal("idempotent-product", (await secondTenant.CountProductsAsync()).Single().Slug);
    }

    [Fact]
    public async Task Update_WithBlankSlugAndChangedName_PreservesStoredSlug()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        var createCommand = harness.CreateCreateCommand("stable-slug-create");
        var createResult = await harness.CreateOrchestrator().AuthorAsync(createCommand);
        Assert.Equal(ProductAuthoringStatus.Accepted, createResult.Status);

        harness.Clear();
        var stored = (await harness.CountProductsAsync()).Single();
        var updateCommand = createCommand with
        {
            Operation = ProductAuthoringOperation.Update,
            Identity = createCommand.Identity with { ProductId = stored.Id },
            Content = createCommand.Content with { Name = "Renamed product" },
            Metadata = createCommand.Metadata with
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                IdempotencyKey = "stable-slug-update",
                ExpectedVersion = stored.RowVersion
            }
        };

        var updateResult = await harness.CreateOrchestrator().AuthorAsync(updateCommand);

        Assert.Equal(ProductAuthoringStatus.Accepted, updateResult.Status);
        harness.Clear();
        var updated = (await harness.CountProductsAsync()).Single();
        Assert.Equal("Renamed product", updated.Name);
        Assert.Equal("idempotent-product", updated.Slug);
    }

    [Fact]
    public async Task MissingIdempotencyKey_IsRejectedBeforeMutation()
    {
        await using var harness = await Harness.TryCreateAsync();
        if (harness is null) return;

        var command = harness.CreateCreateCommand(null);

        var result = await harness.CreateOrchestrator().AuthorAsync(command);

        Assert.Equal(ProductAuthoringStatus.ValidationFailed, result.Status);
        Assert.Contains(result.ValidationErrors, error => error.Code == "PRODUCT_AUTHORING_IDEMPOTENCY_KEY_REQUIRED");
        Assert.Empty(await harness.CountProductsAsync());
        Assert.Equal(0, await harness.CountProductCreatedOutboxRowsAsync());
        Assert.Equal(0, await harness.CountProductAuthoringAuditRowsAsync());
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly PostgresScope? _scope;
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private readonly IProductAuthoringTransactionObserver _observer;

        private Harness(
            PostgresScope? scope,
            DbContextOptions<ApplicationDbContext> options,
            ProductAuthoringIdempotencyDbContext context,
            TestTenantContext tenantContext,
            Guid categoryId,
            Guid unitId,
            IProductAuthoringTransactionObserver observer)
        {
            _scope = scope;
            _options = options;
            Context = context;
            TenantContext = tenantContext;
            CategoryId = categoryId;
            UnitId = unitId;
            _observer = observer;
        }

        private ProductAuthoringIdempotencyDbContext Context { get; }
        private TestTenantContext TenantContext { get; }
        private Guid CategoryId { get; }
        private Guid UnitId { get; }

        public static async Task<Harness?> TryCreateAsync(IProductAuthoringTransactionObserver? observer = null)
        {
            var scope = await PostgresScope.TryCreateAsync();
            if (scope is null)
            {
                return null;
            }

            var tenantId = Guid.NewGuid();
            var tenantContext = new TestTenantContext(tenantId);
            var context = scope.CreateDbContext(tenantContext);
            var category = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Idempotency"
            };
            var unit = new PointOfSale.Core.Entities.Unit
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Piece",
                Symbol = "pc",
                IsActive = true
            };

            context.Categories.Add(category);
            context.Units.Add(unit);
            await context.SaveChangesAsync();

            return new Harness(
                scope,
                scope.Options,
                context,
                tenantContext,
                category.Id,
                unit.Id,
                observer ?? NoOpProductAuthoringTransactionObserver.Instance);
        }

        public Harness CreateSiblingHarness()
        {
            if (_scope is null)
            {
                throw new InvalidOperationException("A persisted test scope is required.");
            }

            return new Harness(
                null,
                _options,
                new ProductAuthoringIdempotencyDbContext(_options, TenantContext),
                TenantContext,
                CategoryId,
                UnitId,
                _observer);
        }

        public async Task<Harness> CreateAdditionalTenantHarnessAsync(Guid tenantId)
        {
            var tenantContext = new TestTenantContext(tenantId);
            var context = new ProductAuthoringIdempotencyDbContext(_options, tenantContext);
            var category = new Category
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Additional tenant"
            };
            var unit = new PointOfSale.Core.Entities.Unit
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Piece",
                Symbol = "pc",
                IsActive = true
            };
            context.Categories.Add(category);
            context.Units.Add(unit);
            await context.SaveChangesAsync();

            return new Harness(
                null,
                _options,
                context,
                tenantContext,
                category.Id,
                unit.Id,
                _observer);
        }

        public ProductAuthoringOrchestrator CreateOrchestrator()
        {
            var eventPublisher = new PersistedOutboxEventPublisher(Context);
            return new ProductAuthoringOrchestrator(
                new ProductAuthoringPolicy(new ProductAuthoringValidator()),
                Context,
                TenantContext,
                new ProductVariantService(Context, eventPublisher, NullLogger<ProductVariantService>.Instance),
                eventPublisher,
                new NoOpMarketplaceEventOutbox(),
                NullLogger<ProductAuthoringOrchestrator>.Instance,
                new HttpContextAccessor(),
                new ProductMutationIdempotencyService(Context),
                _observer,
                new ProductAuthoringReconstructionService(Context, TenantContext));
        }

        public ProductAuthoringCommand CreateCreateCommand(string? idempotencyKey) =>
            new(
                ProductAuthoringOperation.Create,
                new ProductAuthoringIdentity(TenantContext.TenantId!.Value, null, CategoryId, null, null),
                new ProductAuthoringContent("Idempotent product", null, null, null, null, null, [], [], []),
                new ProductAuthoringPricing(5m, 12m, 12m, false, 0m),
                new ProductAuthoringPublication(PublishStatus.Published, true, true, true, false),
                new ProductAuthoringSeo(null, null, null, null),
                new ProductAuthoringShippingProfile(0m, 0m, 0m, 0m, "kg", "cm"),
                new ProductAuthoringInventoryProfile("Piece", 0m, true, false, 10, false),
                new ProductAuthoringUnits(
                    UnitId,
                    UnitId,
                    [new ProductAuthoringUnit(null, UnitId, 1m, 5m, 12m, null, true, true)]),
                [],
                new ProductAuthoringMetadata(
                    false,
                    null,
                    null,
                    "default",
                    Guid.NewGuid().ToString("N"),
                    idempotencyKey,
                    null,
                    new Dictionary<string, string>
                    {
                        ["productUnitsSubmitted"] = "true",
                        ["variantsSubmitted"] = "true"
                    }));

        public void Clear() => Context.ChangeTracker.Clear();

        public async Task<List<Product>> CountProductsAsync() =>
            await Context.Products.IgnoreQueryFilters()
                .Where(product => product.TenantId == TenantContext.TenantId)
                .ToListAsync();

        public async Task<int> CountProductCreatedOutboxRowsAsync() =>
            await Context.OutboxMessages
                .CountAsync(message =>
                    message.TenantId == TenantContext.TenantId &&
                    message.EventType.EndsWith(nameof(ProductCreatedEvent)));

        public async Task<int> CountProductAuthoringAuditRowsAsync() =>
            await Context.AuditLogs
                .CountAsync(audit =>
                    audit.TenantId == TenantContext.TenantId &&
                    audit.Source == "ProductAuthoring");

        public async Task<int> CountProductIdempotencyRowsAsync(string key) =>
            await Context.ProductRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .CountAsync(record =>
                    record.TenantId == TenantContext.TenantId &&
                    record.Key == $"authoring:{key}");

        public async Task SeedHistoricalProductAsync(string slug)
        {
            Context.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantContext.TenantId!.Value,
                Name = "Historical product",
                CategoryId = CategoryId,
                Unit = "Piece",
                Slug = slug,
                PublishStatus = PublishStatus.Draft,
                CreatedAt = DateTime.UtcNow
            });
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            if (_scope is not null)
            {
                await _scope.DisposeAsync();
            }
        }
    }

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

        public DbContextOptions<ApplicationDbContext> Options { get; }

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
            var databaseName = $"pos_product_idempotency_{Guid.NewGuid():N}";
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

                await using (var context = new ProductAuthoringIdempotencyDbContext(
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

        public ProductAuthoringIdempotencyDbContext CreateDbContext(TestTenantContext tenantContext) =>
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

    private sealed class ProductAuthoringIdempotencyDbContext : ApplicationDbContext
    {
        public ProductAuthoringIdempotencyDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var keepTypes = new HashSet<Type>
            {
                typeof(Category),
                typeof(Product),
                typeof(PointOfSale.Core.Entities.Unit),
                typeof(ProductUnit),
                typeof(ProductVariant),
                typeof(ProductVariantAttribute),
                typeof(AuditLog),
                typeof(PointOfSale.Core.Entities.OutboxMessage),
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

            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("Categories");
                entity.HasKey(category => category.Id);
                entity.Property(category => category.Name).IsRequired();
                entity.Ignore(category => category.ParentCategory);
                entity.Ignore(category => category.SubCategories);
                entity.Ignore(category => category.Products);
            });

            modelBuilder.Entity<PointOfSale.Core.Entities.Unit>(entity =>
            {
                entity.ToTable("Units");
                entity.HasKey(unit => unit.Id);
                entity.Property(unit => unit.Name).IsRequired();
                entity.Property(unit => unit.Symbol).IsRequired();
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("Products");
                entity.HasKey(product => product.Id);
                entity.Property(product => product.Name).IsRequired();
                entity.Property(product => product.Unit).IsRequired();
                entity.Property(product => product.WeightUnit).IsRequired();
                entity.Property(product => product.DimensionUnit).IsRequired();
                entity.Property(product => product.RowVersion)
                    .IsRowVersion()
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate();
                entity.HasOne(product => product.Category)
                    .WithMany()
                    .HasForeignKey(product => product.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Ignore(product => product.Media);
                entity.Ignore(product => product.WarehouseStocks);
            });

            modelBuilder.Entity<ProductUnit>(entity =>
            {
                entity.ToTable("ProductUnits");
                entity.HasKey(productUnit => productUnit.Id);
                entity.HasOne(productUnit => productUnit.Product)
                    .WithMany(product => product.ProductUnits)
                    .HasForeignKey(productUnit => productUnit.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(productUnit => productUnit.Unit)
                    .WithMany()
                    .HasForeignKey(productUnit => productUnit.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductVariant>(entity =>
            {
                entity.ToTable("ProductVariants");
                entity.HasKey(variant => variant.Id);
                entity.HasOne(variant => variant.Product)
                    .WithMany(product => product.Variants)
                    .HasForeignKey(variant => variant.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.Ignore(variant => variant.Stocks);
                entity.Ignore(variant => variant.Media);
            });

            modelBuilder.Entity<ProductVariantAttribute>(entity =>
            {
                entity.ToTable("ProductVariantAttributes");
                entity.HasKey(attribute => attribute.Id);
                entity.HasOne(attribute => attribute.Variant)
                    .WithMany(variant => variant.Attributes)
                    .HasForeignKey(attribute => attribute.VariantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(audit => audit.LogId);
                entity.Property(audit => audit.LogId).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<PointOfSale.Core.Entities.OutboxMessage>(entity =>
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

    private sealed class ThrowAfterCommitObserver : IProductAuthoringTransactionObserver
    {
        private bool _enabled = true;

        public void Disable() => _enabled = false;

        public Task AfterUnitSynchronizationAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AfterVariantSynchronizationAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BeforeCommitAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AfterCommitAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken)
        {
            if (_enabled)
            {
                throw new InvalidOperationException("Simulated lost HTTP response after product authoring commit.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class PersistedOutboxEventPublisher(ApplicationDbContext context) : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Persist(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                Persist(@event);
            }

            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            PublishAsync(@event, cancellationToken);

        private void Persist(IntegrationEvent @event)
        {
            var type = @event.GetType();
            context.OutboxMessages.Add(new PointOfSale.Core.Entities.OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = type.FullName ?? type.Name,
                EventPayload = JsonSerializer.Serialize(@event, type),
                TenantId = ReadGuid(@event, "TenantId"),
                AggregateId = ReadGuid(@event, "ProductId") ?? ReadGuid(@event, "VariantId"),
                EventVersion = ReadInt(@event, "EventVersion"),
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        private static Guid? ReadGuid(object value, string propertyName) =>
            value.GetType().GetProperty(propertyName)?.GetValue(value) is Guid guid ? guid : null;

        private static int ReadInt(object value, string propertyName) =>
            value.GetType().GetProperty(propertyName)?.GetValue(value) is int intValue ? intValue : 0;
    }

    private sealed class NoOpMarketplaceEventOutbox : IMarketplaceEventOutboxService
    {
        public Task EnqueueAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, PointOfSale.Core.Events.Marketplace.IMarketplaceDomainEvent =>
            Task.CompletedTask;
    }

    private sealed class NoOpProductAuthoringTransactionObserver : IProductAuthoringTransactionObserver
    {
        public static NoOpProductAuthoringTransactionObserver Instance { get; } = new();

        public Task AfterUnitSynchronizationAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AfterVariantSynchronizationAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BeforeCommitAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AfterCommitAsync(ProductAuthoringCommand command, Product product, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
