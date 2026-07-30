using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Configuration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using System.Reflection;

namespace PointOfSale.Tests.Integration;

public sealed class ProductAuthoringConcurrencyIntegrationTests
{
    [Fact]
    public async Task Update_WithTwoDbContexts_SecondProductSaveFailsAndRetrySucceeds()
    {
        await using var scope = TryOpenScope();
        if (scope is null)
        {
            return;
        }

        var productId = await SeedProductAsync(scope);

        await AssertStaleWriterFailsAndRetrySucceedsAsync(
            scope,
            productId,
            writerA => writerA.Name = "Writer A update",
            writerB => writerB.Name = "Writer B stale update",
            retry => retry.Name = "Writer B retry update",
            verify => Assert.Equal("Writer B retry update", verify.Name));
    }

    [Fact]
    public async Task Publish_WithTwoDbContexts_SecondProductSaveFailsAndRetrySucceeds()
    {
        await using var scope = TryOpenScope();
        if (scope is null)
        {
            return;
        }

        var productId = await SeedProductAsync(scope);

        await AssertStaleWriterFailsAndRetrySucceedsAsync(
            scope,
            productId,
            writerA =>
            {
                writerA.PublishStatus = PublishStatus.Published;
                writerA.IsOnlineVisible = true;
            },
            writerB =>
            {
                writerB.PublishStatus = PublishStatus.Published;
                writerB.IsOnlineVisible = true;
                writerB.IsFeatured = true;
            },
            retry =>
            {
                retry.PublishStatus = PublishStatus.Published;
                retry.IsOnlineVisible = true;
                retry.IsFeatured = true;
            },
            verify =>
            {
                Assert.Equal(PublishStatus.Published, verify.PublishStatus);
                Assert.True(verify.IsOnlineVisible);
                Assert.True(verify.IsFeatured);
            });
    }

    [Fact]
    public async Task Archive_WithTwoDbContexts_SecondProductSaveFailsAndRetrySucceeds()
    {
        await using var scope = TryOpenScope();
        if (scope is null)
        {
            return;
        }

        var productId = await SeedProductAsync(scope);

        await AssertStaleWriterFailsAndRetrySucceedsAsync(
            scope,
            productId,
            writerA =>
            {
                writerA.IsActive = false;
                writerA.IsOnlineVisible = false;
                writerA.PublishStatus = PublishStatus.Archived;
            },
            writerB =>
            {
                writerB.IsActive = false;
                writerB.IsOnlineVisible = false;
                writerB.PublishStatus = PublishStatus.Archived;
                writerB.Notes = "stale archive note";
            },
            retry =>
            {
                retry.IsActive = false;
                retry.IsOnlineVisible = false;
                retry.PublishStatus = PublishStatus.Archived;
                retry.Notes = "retry archive note";
            },
            verify =>
            {
                Assert.False(verify.IsActive);
                Assert.False(verify.IsOnlineVisible);
                Assert.Equal(PublishStatus.Archived, verify.PublishStatus);
                Assert.Equal("retry archive note", verify.Notes);
            });
    }

    [Fact]
    public async Task UnitModification_WhenProductStateChanges_SecondProductSaveFailsAndRetrySucceeds()
    {
        await using var scope = TryOpenScope();
        if (scope is null)
        {
            return;
        }

        var productId = await SeedProductAsync(scope);

        await AssertStaleWriterFailsAndRetrySucceedsAsync(
            scope,
            productId,
            writerA =>
            {
                writerA.ProductUnits.Single().SellingPrice = 13m;
                writerA.UpdatedAt = DateTime.UtcNow;
            },
            writerB =>
            {
                writerB.ProductUnits.Single().SellingPrice = 14m;
                writerB.UpdatedAt = DateTime.UtcNow;
            },
            retry =>
            {
                retry.ProductUnits.Single().SellingPrice = 14m;
                retry.UpdatedAt = DateTime.UtcNow;
            },
            verify => Assert.Equal(14m, verify.ProductUnits.Single().SellingPrice));
    }

    [Fact]
    public async Task VariantModification_WhenProductStateChanges_SecondProductSaveFailsAndRetrySucceeds()
    {
        await using var scope = TryOpenScope();
        if (scope is null)
        {
            return;
        }

        var productId = await SeedProductAsync(scope);

        await AssertStaleWriterFailsAndRetrySucceedsAsync(
            scope,
            productId,
            writerA =>
            {
                writerA.Variants.Single().Price = 13m;
                writerA.UpdatedAt = DateTime.UtcNow;
            },
            writerB =>
            {
                writerB.Variants.Single().Price = 14m;
                writerB.UpdatedAt = DateTime.UtcNow;
            },
            retry =>
            {
                retry.Variants.Single().Price = 14m;
                retry.UpdatedAt = DateTime.UtcNow;
            },
            verify => Assert.Equal(14m, verify.Variants.Single().Price));
    }

    private static Scope? TryOpenScope()
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

        return Scope.CreateAsync(baseConnectionString).GetAwaiter().GetResult();
    }

    private static async Task<Guid> SeedProductAsync(Scope scope)
    {
        await using var context = scope.CreateDbContext();

        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = scope.TenantId,
            Name = "Concurrency"
        };
        var unit = new PointOfSale.Core.Entities.Unit
        {
            Id = Guid.NewGuid(),
            TenantId = scope.TenantId,
            Name = "Piece",
            Symbol = "pc",
            IsActive = true
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = scope.TenantId,
            Name = "Concurrency product",
            CategoryId = category.Id,
            PurchasePrice = 5m,
            SalePrice = 12m,
            OnlinePrice = 12m,
            Unit = "Piece",
            IsActive = true,
            TrackStock = true,
            PublishStatus = PublishStatus.Draft,
            IsOnlineVisible = false,
            IsSearchable = true,
            CreatedAt = DateTime.UtcNow
        };
        var productUnit = new ProductUnit
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UnitId = unit.Id,
            ConversionFactor = 1m,
            PurchasePrice = 5m,
            SellingPrice = 12m,
            IsBaseUnit = true,
            IsDefault = true
        };
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = scope.TenantId,
            ProductId = product.Id,
            SKU = "CONC-SKU",
            Price = 12m,
            OnlinePrice = 12m,
            CostPrice = 5m,
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Categories.Add(category);
        context.Units.Add(unit);
        context.Products.Add(product);
        context.ProductUnits.Add(productUnit);
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private static async Task AssertStaleWriterFailsAndRetrySucceedsAsync(
        Scope scope,
        Guid productId,
        Action<Product> mutateWriterA,
        Action<Product> mutateWriterB,
        Action<Product> mutateRetry,
        Action<Product> verify)
    {
        await using var writerAContext = scope.CreateDbContext();
        await using var writerBContext = scope.CreateDbContext();

        var writerAProduct = await LoadProductAggregateAsync(writerAContext, productId);
        var writerBProduct = await LoadProductAggregateAsync(writerBContext, productId);
        Assert.Equal(writerAProduct.RowVersion, writerBProduct.RowVersion);

        mutateWriterA(writerAProduct);
        await writerAContext.SaveChangesAsync();

        mutateWriterB(writerBProduct);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => writerBContext.SaveChangesAsync());

        writerBContext.ChangeTracker.Clear();
        var retryProduct = await LoadProductAggregateAsync(writerBContext, productId);
        mutateRetry(retryProduct);
        await writerBContext.SaveChangesAsync();

        await using var verifyContext = scope.CreateDbContext();
        var verified = await LoadProductAggregateAsync(verifyContext, productId);
        verify(verified);
    }

    private static Task<Product> LoadProductAggregateAsync(ApplicationDbContext context, Guid productId) =>
        context.Products
            .Include(product => product.ProductUnits)
            .Include(product => product.Variants)
            .SingleAsync(product => product.Id == productId);

    private sealed class Scope : IAsyncDisposable
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private readonly string _adminConnectionString;
        private readonly string _databaseName;
        private readonly TestTenantContext _tenantContext;

        private Scope(
            DbContextOptions<ApplicationDbContext> options,
            string adminConnectionString,
            string databaseName,
            Guid tenantId)
        {
            _options = options;
            _adminConnectionString = adminConnectionString;
            _databaseName = databaseName;
            TenantId = tenantId;
            _tenantContext = new TestTenantContext(tenantId);
        }

        public Guid TenantId { get; }

        public static async Task<Scope> CreateAsync(string baseConnectionString)
        {
            var normalizedConnectionString =
                PostgresConnectionStringPolicy.Normalize(baseConnectionString, isDevelopment: true)
                ?? baseConnectionString;

            var baseConnection = new NpgsqlConnectionStringBuilder(normalizedConnectionString)
            {
                Pooling = false,
                IncludeErrorDetail = true,
                SslMode = SslMode.Disable,
                TrustServerCertificate = true
            };
            baseConnection["Timezone"] = "UTC";

            var databaseName = $"product_authoring_concurrency_{Guid.NewGuid():N}";
            var connectionString = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
            {
                Database = databaseName
            }.ConnectionString;
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
            {
                Database = "postgres"
            }.ConnectionString;

            await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
            {
                await adminConnection.OpenAsync();
                await using var createCommand = new NpgsqlCommand(
                    $"CREATE DATABASE {QuoteIdentifier(databaseName)};",
                    adminConnection);
                await createCommand.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString)
                .EnableDetailedErrors()
                .Options;

            await using (var context = new ProductAuthoringConcurrencyDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
            }

            return new Scope(options, adminConnectionString, databaseName, Guid.NewGuid());
        }

        public ApplicationDbContext CreateDbContext() =>
            new ProductAuthoringConcurrencyDbContext(_options, _tenantContext);

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();

            await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
            await adminConnection.OpenAsync();

            await using (var terminateCommand = new NpgsqlCommand(
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName
                  AND pid <> pg_backend_pid();
                """,
                adminConnection))
            {
                terminateCommand.Parameters.AddWithValue("databaseName", _databaseName);
                await terminateCommand.ExecuteNonQueryAsync();
            }

            await using var dropCommand = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS {QuoteIdentifier(_databaseName)};",
                adminConnection);
            await dropCommand.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }
    }

    private sealed class ProductAuthoringConcurrencyDbContext : ApplicationDbContext
    {
        public ProductAuthoringConcurrencyDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public ProductAuthoringConcurrencyDbContext(
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
                typeof(ProductVariant)
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
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .IsRowVersion();
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
                entity.Property(variant => variant.RowVersion)
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .IsRowVersion();
                entity.HasOne(variant => variant.Product)
                    .WithMany(product => product.Variants)
                    .HasForeignKey(variant => variant.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.Ignore(variant => variant.Attributes);
                entity.Ignore(variant => variant.Stocks);
                entity.Ignore(variant => variant.Media);
            });
        }
    }
}
