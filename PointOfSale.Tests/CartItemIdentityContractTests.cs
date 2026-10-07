using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Events;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.Promotions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.Tests;

public sealed class CartItemIdentityContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalMedia_AddReadAndRedisReadback_RepairsStaleOrMissingImage(bool hasVariants)
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants);
        var media = await fixture.SeedMediaAsync();
        var cart = await fixture.Service.AddItemAsync(fixture.TenantId, fixture.Owner,
            new AddCartItemRequest { ProductId = fixture.ProductId, VariantId = fixture.VariantId },
            false, "media-add");
        Assert.Equal(hasVariants ? media[1].Url : media[0].Url, Assert.Single(cart.Items).ThumbnailImage);

        // Redis and Mongo both lack current media; SQL must repair the line.
        fixture.RewritePersistedCart(persisted => persisted.Items[0].ThumbnailImage = null);
        var read = await fixture.Service.GetCartAsync(fixture.TenantId, fixture.Owner, "media-read");
        Assert.Equal(hasVariants ? media[1].Url : media[0].Url, Assert.Single(read.Items).ThumbnailImage);
        Assert.Equal(read.Items[0].ThumbnailImage, Assert.Single(fixture.ReadPersistedCart()!.Items).ThumbnailImage);

        await fixture.ChangeMediaAsync(media, removeSelected: false);
        read = await fixture.Service.GetCartAsync(fixture.TenantId, fixture.Owner, "media-change");
        Assert.Equal(media[2].Url, Assert.Single(read.Items).ThumbnailImage);
        Assert.Equal(media[2].Url, Assert.Single(fixture.ReadPersistedCart()!.Items).ThumbnailImage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalMedia_DeletedSelection_FallsBackAndDoesNotRetainDeadUrl(bool hasVariants)
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants);
        var media = await fixture.SeedMediaAsync();
        await fixture.Service.AddItemAsync(fixture.TenantId, fixture.Owner,
            new AddCartItemRequest { ProductId = fixture.ProductId, VariantId = fixture.VariantId },
            false, "media-delete-add");
        await fixture.ChangeMediaAsync(media, removeSelected: true);
        var read = await fixture.Service.GetCartAsync(fixture.TenantId, fixture.Owner, "media-delete-read");
        Assert.Equal(media[2].Url, Assert.Single(read.Items).ThumbnailImage);
        Assert.Equal(media[2].Url, Assert.Single(fixture.ReadPersistedCart()!.Items).ThumbnailImage);
    }

    [Fact]
    public async Task SimpleProduct_AddGetUpdateGetDelete_UsesCanonicalVariantIdentity()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: false);

        var added = await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "simple-add");

        var addedItem = Assert.Single(added.Items);
        Assert.Equal(fixture.ProductId, addedItem.ProductId);
        Assert.Equal(fixture.VariantId, addedItem.VariantId);

        var read = await fixture.Service.GetCartAsync(fixture.TenantId, fixture.Owner, "simple-get");
        Assert.Equal(added.CartId, read.CartId);
        Assert.Equal(fixture.VariantId, Assert.Single(read.Items).VariantId);

        var updated = await fixture.Service.UpdateItemByVariantAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.VariantId,
            new UpdateCartItemRequest { Quantity = 2 },
            "simple-update");
        Assert.Equal(2, Assert.Single(updated.Items).Quantity);

        var readAfterUpdate = await fixture.Service.GetCartAsync(
            fixture.TenantId,
            fixture.Owner,
            "simple-get-after-update");
        var updatedItem = Assert.Single(readAfterUpdate.Items);
        Assert.Equal(fixture.VariantId, updatedItem.VariantId);
        Assert.Equal(2, updatedItem.Quantity);

        var removed = await fixture.Service.RemoveItemByVariantAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.VariantId,
            "simple-delete");
        Assert.Empty(removed.Items);

        var readAfterDelete = await fixture.Service.GetCartAsync(
            fixture.TenantId,
            fixture.Owner,
            "simple-get-after-delete");
        Assert.Equal(added.CartId, readAfterDelete.CartId);
        Assert.Empty(readAfterDelete.Items);
    }

    [Fact]
    public async Task VariantProduct_AddUpdateDelete_PreservesSelectedVariantIdentity()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: true);

        var added = await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                VariantId = fixture.VariantId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "variant-add");
        Assert.Equal(fixture.VariantId, Assert.Single(added.Items).VariantId);

        var updated = await fixture.Service.UpdateItemByVariantAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.VariantId,
            new UpdateCartItemRequest { Quantity = 3 },
            "variant-update");
        Assert.Equal(3, Assert.Single(updated.Items).Quantity);

        var removed = await fixture.Service.RemoveItemByVariantAsync(
            fixture.TenantId,
            fixture.Owner,
            fixture.VariantId,
            "variant-delete");
        Assert.Empty(removed.Items);
    }

    [Fact]
    public async Task SimpleProduct_MismatchedOrCrossTenantVariant_IsRejectedWithoutAssociation()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: false);
        var otherTenantVariantId = await fixture.SeedOtherTenantVariantAsync();

        await Assert.ThrowsAsync<VariantNotFoundException>(() => fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                VariantId = Guid.NewGuid(),
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "mismatched-variant"));

        await Assert.ThrowsAsync<VariantNotFoundException>(() => fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                VariantId = otherTenantVariantId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "cross-tenant-variant"));

        Assert.Null(fixture.ReadPersistedCart());
    }

    [Theory]
    [InlineData(false, false, 5, 25, typeof(ProductOutOfStockException))]
    [InlineData(true, true, 5, 25, typeof(ProductOutOfStockException))]
    [InlineData(true, false, 5, 0, typeof(ProductNotForSaleException))]
    public async Task SimpleProduct_InactiveDeletedOrUnpurchasableVariant_IsRejected(
        bool variantActive,
        bool variantDeleted,
        int stock,
        decimal price,
        Type expectedExceptionType)
    {
        await using var fixture = await CartFixture.CreateAsync(
            hasVariants: false,
            stock: stock,
            variantActive: variantActive,
            variantDeleted: variantDeleted,
            simplePrice: price);

        var exception = await Record.ExceptionAsync(() => fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                VariantId = fixture.VariantId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "unavailable-simple-variant"));

        Assert.NotNull(exception);
        Assert.Equal(expectedExceptionType, exception.GetType());
        Assert.Null(fixture.ReadPersistedCart());
    }

    [Fact]
    public async Task SimpleProduct_StockFailure_IsRejected()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: false, stock: 2);

        var exception = await Assert.ThrowsAsync<VariantInsufficientStockException>(() =>
            fixture.Service.AddItemAsync(
                fixture.TenantId,
                fixture.Owner,
                new AddCartItemRequest
                {
                    ProductId = fixture.ProductId,
                    VariantId = fixture.VariantId,
                    Quantity = 3
                },
                affiliateModeOnly: false,
                correlationId: "simple-stock-failure"));

        Assert.Equal(fixture.VariantId, exception.VariantId);
        Assert.Null(fixture.ReadPersistedCart());
    }

    [Fact]
    public async Task SimpleProduct_AddAgain_MergesByCanonicalVariant()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: false);
        var request = new AddCartItemRequest
        {
            ProductId = fixture.ProductId,
            VariantId = fixture.VariantId,
            Quantity = 1
        };

        await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            request,
            affiliateModeOnly: false,
            correlationId: "simple-first-add");
        var result = await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            request,
            affiliateModeOnly: false,
            correlationId: "simple-second-add");

        var item = Assert.Single(result.Items);
        Assert.Equal(fixture.VariantId, item.VariantId);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public async Task SimpleProduct_AddToLegacyCart_UpgradesIdentityWithoutDuplicatingLine()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: false);
        await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "legacy-direct-add-seed");
        fixture.RewritePersistedCart(cart => Assert.Single(cart.Items).VariantId = null);

        var result = await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "legacy-direct-add");

        var item = Assert.Single(result.Items);
        Assert.Equal(fixture.VariantId, item.VariantId);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public async Task Revalidation_UpgradesLegacySimpleItemAndKeepsCartIdentityStable()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: false);
        var added = await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                VariantId = fixture.VariantId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "legacy-seed");

        fixture.RewritePersistedCart(cart => Assert.Single(cart.Items).VariantId = null);

        var revalidated = await fixture.Service.GetCartAsync(
            fixture.TenantId,
            fixture.Owner,
            "legacy-revalidation");

        Assert.Equal(added.CartId, revalidated.CartId);
        Assert.Equal(fixture.VariantId, Assert.Single(revalidated.Items).VariantId);
        Assert.Equal(fixture.VariantId, Assert.Single(fixture.ReadPersistedCart()!.Items).VariantId);
    }

    [Fact]
    public async Task Revalidation_UsesOneBatchProductReadAndNoPerItemRead()
    {
        await using var fixture = await CartFixture.CreateAsync(hasVariants: false);
        await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "batch-revalidation-seed");
        fixture.ResetProductRepositoryCalls();

        await fixture.Service.GetCartAsync(
            fixture.TenantId,
            fixture.Owner,
            "batch-revalidation");

        fixture.VerifySingleBatchProductRead();
    }

    [Fact]
    public async Task AddItem_ReleasesMutationLockBeforePublishingSingleEventBatch()
    {
        var sequence = new List<string>();
        var lockDurations = new List<double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == CommerceReliabilityTelemetry.MeterName &&
                    instrument.Name == "cart_mutation_lock_duration_seconds")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<double>((_, seconds, _, _) => lockDurations.Add(seconds));
        listener.Start();
        var publisher = new CartFixture.RecordingBatchEventPublisher(() => sequence.Add("batch-publish"));
        await using var fixture = await CartFixture.CreateAsync(
            hasVariants: false,
            eventPublisher: publisher,
            onLockReleased: () => sequence.Add("lock-release"));

        await fixture.Service.AddItemAsync(
            fixture.TenantId,
            fixture.Owner,
            new AddCartItemRequest
            {
                ProductId = fixture.ProductId,
                Quantity = 1
            },
            affiliateModeOnly: false,
            correlationId: "event-batch-order");

        Assert.Equal(["lock-release", "batch-publish"], sequence);
        Assert.True(Assert.Single(lockDurations) >= 0);
        Assert.Equal(1, publisher.BatchPublishCount);
        Assert.Equal(0, publisher.SinglePublishCount);
        Assert.Collection(
            publisher.Events,
            @event => Assert.IsType<CartCreatedEvent>(@event),
            @event => Assert.IsType<CartItemAddedEvent>(@event));
    }

    [Fact]
    public async Task AddItem_LostRenewal_StillAttemptsTokenOwnedReleaseBeforeConflict()
    {
        var releaseCount = 0;
        await using var fixture = await CartFixture.CreateAsync(
            hasVariants: false,
            onLockReleased: () => Interlocked.Increment(ref releaseCount),
            loseLockOnRenewal: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<CartConcurrencyConflictException>(() =>
            fixture.Service.AddItemAsync(
                fixture.TenantId,
                fixture.Owner,
                new AddCartItemRequest { ProductId = fixture.ProductId, Quantity = 1 },
                affiliateModeOnly: false,
                correlationId: "lost-lock-release",
                cancellationToken: timeout.Token));

        Assert.Equal(1, releaseCount);
        Assert.Null(fixture.ReadPersistedCart());
    }

    private sealed class CartFixture : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ConcurrentDictionary<string, string> _redis;
        private readonly ApplicationDbContext _dbContext;
        private readonly Mock<IProductReadRepository> _productRepository;

        private CartFixture(
            ApplicationDbContext dbContext,
            Mock<IProductReadRepository> productRepository,
            CartService service,
            ConcurrentDictionary<string, string> redis,
            Guid tenantId,
            Guid productId,
            Guid variantId)
        {
            _dbContext = dbContext;
            _productRepository = productRepository;
            _redis = redis;
            Service = service;
            TenantId = tenantId;
            ProductId = productId;
            VariantId = variantId;
        }

        public CartService Service { get; }
        public Guid TenantId { get; }
        public Guid ProductId { get; }
        public Guid VariantId { get; }
        public CartOwner Owner { get; } = new() { OwnerType = "session", OwnerId = "cart-contract-session" };

        public async Task<List<ProductMedia>> SeedMediaAsync()
        {
            var media = Enumerable.Range(0, 3).Select(index => new ProductMedia
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ProductId = ProductId,
                MediaType = "image", Url = $"/api/upload/product-media/{ProductId}/{Guid.NewGuid():N}.png",
                Position = index + 1, CreatedAtUtc = DateTime.UtcNow
            }).ToList();
            _dbContext.ProductMedia.AddRange(media);
            var variant = await _dbContext.ProductVariants.IgnoreQueryFilters().SingleAsync(v => v.Id == VariantId);
            variant.MediaId = media[1].Id;
            variant.ImageUrl = "/legacy-must-not-win.png";
            await _dbContext.SaveChangesAsync();
            return media;
        }

        public async Task ChangeMediaAsync(List<ProductMedia> media, bool removeSelected)
        {
            media[2].Position = 0;
            var variant = await _dbContext.ProductVariants.IgnoreQueryFilters().SingleAsync(v => v.Id == VariantId);
            variant.MediaId = removeSelected ? null : media[2].Id;
            if (removeSelected)
            {
                _dbContext.ProductMedia.Remove(media[0]);
                _dbContext.ProductMedia.Remove(media[1]);
            }
            await _dbContext.SaveChangesAsync();
        }

        public static async Task<CartFixture> CreateAsync(
            bool hasVariants,
            int stock = 10,
            bool variantActive = true,
            bool variantDeleted = false,
            decimal simplePrice = 25m,
            IEventPublisher? eventPublisher = null,
            Action? onLockReleased = null,
            bool loseLockOnRenewal = false)
        {
            var tenantId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var variantId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var unitId = Guid.NewGuid();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"cart-identity-{Guid.NewGuid():N}")
                .Options;
            var dbContext = new ApplicationDbContext(options);

            dbContext.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Cart identity tenant",
                Currency = "EGP",
                Status = TenantStatus.Active,
                IsActive = true
            });
            dbContext.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Cart identity product",
                SKU = "CART-IDENTITY",
                CategoryId = Guid.Empty,
                HasVariants = hasVariants,
                IsActive = true,
                IsOnlineVisible = true,
                IsSearchable = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = true,
                CreatedAt = DateTime.UtcNow
            });
            dbContext.Units.Add(new PointOfSale.Core.Entities.Unit
            {
                Id = unitId,
                TenantId = tenantId,
                Name = "Piece",
                Symbol = "pc",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            dbContext.ProductUnits.Add(new ProductUnit
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                UnitId = unitId,
                ConversionFactor = 1m,
                SellingPrice = simplePrice,
                PurchasePrice = 10m,
                IsDefault = true,
                IsBaseUnit = true,
                CreatedAt = DateTime.UtcNow
            });
            dbContext.ProductVariants.Add(new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                SKU = "CART-IDENTITY",
                Price = simplePrice,
                OnlinePrice = simplePrice,
                CostPrice = 10m,
                IsDefault = true,
                IsActive = variantActive,
                IsDeleted = variantDeleted,
                SemanticLifecycleState = ProductVariantSemanticLifecycleState.Current,
                CreatedAt = DateTime.UtcNow
            });
            dbContext.Warehouses.Add(new Warehouse
            {
                Id = warehouseId,
                TenantId = tenantId,
                Name = "Cart identity warehouse",
                IsActive = true,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            });
            if (!variantDeleted)
            {
                dbContext.ProductVariantStocks.Add(new ProductVariantStock
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    VariantId = variantId,
                    WarehouseId = warehouseId,
                    Quantity = stock,
                    ReservedQuantity = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await dbContext.SaveChangesAsync();

            var projectedProduct = new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId
            };
            var productRepository = new Mock<IProductReadRepository>();
            productRepository
                .Setup(repository => repository.FindByProductIdAsync(
                    tenantId,
                    productId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(projectedProduct);
            productRepository
                .Setup(repository => repository.GetByProductIdsAsync(
                    tenantId,
                    It.IsAny<List<Guid>>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, List<Guid> productIds, bool _, CancellationToken _) =>
                    productIds.Contains(productId)
                        ? new List<ProductDocument>
                        {
                            new() { TenantId = tenantId, ProductId = productId }
                        }
                        : []);

            var database = new Mock<IDatabase>();
            var redisState = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            database
                .Setup(redis => redis.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .Returns(async (RedisKey key, CommandFlags _) =>
                {
                    // Hold the cart read *inside* the lock for the renewal
                    // failure contract. Mongo candidate fetching now happens
                    // before lock acquisition and must not trigger renewal.
                    if (loseLockOnRenewal)
                        await Task.Delay(100);
                    return redisState.TryGetValue(key.ToString(), out var value)
                        ? (RedisValue)value
                        : RedisValue.Null;
                });
            database
                .Setup(redis => redis.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<bool>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? _, bool _, When _, CommandFlags _) =>
                {
                    redisState[key.ToString()] = value.ToString();
                    return true;
                });
            database
                .Setup(redis => redis.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags flags) => redisState.TryRemove(key.ToString(), out var removed));

            var multiplexer = new Mock<IConnectionMultiplexer>();
            multiplexer
                .Setup(redis => redis.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(database.Object);
            var redisSettings = Options.Create(new RedisSettings
            {
                Enabled = true,
                InstanceName = "cart-contract"
            });

            var lockService = new Mock<IDistributedLockService>();
            lockService
                .Setup(service => service.TryAcquireLockAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>()))
                .ReturnsAsync(DistributedLockResult.Acquired());
            lockService
                .Setup(service => service.RefreshLockAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>()))
                .ReturnsAsync(loseLockOnRenewal
                    ? DistributedLockResult.AlreadyHeld()
                    : DistributedLockResult.Acquired());
            lockService
                .Setup(service => service.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string _) =>
                {
                    onLockReleased?.Invoke();
                    return Task.FromResult(true);
                });

            var mongoCollection = new Mock<IMongoCollection<StorefrontConfigDocument>>();
            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(database => database.GetCollection<StorefrontConfigDocument>(
                    "storefront_configs",
                    It.IsAny<MongoCollectionSettings>()))
                .Returns(mongoCollection.Object);

            var promotionEngine = new Mock<IPromotionEngine>();
            promotionEngine
                .Setup(engine => engine.GetActiveCartPromotionAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<decimal>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((CartPromotionResult?)null);
            var taxService = new Mock<ITaxCalculationService>();
            taxService
                .Setup(service => service.CalculateAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<decimal>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TaxComputationResult(0m, 0m, "test", null));

            var service = new CartService(
                dbContext,
                new RedisConnectionFactory(
                    redisSettings,
                    NullLogger<RedisConnectionFactory>.Instance,
                    multiplexer.Object),
                redisSettings,
                productRepository.Object,
                eventPublisher ?? Mock.Of<IEventPublisher>(),
                mongoDatabase.Object,
                promotionEngine.Object,
                taxService.Object,
                new CommerceReliabilityTelemetry(),
                lockService.Object,
                NullLogger<CartService>.Instance,
                lockRenewalInterval: loseLockOnRenewal ? TimeSpan.FromMilliseconds(10) : null);

            var fixture = new CartFixture(
                dbContext,
                productRepository,
                service,
                redisState,
                tenantId,
                productId,
                variantId);
            return fixture;
        }

        public sealed class RecordingBatchEventPublisher(Action onBatchPublished) : IEventPublisher
        {
            public List<object> Events { get; } = [];
            public int BatchPublishCount { get; private set; }
            public int SinglePublishCount { get; private set; }

            public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
                where T : IntegrationEvent
            {
                SinglePublishCount++;
                Events.Add(@event);
                return Task.CompletedTask;
            }

            public Task PublishAsync(object @event, CancellationToken cancellationToken = default)
            {
                SinglePublishCount++;
                Events.Add(@event);
                return Task.CompletedTask;
            }

            public Task PublishBatchAsync<T>(
                IEnumerable<T> events,
                CancellationToken cancellationToken = default)
                where T : IntegrationEvent
            {
                BatchPublishCount++;
                Events.AddRange(events.Cast<object>());
                onBatchPublished();
                return Task.CompletedTask;
            }

            public Task PublishBatchAsync(
                IEnumerable<object> events,
                CancellationToken cancellationToken = default)
            {
                BatchPublishCount++;
                Events.AddRange(events);
                onBatchPublished();
                return Task.CompletedTask;
            }

            public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
                where T : IntegrationEvent, ISensitiveIntegrationEvent
            {
                SinglePublishCount++;
                Events.Add(@event);
                return Task.CompletedTask;
            }
        }

        public CartDto? ReadPersistedCart()
        {
            var value = _redis.Values.SingleOrDefault();
            return value == null ? null : JsonSerializer.Deserialize<CartDto>(value, JsonOptions);
        }

        public void RewritePersistedCart(Action<CartDto> rewrite)
        {
            var entry = Assert.Single(_redis);
            var cart = JsonSerializer.Deserialize<CartDto>(entry.Value, JsonOptions)!;
            rewrite(cart);
            _redis[entry.Key] = JsonSerializer.Serialize(cart, JsonOptions);
        }

        public void ResetProductRepositoryCalls() => _productRepository.Invocations.Clear();

        public void VerifySingleBatchProductRead()
        {
            _productRepository.Verify(repository => repository.GetByProductIdsAsync(
                TenantId,
                It.Is<List<Guid>>(ids => ids.Count == 1 && ids[0] == ProductId),
                false,
                It.IsAny<CancellationToken>()), Times.Once);
            _productRepository.Verify(repository => repository.FindByProductIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        public async Task<Guid> SeedOtherTenantVariantAsync()
        {
            var tenantId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var variantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Other tenant",
                Currency = "EGP",
                Status = TenantStatus.Active,
                IsActive = true
            });
            _dbContext.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Other product",
                CategoryId = Guid.Empty,
                HasVariants = false,
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                CreatedAt = DateTime.UtcNow
            });
            _dbContext.ProductVariants.Add(new ProductVariant
            {
                Id = variantId,
                TenantId = tenantId,
                ProductId = productId,
                SKU = $"OTHER-{variantId:N}",
                Price = 25m,
                IsDefault = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
            return variantId;
        }

        public async ValueTask DisposeAsync()
        {
            await _dbContext.DisposeAsync();
            GC.KeepAlive(_productRepository);
        }
    }
}
