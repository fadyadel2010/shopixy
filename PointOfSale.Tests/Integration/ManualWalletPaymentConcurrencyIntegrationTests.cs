using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using AccountingOutboxMessage = PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Configuration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class ManualWalletPaymentConcurrencyIntegrationTests
{
    [Fact]
    public async Task ConfirmPayment_WhenTwoAdminsRace_OnlyOneSucceeds()
    {
        await using var scope = await RealPostgresConcurrencyScope.TryCreateAsync();
        if (scope is null)
        {
            Assert.Fail("Failed to create real-Postgres test database scope. Ensure PostgreSQL is available and configured correctly with ConnectionStrings:DefaultConnection in test settings.");
        }

        var seeded = await SeedScenarioAsync(scope, scope.TenantContext, scope.TenantId);

        await using var firstContext = scope.CreateDbContext(scope.TenantContext);
        await using var secondContext = scope.CreateDbContext(scope.TenantContext);

        _ = await firstContext.PaymentIntents.SingleAsync(pi => pi.Id == seeded.PaymentIntentId);
        _ = await secondContext.PaymentIntents.SingleAsync(pi => pi.Id == seeded.PaymentIntentId);

        var commerceAccounting = new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostWalletApprovedAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "WALLET-JOURNAL-TEST"));

        var firstLifecycleService = CreateLifecycleService(firstContext, commerceAccounting);
        var secondLifecycleService = CreateLifecycleService(secondContext, commerceAccounting);

        var firstController = CreateController(
            firstContext,
            scope.TenantContext,
            firstLifecycleService,
            "admin-a");
        var secondController = CreateController(
            secondContext,
            scope.TenantContext,
            secondLifecycleService,
            "admin-b");

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = RunConfirmAsync(firstController, seeded.PaymentIntentId, startGate.Task);
        var secondTask = RunConfirmAsync(secondController, seeded.PaymentIntentId, startGate.Task);

        startGate.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(30));

        var okResults = results.OfType<OkObjectResult>().ToList();
        if (okResults.Count != 1)
        {
            Assert.Fail(string.Join(
                "; ",
                results.Select(result =>
                {
                    var objectResult = result as ObjectResult;
                    var apiError = objectResult?.Value as ApiErrorResponse;
                    return $"{result.GetType().Name}:{objectResult?.StatusCode}:{apiError?.Code}:{apiError?.Message}";
                })));
        }

        var ok = okResults[0];
        var conflict = Assert.Single(results.OfType<ConflictObjectResult>());
        var error = Assert.IsType<ApiErrorResponse>(conflict.Value);

        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode ?? StatusCodes.Status200OK);
        Assert.Equal("PAYMENT_CONCURRENCY_CONFLICT", error.Code);

        await using var verifyContext = scope.CreateDbContext(scope.TenantContext);
        var order = await verifyContext.Orders.SingleAsync(order => order.Id == seeded.OrderId);
        var paymentIntent = await verifyContext.PaymentIntents.SingleAsync(pi => pi.Id == seeded.PaymentIntentId);
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
        Assert.Equal(PaymentIntentStatus.Paid, paymentIntent.Status);
        Assert.NotNull(paymentIntent.VerifiedAtUtc);
        Assert.NotNull(paymentIntent.VerifiedByAdmin);

        // Verify commerce accounting side effect - should be called exactly once by the winning admin
        commerceAccounting.Verify(
            service => service.PostWalletApprovedAsync(
                It.Is<Order>(o => o.Id == seeded.OrderId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static async Task<SeededManualWalletPayment> SeedScenarioAsync(
        RealPostgresConcurrencyScope scope,
        TestTenantContext tenantContext,
        Guid tenantId)
    {
        var orderId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var paymentReference = $"PAY-{Guid.NewGuid():N}";
        var utcNow = DateTime.UtcNow;

        await using var context = scope.CreateDbContext(tenantContext);
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-{orderId:N}",
            PaymentMethod = PaymentMethod.BankTransfer,
            PaymentMethodCode = "MANUALWALLET",
            PaymentMethodName = "Manual Wallet",
            Status = OrderStatus.Pending,
            Total = 100m,
            Subtotal = 100m,
            Currency = "EGP",
            PaymentStatus = PaymentStatus.AwaitingVerification,
            CreatedAt = utcNow
        });

        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = paymentIntentId,
            OrderId = orderId,
            TenantId = tenantId,
            Amount = 100m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.BankTransfer,
            Status = PaymentIntentStatus.AwaitingVerification,
            Provider = PaymentProvider.ManualWallet,
            PaymentReference = paymentReference,
            WalletType = "VODAFONECASH",
            WalletPhoneNumber = "01000000000",
            ProofImageUrl = $"tenants/{tenantId:D}/manual-wallet-proofs/{paymentIntentId:N}.png",
            ProofImageExpiresAtUtc = utcNow.AddDays(30),
            ExpiresAtUtc = utcNow.AddMinutes(30),
            CreatedAt = utcNow
        });

        await context.SaveChangesAsync();
        return new SeededManualWalletPayment(orderId, paymentIntentId, paymentReference);
    }

    private static AdminManualPaymentController CreateController(
        ApplicationDbContext context,
        TestTenantContext tenantContext,
        IPaymentLifecycleService lifecycleService,
        string adminId)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, adminId),
                new Claim(ClaimTypes.Role, "Admin")
            },
            "TestAuth");

        return new AdminManualPaymentController(
            new LifecycleBackedManualPaymentReviewApplicationService(lifecycleService),
            Mock.Of<IManualPaymentCapabilityAdmissionAuthority>(),
            Mock.Of<IManualPaymentExpirationAuthority>(),
            Mock.Of<IManualPaymentRetentionAuthority>(),
            tenantContext,
            context,
            Mock.Of<IR2StorageService>(),
            Options.Create(new R2Options()),
            NullLogger<AdminManualPaymentController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static async Task<IActionResult> RunConfirmAsync(
        AdminManualPaymentController controller,
        Guid paymentIntentId,
        Task startSignal)
    {
        await startSignal;
        return await controller.ConfirmPayment(paymentIntentId, CancellationToken.None);
    }

    private static IPaymentLifecycleService CreateLifecycleService(
        ApplicationDbContext context,
        Mock<ICommerceAccountingService>? commerceAccounting = null)
    {
        var outbox = new Mock<IAccountingOutboxService>();
        outbox.SetReturnsDefault(Task.CompletedTask);
        outbox
            .Setup(service => service.EnqueueWalletApprovedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, CancellationToken>(async (tenantId, orderId, cancellationToken) =>
            {
                context.AccountingOutboxMessages.Add(AccountingOutboxMessage.Create(
                    tenantId,
                    "WalletApproved",
                    orderId,
                    typeof(Order).FullName!,
                    "{}",
                    $"WALLET_APPROVED:{tenantId}:{orderId}",
                    aggregateVersion: 1,
                    sourceType: "Order"));
                await context.SaveChangesAsync(cancellationToken);
            });

        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher.SetReturnsDefault(Task.CompletedTask);

        commerceAccounting ??= new Mock<ICommerceAccountingService>();
        commerceAccounting
            .Setup(service => service.PostWalletApprovedAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PostingResult.Succeeded(Guid.NewGuid(), "WALLET-JOURNAL-TEST"));

        var marketplaceOutbox = new Mock<IMarketplaceEventOutboxService>();
        marketplaceOutbox.SetReturnsDefault(Task.CompletedTask);

        var correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor.Setup(accessor => accessor.GetCorrelationId()).Returns("manual-wallet-concurrency");

        var metricsService = new Mock<IInventoryReservationMetricsService>();

        var stockRepository = new Mock<IProductVariantStockRepository>();
        stockRepository
            .Setup(repo => repo.CommitOrderReservationsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stockRepository
            .Setup(repo => repo.ReleaseOrderReservationsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new PaymentLifecycleService(
            context,
            outbox.Object,
            commerceAccounting.Object,
            eventPublisher.Object,
            marketplaceOutbox.Object,
            correlationIdAccessor.Object,
            stockRepository.Object,
            metricsService.Object,
            Mock.Of<ICouponValidationService>(),
            NullLogger<PaymentLifecycleService>.Instance,
            Options.Create(new PaymentLifecycleSettings()));
    }

    private readonly record struct SeededManualWalletPayment(
        Guid OrderId,
        Guid PaymentIntentId,
        string PaymentReference);

    private sealed class LifecycleBackedManualPaymentReviewApplicationService : IManualPaymentReviewApplicationService
    {
        private readonly IPaymentLifecycleService _lifecycleService;

        public LifecycleBackedManualPaymentReviewApplicationService(IPaymentLifecycleService lifecycleService)
        {
            _lifecycleService = lifecycleService;
        }

        public async Task<ManualPaymentReviewApplicationResult> ApproveAsync(
            ManualPaymentReviewApplicationRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await _lifecycleService.HandlePaymentResultAsync(new HandlePaymentResultRequest
            {
                PaymentIntentId = request.PaymentIntentId,
                Outcome = PaymentLifecycleOutcome.Succeeded,
                Source = PaymentLifecycleSource.PaymentReviewApproval,
                Actor = request.Actor,
                ReviewProofRetentionExpiresAtUtc = DateTime.UtcNow.AddDays(30)
            }, cancellationToken);

            return new ManualPaymentReviewApplicationResult(null!, null!, null!, result);
        }

        public async Task<ManualPaymentReviewApplicationResult> RejectAsync(
            ManualPaymentReviewApplicationRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await _lifecycleService.HandlePaymentResultAsync(new HandlePaymentResultRequest
            {
                PaymentIntentId = request.PaymentIntentId,
                Outcome = PaymentLifecycleOutcome.Failed,
                Source = PaymentLifecycleSource.PaymentReviewRejection,
                Actor = request.Actor,
                Reason = request.Reason
            }, cancellationToken);

            return new ManualPaymentReviewApplicationResult(null!, null!, null!, result);
        }
    }

    private sealed class RealPostgresConcurrencyScope : IAsyncDisposable
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private readonly string _adminConnectionString;
        private readonly string _databaseName;

        private RealPostgresConcurrencyScope(
            DbContextOptions<ApplicationDbContext> options,
            string adminConnectionString,
            string databaseName,
            Guid tenantId)
        {
            _options = options;
            _adminConnectionString = adminConnectionString;
            _databaseName = databaseName;
            TenantId = tenantId;
            TenantContext = new TestTenantContext(tenantId);
        }

        public Guid TenantId { get; }

        public TestTenantContext TenantContext { get; }

        public static async Task<RealPostgresConcurrencyScope?> TryCreateAsync()
        {
            string baseConnectionString;
            try
            {
                baseConnectionString = TestConfiguration.GetRequiredConnectionString();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionStrings:DefaultConnection", StringComparison.Ordinal))
            {
                return null;
            }

            var normalizedConnectionString =
                PostgresConnectionStringPolicy.Normalize(baseConnectionString, isDevelopment: true)
                ?? baseConnectionString;

            var baseConnection = new NpgsqlConnectionStringBuilder(normalizedConnectionString)
            {
                Pooling = false,
                IncludeErrorDetail = true,
                SslMode = SslMode.Disable
            };
            baseConnection["Timezone"] = "UTC";

            var databaseName = $"manual_wallet_concurrency_{Guid.NewGuid():N}";
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

            await using (var context = new ManualWalletConcurrencyDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
            }

            return new RealPostgresConcurrencyScope(
                options,
                adminConnectionString,
                databaseName,
                Guid.NewGuid());
        }

        public ApplicationDbContext CreateDbContext(TestTenantContext tenantContext)
        {
            return new ManualWalletConcurrencyDbContext(_options, tenantContext);
        }

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

    private sealed class ManualWalletConcurrencyDbContext : ApplicationDbContext
    {
        public ManualWalletConcurrencyDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public ManualWalletConcurrencyDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var keepTypes = new HashSet<Type>
            {
                typeof(Order),
                typeof(OrderItem),
                typeof(InventoryReservation),
                typeof(AccountingOutboxMessage),
                typeof(PaymentIntent)
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

            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("Orders");
                entity.HasKey(order => order.Id);
                entity.Property(order => order.OrderNumber).IsRequired();
                entity.Property(order => order.Currency).IsRequired();
                entity.HasMany(order => order.Items)
                    .WithOne(item => item.Order)
                    .HasForeignKey(item => item.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("OrderItems");
                entity.HasKey(item => item.Id);
                entity.Ignore(item => item.Product);
                entity.Ignore(item => item.Variant);
                entity.Ignore(item => item.Unit);
                entity.Ignore(item => item.Warehouse);
            });

            modelBuilder.Entity<InventoryReservation>(entity =>
            {
                entity.ToTable("InventoryReservations");
                entity.HasKey(reservation => reservation.Id);
            });

            modelBuilder.Entity<AccountingOutboxMessage>(entity =>
            {
                entity.ToTable("AccountingOutboxMessages");
                entity.HasKey(message => message.Id);
                entity.HasIndex(message => new { message.TenantId, message.IdempotencyKey }).IsUnique();
            });

            modelBuilder.Entity<PaymentIntent>(entity =>
            {
                entity.ToTable("PaymentIntents");
                entity.HasKey(paymentIntent => paymentIntent.Id);
                entity.Property(paymentIntent => paymentIntent.PaymentReference).IsRequired();
                entity.Property(paymentIntent => paymentIntent.Currency).IsRequired();
                entity.Property(paymentIntent => paymentIntent.RowVersion)
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .IsRowVersion();
                entity.HasOne(paymentIntent => paymentIntent.Order)
                    .WithMany()
                    .HasForeignKey(paymentIntent => paymentIntent.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
