using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Storefront;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Infrastructure;

public sealed class NpgsqlAcquisitionRemediationTests
{
    [Fact]
    public async Task ResolveSlug_FindsCommittedSqlProductBeforeMongoProjectionCatchesUp()
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("new-sql-product");
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        fixture.Context.Tenants.Add(tenant);
        fixture.Context.Categories.Add(new Category
        {
            Id = categoryId, TenantId = tenant.Id, Name = "Current", CreatedAt = DateTime.UtcNow
        });
        fixture.Context.Products.Add(new Product
        {
            Id = productId, TenantId = tenant.Id, CategoryId = categoryId,
            Name = "Immediately visible", SKU = "SQL-SLUG-1", Slug = "just-created",
            IsActive = true, IsOnlineVisible = true, IsSearchable = true,
            PublishStatus = PublishStatus.Published, CreatedAt = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var settings = Options.Create(new RedisSettings { Enabled = false, InstanceName = "sql-product-fixture" });
        using var redis = new RedisConnectionFactory(settings, NullLogger<RedisConnectionFactory>.Instance);
        var repo = new Mock<IProductReadRepository>();
        repo.Setup(r => r.FindBySlugAsync(tenant.Id, "just-created", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PointOfSale.Infrastructure.MongoDB.Documents.ProductDocument?)null);
        var seo = new SeoService(
            fixture.Context, repo.Object, Mock.Of<ICategoryReadRepository>(), redis, settings,
            new StorefrontUrlResolver(new ConfigurationBuilder().Build(),
                NullLogger<StorefrontUrlResolver>.Instance, Mock.Of<IServiceScopeFactory>()),
            NullLogger<SeoService>.Instance);

        var resolution = await seo.ResolveSlugAsync(tenant.Id, "just-created", "new-product");
        Assert.Equal(productId, resolution?.Id);
        Assert.Equal("product", resolution?.Type);
    }

    [Fact]
    public async Task ResolveSlug_WarmCmsCandidate_UsesOneTenantScopedSqlCommand_AndRejectsUnpublishedPage()
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("cms-slug");
        var otherTenant = ActiveTenant("other-cms");
        fixture.Context.Tenants.AddRange(tenant, otherTenant);
        var page = new CmsPage
        {
            Id = Guid.NewGuid(), PageGroupId = Guid.NewGuid(), TenantId = tenant.Id,
            Slug = "terms", Title = "Terms", Content = "Text",
            ContentType = CmsPageContentType.RichText, Version = 1, SchemaVersion = 1,
            IsPublished = true, IsDraft = false, PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        fixture.Context.CmsPages.Add(page);
        await fixture.Context.SaveChangesAsync();

        var redisDb = new Mock<IDatabase>();
        redisDb.Setup(redis => redis.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)JsonSerializer.Serialize(new SlugResolutionDto
            {
                Type = "cms", Id = page.PageGroupId, CanonicalSlug = "terms", Name = "Terms"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        redisDb.Setup(redis => redis.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(redis => redis.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(redisDb.Object);
        var settings = Options.Create(new RedisSettings { Enabled = true, InstanceName = "cms-fixture" });
        using var redisFactory = new RedisConnectionFactory(
            settings, NullLogger<RedisConnectionFactory>.Instance, mux.Object);
        var productRepo = new Mock<IProductReadRepository>();
        productRepo.Setup(repo => repo.FindBySlugAsync(
                It.IsAny<Guid>(), "terms", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PointOfSale.Infrastructure.MongoDB.Documents.ProductDocument?)null);
        var seo = new SeoService(
            fixture.Context, productRepo.Object, Mock.Of<ICategoryReadRepository>(),
            redisFactory, settings,
            new StorefrontUrlResolver(new ConfigurationBuilder().Build(),
                NullLogger<StorefrontUrlResolver>.Instance, Mock.Of<IServiceScopeFactory>()),
            NullLogger<SeoService>.Instance);

        fixture.Counter.Reset();
        Assert.Equal(page.PageGroupId, (await seo.ResolveSlugAsync(tenant.Id, "terms", "warm-cms"))?.Id);
        Assert.Equal(1, fixture.Counter.CommandCount);

        fixture.Counter.Reset();
        Assert.Null(await seo.ResolveSlugAsync(otherTenant.Id, "terms", "foreign-cms"));
        Assert.True(fixture.Counter.CommandCount > 0);

        page.IsPublished = false;
        await fixture.Context.SaveChangesAsync();
        fixture.Counter.Reset();
        Assert.Null(await seo.ResolveSlugAsync(tenant.Id, "terms", "unpublished-cms"));
        // Warm candidate validation is one command; the fallback must check
        // current product/category/CMS authority before returning not-found.
        Assert.Equal(5, fixture.Counter.CommandCount);
    }

    [Fact]
    public async Task ResolveSlug_StaleCategoryCandidate_FindsNewSqlOwnerWithoutRepeatingCategorySnapshot()
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("slug-reassigned");
        var oldCategoryId = Guid.NewGuid();
        var newCategoryId = Guid.NewGuid();
        fixture.Context.Tenants.Add(tenant);
        fixture.Context.Categories.AddRange(
            new Category { Id = oldCategoryId, TenantId = tenant.Id, Name = "renamed", CreatedAt = DateTime.UtcNow },
            new Category { Id = newCategoryId, TenantId = tenant.Id, Name = "claimed", CreatedAt = DateTime.UtcNow });
        await fixture.Context.SaveChangesAsync();

        var oldCandidate = new SlugResolutionDto
        {
            Type = "category", Id = oldCategoryId, CanonicalSlug = "claimed", Name = "old"
        };
        var database = new Mock<IDatabase>();
        database.Setup(redis => redis.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)JsonSerializer.Serialize(oldCandidate, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        database.Setup(redis => redis.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        database.Setup(redis => redis.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(redis => redis.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        var settings = Options.Create(new RedisSettings { Enabled = true, InstanceName = "slug-fixture" });
        var productRepository = new Mock<IProductReadRepository>();
        productRepository.Setup(repo => repo.FindBySlugAsync(
                tenant.Id, "claimed", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PointOfSale.Infrastructure.MongoDB.Documents.ProductDocument?)null);
        using var redis = new RedisConnectionFactory(
            settings, NullLogger<RedisConnectionFactory>.Instance, multiplexer.Object);
        var seo = new SeoService(
            fixture.Context, productRepository.Object, Mock.Of<ICategoryReadRepository>(), redis,
            settings,
            new StorefrontUrlResolver(new ConfigurationBuilder().Build(),
                NullLogger<StorefrontUrlResolver>.Instance, Mock.Of<IServiceScopeFactory>()),
            NullLogger<SeoService>.Instance);

        fixture.Counter.Reset();
        var resolved = await seo.ResolveSlugAsync(tenant.Id, "claimed", "stale-category");

        Assert.Equal(newCategoryId, resolved?.Id);
        Assert.Equal("category", resolved?.Type);
        // One SQL slug check (Mongo may lag a newly committed product claim),
        // one active-tenant query and one reusable category-lineage snapshot.
        Assert.Equal(3, fixture.Counter.CommandCount);
        database.Verify(redis => redis.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 7)]
    public async Task UnifiedSearch_EmptyOrInactiveProductOnlyCandidates_DoNotRepeatTenantSql(
        bool includeProduct, bool tenantActive, int expectedCommands)
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("unified-search");
        tenant.Status = tenantActive ? TenantStatus.Active : TenantStatus.Suspended;
        fixture.Context.Tenants.Add(tenant);
        var productId = Guid.NewGuid();
        if (includeProduct)
        {
            var categoryId = Guid.NewGuid();
            fixture.Context.Categories.Add(new Category
            {
                Id = categoryId, TenantId = tenant.Id, Name = "Category", CreatedAt = DateTime.UtcNow
            });
            fixture.Context.Products.Add(new Product
            {
                Id = productId, TenantId = tenant.Id, CategoryId = categoryId,
                Name = "Unavailable", SKU = "SEARCH-1", IsActive = true,
                IsOnlineVisible = true, IsSearchable = true,
                PublishStatus = PublishStatus.Published, CreatedAt = DateTime.UtcNow
            });
        }

        await fixture.Context.SaveChangesAsync();
        fixture.TenantContext.SetTenantId(tenant.Id);
        var candidates = includeProduct
            ? new List<UnifiedSearchItem>
            {
                new() { TenantId = tenant.Id, EntityId = productId, EntityType = "product" }
            }
            : new List<UnifiedSearchItem>();
        var searchIndex = new Mock<IUnifiedSearchIndexService>();
        searchIndex.Setup(index => index.SearchAsync(
                tenant.Id, It.IsAny<string>(), It.IsAny<string[]?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnifiedSearchResult.Success(candidates, candidates.Count, 0, "test", 1, 20));
        using var telemetry = new CommerceReliabilityTelemetry();
        var controller = new StorefrontSearchController(
            searchIndex.Object, fixture.Context, telemetry,
            NullLogger<StorefrontSearchController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection()
                        .AddSingleton<PointOfSale.Core.Interfaces.ITenantContext>(fixture.TenantContext)
                        .BuildServiceProvider()
                }
            }
        };

        fixture.Counter.Reset();
        var result = await controller.Search("test", cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsType<UnifiedSearchResponse>(ok.Value).Items;
        if (tenantActive)
        {
            Assert.Equal(productId, Assert.Single(items).EntityId);
        }
        else
        {
            Assert.Empty(items);
        }
        Assert.Equal(expectedCommands, fixture.Counter.CommandCount);
    }

    [Theory]
    [InlineData("live", TenantStatus.Active, true, false, true)]
    [InlineData("draft", TenantStatus.Active, true, false, false)]
    [InlineData("live", TenantStatus.Suspended, true, false, false)]
    [InlineData("live", TenantStatus.Active, false, false, false)]
    [InlineData("live", TenantStatus.Active, true, true, false)]
    public async Task PublicationAuthority_OneJoinedCommand_RequiresFullyActiveLiveTenant(
        string storefrontStatus, TenantStatus status, bool isActive,
        bool deleted, bool expectedPublished)
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("publication");
        tenant.Status = status;
        tenant.IsActive = isActive;
        tenant.DeletedAtUtc = deleted ? DateTime.UtcNow : null;
        fixture.Context.Tenants.Add(tenant);
        fixture.Context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, StoreName = "Publication",
            StorefrontStatus = storefrontStatus
        });
        await fixture.Context.SaveChangesAsync();
        fixture.TenantContext.SetTenantId(tenant.Id);

        var service = new StorefrontPublicationStatusService(
            fixture.Context, NullLogger<StorefrontPublicationStatusService>.Instance);
        fixture.Counter.Reset();
        Assert.Equal(expectedPublished, await service.IsPublishedAsync(tenant.Id));
        Assert.Equal(1, fixture.Counter.CommandCount);

        fixture.Counter.Reset();
        Assert.False(await service.IsPublishedAsync(Guid.NewGuid()));
        Assert.Equal(1, fixture.Counter.CommandCount);
    }

    [Fact]
    public async Task StoreConfigLiveTenant_UsesOneAuthorityJoinAndOneThemePresetRead()
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("theme-store");
        var otherTenant = ActiveTenant("other-store");
        fixture.Context.Tenants.AddRange(tenant, otherTenant);
        fixture.Context.StoreConfigs.AddRange(
            new StoreConfig
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id,
                StoreName = "Right store", StorefrontStatus = "live",
                ThemeLivePresetKey = "preferred"
            },
            new StoreConfig
            {
                Id = Guid.NewGuid(), TenantId = otherTenant.Id,
                StoreName = "Other store", StorefrontStatus = "live"
            });
        fixture.Context.ThemePresets.Add(
            new ThemePreset
            {
                Id = Guid.NewGuid(), Key = "preferred", Name = "Preferred",
                BaseThemeJson = "{}", CreatedAtUtc = DateTime.UtcNow
            });
        await fixture.Context.SaveChangesAsync();
        fixture.TenantContext.SetTenantId(tenant.Id);
        fixture.Counter.Reset();

        var controller = new StorefrontStoreConfigController(
            fixture.TenantContext, fixture.Context,
            Mock.Of<IStorefrontThemePreviewTokenService>(),
            NullLogger<StorefrontStoreConfigController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "live-config-count" }
            }
        };

        var response = await controller.GetStoreConfig(cancellationToken: CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(2, fixture.Counter.CommandCount);
    }

    [Fact]
    public async Task CanonicalSlugResolution_IssuesOneRelationalCommand()
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("canonical");
        fixture.Context.Tenants.Add(tenant);
        await fixture.Context.SaveChangesAsync();
        fixture.Counter.Reset();

        var tenantContext = new TenantContext();
        var nextCalled = false;
        var middleware = CreateTenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var httpContext = CreateHttpContext("canonical.example.com");

        await middleware.InvokeAsync(httpContext, tenantContext, fixture.Context);

        Assert.True(nextCalled);
        Assert.Equal(tenant.Id, tenantContext.TenantId);
        Assert.Equal(1, fixture.Counter.CommandCount);
    }

    [Fact]
    public async Task MatchingAuthenticatedDomain_ReusesHostAuthorityWithoutRelationalCommand()
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("authenticated");
        fixture.Context.Tenants.Add(tenant);
        await fixture.Context.SaveChangesAsync();
        fixture.Counter.Reset();

        var tenantContext = new TenantContext();
        var middleware = CreateTenantMiddleware(_ => Task.CompletedTask);
        var httpContext = CreateHttpContext("authenticated.example.com");
        httpContext.User = AuthenticatedUser(tenant.Id);
        httpContext.Items[HostResolutionContextKeys.DomainResolutionResultItemKey] =
            DomainResolutionResult.Found("authenticated.example.com", Guid.NewGuid(), tenant.Id);

        await middleware.InvokeAsync(httpContext, tenantContext, fixture.Context);

        Assert.Equal(tenant.Id, tenantContext.TenantId);
        Assert.Equal(0, fixture.Counter.CommandCount);
    }

    [Fact]
    public async Task MatchingAuthenticatedDomain_WithSlugHeaderRetainsRelationalSlugValidation()
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("authenticated-slug");
        fixture.Context.Tenants.Add(tenant);
        await fixture.Context.SaveChangesAsync();
        fixture.Counter.Reset();

        var tenantContext = new TenantContext();
        var nextCalled = false;
        var middleware = CreateTenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var httpContext = CreateHttpContext("authenticated-slug.example.com");
        httpContext.User = AuthenticatedUser(tenant.Id);
        httpContext.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName] = tenant.Slug;
        httpContext.Items[HostResolutionContextKeys.DomainResolutionResultItemKey] =
            DomainResolutionResult.Found("authenticated-slug.example.com", Guid.NewGuid(), tenant.Id);

        await middleware.InvokeAsync(httpContext, tenantContext, fixture.Context);

        Assert.True(nextCalled);
        Assert.Equal(tenant.Id, tenantContext.TenantId);
        Assert.Equal(tenant.Id.ToString(), httpContext.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName]);
        Assert.Equal(1, fixture.Counter.CommandCount);
    }

    [Fact]
    public async Task MismatchedAuthenticatedDomain_RemainsFailClosed()
    {
        await using var fixture = CreateFixture();
        var authenticatedTenant = ActiveTenant("authenticated");
        var hostTenant = ActiveTenant("host");
        fixture.Context.Tenants.AddRange(authenticatedTenant, hostTenant);
        await fixture.Context.SaveChangesAsync();
        fixture.Counter.Reset();

        var tenantContext = new TenantContext();
        var nextCalled = false;
        var middleware = CreateTenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var httpContext = CreateHttpContext("host.example.com");
        httpContext.User = AuthenticatedUser(authenticatedTenant.Id);
        httpContext.Items[HostResolutionContextKeys.DomainResolutionResultItemKey] =
            DomainResolutionResult.Found("host.example.com", Guid.NewGuid(), hostTenant.Id);

        await middleware.InvokeAsync(httpContext, tenantContext, fixture.Context);

        Assert.False(nextCalled);
        Assert.Null(tenantContext.TenantId);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.Equal(1, fixture.Counter.CommandCount);
    }

    [Theory]
    [InlineData("live", TenantStatus.Suspended, true, false, "STOREFRONT_CONFIG_NOT_FOUND")]
    [InlineData("live", TenantStatus.Active, false, false, "STOREFRONT_CONFIG_NOT_FOUND")]
    [InlineData("live", TenantStatus.Active, true, true, "STOREFRONT_CONFIG_NOT_FOUND")]
    public async Task StoreConfigAuthority_FailsClosedInOneRelationalCommand(
        string storefrontStatus,
        TenantStatus tenantStatus,
        bool isActive,
        bool isDeleted,
        string expectedCode)
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("store-config");
        tenant.Status = tenantStatus;
        tenant.IsActive = isActive;
        tenant.DeletedAtUtc = isDeleted ? DateTime.UtcNow : null;
        fixture.Context.Tenants.Add(tenant);
        fixture.Context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            StoreName = "Store",
            StorefrontStatus = storefrontStatus
        });
        await fixture.Context.SaveChangesAsync();
        fixture.TenantContext.SetTenantId(tenant.Id);
        fixture.Counter.Reset();

        var controller = new StorefrontStoreConfigController(
            fixture.TenantContext,
            fixture.Context,
            Mock.Of<IStorefrontThemePreviewTokenService>(),
            NullLogger<StorefrontStoreConfigController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "store-config-authority" }
            }
        };

        var result = await controller.GetStoreConfig(cancellationToken: CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(1, fixture.Counter.CommandCount);
    }

    [Theory]
    [InlineData("maintenance")]
    [InlineData("disabled")]
    public async Task StoreConfigAuthority_ReturnsActiveTenantStatusForFrontendBootstrap(string storefrontStatus)
    {
        await using var fixture = CreateFixture();
        var tenant = ActiveTenant("store-config-status");
        fixture.Context.Tenants.Add(tenant);
        fixture.Context.StoreConfigs.Add(new StoreConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            StoreName = "Status Store",
            StorefrontStatus = storefrontStatus
        });
        await fixture.Context.SaveChangesAsync();
        fixture.TenantContext.SetTenantId(tenant.Id);
        fixture.Counter.Reset();

        var controller = new StorefrontStoreConfigController(
            fixture.TenantContext,
            fixture.Context,
            Mock.Of<IStorefrontThemePreviewTokenService>(),
            NullLogger<StorefrontStoreConfigController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "store-config-status" }
            }
        };

        var result = await controller.GetStoreConfig(cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var config = Assert.IsType<StorefrontStoreConfigDto>(ok.Value);
        Assert.Equal(storefrontStatus, config.StorefrontStatus);
        Assert.Equal(2, fixture.Counter.CommandCount);
    }

    private static TestFixture CreateFixture()
    {
        var counter = new CountingInterceptor();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;
        var tenantContext = new TenantContext();
        var context = new SqliteTestDbContext(options, tenantContext);
        context.Database.EnsureCreated();
        return new TestFixture(context, connection, counter, tenantContext);
    }

    private static TenantResolutionMiddleware CreateTenantMiddleware(RequestDelegate next)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.com"
            })
            .Build();
        return new TenantResolutionMiddleware(
            next,
            NullLogger<TenantResolutionMiddleware>.Instance,
            configuration);
    }

    private static DefaultHttpContext CreateHttpContext(string host)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(host);
        context.Request.Path = "/api/storefront/products";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ClaimsPrincipal AuthenticatedUser(Guid tenantId) => new(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        ],
        "TestAuth"));

    private static Tenant ActiveTenant(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = slug,
        Slug = slug,
        StorefrontBaseUrl = $"https://{slug}.example.com",
        Currency = "EGP",
        Status = TenantStatus.Active,
        IsActive = true
    };

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }

        public void Reset() => CommandCount = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class SqliteTestDbContext(
        DbContextOptions<ApplicationDbContext> options,
        TenantContext tenantContext)
        : ApplicationDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties().Where(property =>
                             string.Equals(property.GetColumnName(), "xmin", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                    property.SetDefaultValue(0u);
                }
            }
        }
    }

    private sealed class TestFixture(
        ApplicationDbContext context,
        SqliteConnection connection,
        CountingInterceptor counter,
        TenantContext tenantContext) : IAsyncDisposable
    {
        public ApplicationDbContext Context { get; } = context;
        public CountingInterceptor Counter { get; } = counter;
        public TenantContext TenantContext { get; } = tenantContext;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
