using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class CheckoutSessionConsistencyIntegrationTests
{
    [Fact]
    public async Task CreateOrResumeSessionAsync_WhenParallelRequestsRace_OnlyOneActiveSessionIsCreated()
    {
        await using var scope = await SqliteCheckoutSessionScope.CreateAsync();
        var cartId = Guid.NewGuid();

        await using var firstContext = await scope.CreateDbContextAsync();
        await using var secondContext = await scope.CreateDbContextAsync();

        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = RunCreateOrResumeAsync(firstService, scope.TenantId, cartId, "guest-session-1", startGate.Task);
        var secondTask = RunCreateOrResumeAsync(secondService, scope.TenantId, cartId, "guest-session-1", startGate.Task);

        startGate.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(results[0].Id, results[1].Id);
        Assert.All(results, session => Assert.Equal(CheckoutSessionStatus.Active, session.Status));

        await using var verifyContext = await scope.CreateDbContextAsync();
        var persistedSessions = await verifyContext.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(session => session.TenantId == scope.TenantId && session.CartId == cartId)
            .OrderByDescending(session => session.LastActivityAtUtc)
            .ToListAsync();

        var canonical = Assert.Single(persistedSessions);
        Assert.Equal(CheckoutSessionStatus.Active, canonical.Status);
        Assert.Equal("guest-session-1", canonical.SessionId);
    }

    [Fact]
    public async Task RecoverByToken_WhenLegacyDuplicateSessionsExist_ReturnsNewestCanonicalSession()
    {
        await using var scope = await SqliteCheckoutSessionScope.CreateAsync();
        var seeded = await SeedLegacyDuplicateSessionsAsync(scope);

        var tokenService = CreateCheckoutSessionTokenService();
        var recoveryToken = tokenService.CreateRecoveryToken(seeded.OlderSession);

        await using var context = await scope.CreateDbContextAsync();
        var controller = CreateController(
            CreateService(context),
            scope.TenantId,
            BuildAuthoritativeCart(scope.TenantId, seeded.NewerSession.CartId, "guest-session-legacy"),
            tokenService);

        var result = await controller.RecoverByToken(recoveryToken, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<CheckoutRecoveryResponse>(ok.Value);
        Assert.Equal(seeded.NewerSession.Id, payload.SessionId);
        Assert.Equal(seeded.NewerSession.CartId, payload.CartId);
        Assert.Equal("guest-session-legacy", payload.GuestSessionId);
        var recoveredItem = Assert.Single(payload.Cart);
        Assert.Equal("Canonical Product", recoveredItem.ProductName);

        await using var verifyContext = await scope.CreateDbContextAsync();
        var sessions = await verifyContext.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(session => session.TenantId == scope.TenantId && session.CartId == seeded.NewerSession.CartId)
            .ToListAsync();

        var activeSession = Assert.Single(sessions, session => session.Status == CheckoutSessionStatus.Active);
        var olderSession = Assert.Single(sessions, session => session.Id == seeded.OlderSession.Id);
        var newerSession = Assert.Single(sessions, session => session.Id == seeded.NewerSession.Id);

        Assert.Equal(CheckoutSessionStatus.Abandoned, olderSession.Status);
        Assert.NotEqual(seeded.OlderSession.RecoveryToken, olderSession.RecoveryToken);
        Assert.Equal(seeded.NewerSession.Id, activeSession.Id);
        Assert.Equal(CheckoutSessionStatus.Active, newerSession.Status);
        Assert.Equal(seeded.NewerSession.RecoveryToken, newerSession.RecoveryToken);
    }

    [Fact]
    public async Task Postgres_CreateOrResumeSessionAsync_WhenParallelRequestsRace_AdvisoryLockPreventsRaceCondition()
    {
        // Skip if PostgreSQL is not available
        if (!RealPostgresTestSupport.TryCreateScope(out var testScope, out _, out _))
        {
            return;
        }
        await testScope!.DisposeAsync();

        await using var scope = await PostgresCheckoutSessionScope.CreateAsync();
        var cartId = Guid.NewGuid();

        await using var firstContext = await scope.CreateDbContextAsync();
        await using var secondContext = await scope.CreateDbContextAsync();

        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = RunCreateOrResumeAsync(firstService, scope.TenantId, cartId, "guest-session-pg-1", startGate.Task);
        var secondTask = RunCreateOrResumeAsync(secondService, scope.TenantId, cartId, "guest-session-pg-1", startGate.Task);

        startGate.SetResult();
        var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(30));

        // With PostgreSQL advisory locks, both requests should get the same session
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.All(results, session => Assert.Equal(CheckoutSessionStatus.Active, session.Status));

        await using var verifyContext = await scope.CreateDbContextAsync();
        var persistedSessions = await verifyContext.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(session => session.TenantId == scope.TenantId && session.CartId == cartId)
            .OrderByDescending(session => session.LastActivityAtUtc)
            .ToListAsync();

        // Advisory lock should ensure only one session was created
        var canonical = Assert.Single(persistedSessions);
        Assert.Equal(CheckoutSessionStatus.Active, canonical.Status);
        Assert.Equal("guest-session-pg-1", canonical.SessionId);
    }

    [Fact]
    public async Task Postgres_RecoverByToken_WhenLegacyDuplicateSessionsExist_AdvisoryLockEnsuresConsistentReconciliation()
    {
        // Skip if PostgreSQL is not available
        if (!RealPostgresTestSupport.TryCreateScope(out var testScope, out _, out _))
        {
            return;
        }
        await testScope!.DisposeAsync();

        await using var scope = await PostgresCheckoutSessionScope.CreateAsync();
        var seeded = await SeedLegacyDuplicateSessionsAsync(scope);

        var tokenService = CreateCheckoutSessionTokenService();
        var recoveryToken = tokenService.CreateRecoveryToken(seeded.OlderSession);

        await using var context = await scope.CreateDbContextAsync();
        var controller = CreateController(
            CreateService(context),
            scope.TenantId,
            BuildAuthoritativeCart(scope.TenantId, seeded.NewerSession.CartId, "guest-session-legacy"),
            tokenService);

        var result = await controller.RecoverByToken(recoveryToken, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<CheckoutRecoveryResponse>(ok.Value);
        Assert.Equal(seeded.NewerSession.Id, payload.SessionId);
        Assert.Equal(seeded.NewerSession.CartId, payload.CartId);
        Assert.Equal("guest-session-legacy", payload.GuestSessionId);
        var recoveredItem = Assert.Single(payload.Cart);
        Assert.Equal("Canonical Product", recoveredItem.ProductName);

        await using var verifyContext = await scope.CreateDbContextAsync();
        var sessions = await verifyContext.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(session => session.TenantId == scope.TenantId && session.CartId == seeded.NewerSession.CartId)
            .ToListAsync();

        var activeSession = Assert.Single(sessions, session => session.Status == CheckoutSessionStatus.Active);
        var olderSession = Assert.Single(sessions, session => session.Id == seeded.OlderSession.Id);
        var newerSession = Assert.Single(sessions, session => session.Id == seeded.NewerSession.Id);

        Assert.Equal(CheckoutSessionStatus.Abandoned, olderSession.Status);
        Assert.NotEqual(seeded.OlderSession.RecoveryToken, olderSession.RecoveryToken);
        Assert.Equal(seeded.NewerSession.Id, activeSession.Id);
        Assert.Equal(CheckoutSessionStatus.Active, newerSession.Status);
        Assert.Equal(seeded.NewerSession.RecoveryToken, newerSession.RecoveryToken);
    }

    [Fact]
    public async Task RecoverByToken_WhenLegacyTokenWasReconciledAway_RejectsStaleTokenOnSubsequentRequests()
    {
        await using var scope = await SqliteCheckoutSessionScope.CreateAsync();
        var seeded = await SeedLegacyDuplicateSessionsAsync(scope);

        var tokenService = CreateCheckoutSessionTokenService();
        var staleRecoveryToken = tokenService.CreateRecoveryToken(seeded.OlderSession);

        await using (var firstContext = await scope.CreateDbContextAsync())
        {
            var firstController = CreateController(
                CreateService(firstContext),
                scope.TenantId,
                BuildAuthoritativeCart(scope.TenantId, seeded.NewerSession.CartId, "guest-session-legacy"),
                tokenService);
            var firstResult = await firstController.RecoverByToken(staleRecoveryToken, CancellationToken.None);
            Assert.IsType<OkObjectResult>(firstResult);
        }

        await using var secondContext = await scope.CreateDbContextAsync();
        var secondController = CreateController(
            CreateService(secondContext),
            scope.TenantId,
            BuildAuthoritativeCart(scope.TenantId, seeded.NewerSession.CartId, "guest-session-legacy"),
            tokenService);
        var secondResult = await secondController.RecoverByToken(staleRecoveryToken, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(secondResult);
        var error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("SESSION_NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task CreateOrResume_WhenSessionPersistsButEventDispatchFails_ReturnsPartialSuccessInsteadOf503()
    {
        await using var scope = await SqliteCheckoutSessionScope.CreateAsync();
        var cartId = Guid.NewGuid();

        await using var context = await scope.CreateDbContextAsync();
        var service = CreateService(context, new ThrowingCheckoutSessionStartPublisher());
        var controller = CreateController(
            service,
            scope.TenantId,
            BuildAuthoritativeCart(scope.TenantId, cartId, "guest-session-partial"));

        controller.Request.Headers["X-Session-Id"] = "guest-session-partial";

        var result = await controller.CreateOrResume(
            new CreateCheckoutSessionRequest
            {
                CartId = cartId,
                DeviceType = "web",
                Source = "storefront"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<CheckoutSessionResponse>(ok.Value);
        Assert.Equal("partial_success", payload.Status);
        Assert.Equal("async_reconciliation", payload.Recovery);
        Assert.NotEqual(Guid.Empty, payload.SessionId);

        await using var verifyContext = await scope.CreateDbContextAsync();
        var persisted = await verifyContext.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(session => session.TenantId == scope.TenantId && session.CartId == cartId)
            .ToListAsync();

        var session = Assert.Single(persisted);
        Assert.Equal(CheckoutSessionStatus.Active, session.Status);
        Assert.Equal("guest-session-partial", session.SessionId);
    }

    [Fact]
    public async Task UpdateSessionWithOutcomeAsync_WhenAutosaveIsRetried_DoesNotCreateDuplicateSessions()
    {
        await using var scope = await SqliteCheckoutSessionScope.CreateAsync();
        var cartId = Guid.NewGuid();

        await using var context = await scope.CreateDbContextAsync();
        var service = CreateService(context);

        var session = await service.CreateOrResumeSessionAsync(
            scope.TenantId,
            cartId,
            "guest-session-autosave",
            onlineCustomerId: null,
            itemsSnapshotJson: """[{"productName":"Autosave Product","quantity":1,"price":75.0}]""",
            cartTotal: 75m,
            currency: "EGP",
            deviceType: "web",
            source: "storefront",
            ct: CancellationToken.None);

        var request = new PointOfSale.Core.DTOs.UpdateCheckoutSessionRequest
        {
            SessionId = session.Id,
            TenantId = scope.TenantId,
            CustomerName = "Ada Lovelace",
            Phone = "01011112222",
            City = "Cairo",
            Area = "Nasr City",
            Address = "42 Test Street",
            Currency = "EGP",
            CartTotal = 75m,
            ItemsSnapshotJson = """[{"productName":"Autosave Product","quantity":1,"price":75.0}]"""
        };

        var firstAutosave = await service.UpdateSessionWithOutcomeAsync(request, CancellationToken.None);
        var secondAutosave = await service.UpdateSessionWithOutcomeAsync(request, CancellationToken.None);

        Assert.NotNull(firstAutosave.Session);
        Assert.NotNull(secondAutosave.Session);
        Assert.Equal(session.Id, firstAutosave.Session!.Id);
        Assert.Equal(session.Id, secondAutosave.Session!.Id);

        await using var verifyContext = await scope.CreateDbContextAsync();
        var persisted = await verifyContext.CheckoutSessions
            .IgnoreQueryFilters()
            .Where(existing => existing.TenantId == scope.TenantId && existing.CartId == cartId)
            .ToListAsync();

        var canonical = Assert.Single(persisted);
        Assert.Equal(session.Id, canonical.Id);
        Assert.Equal("Ada Lovelace", canonical.CustomerName);
    }

    private static async Task<CheckoutSession> RunCreateOrResumeAsync(
        CheckoutSessionService service,
        Guid tenantId,
        Guid cartId,
        string sessionId,
        Task startSignal)
    {
        await startSignal;
        return await service.CreateOrResumeSessionAsync(
            tenantId,
            cartId,
            sessionId,
            onlineCustomerId: null,
            itemsSnapshotJson: """[{"productName":"Canonical Product","quantity":1,"price":150.0}]""",
            cartTotal: 150m,
            currency: "EGP",
            deviceType: "web",
            source: "storefront",
            ct: CancellationToken.None);
    }

    private static CheckoutSessionService CreateService(
        ApplicationDbContext context,
        IEventPublisher? eventPublisher = null)
    {
        if (eventPublisher == null)
        {
            var eventPublisherMock = new Mock<IEventPublisher>();
            eventPublisherMock.SetReturnsDefault(Task.CompletedTask);
            eventPublisher = eventPublisherMock.Object;
        }

        return new CheckoutSessionService(
            context,
            eventPublisher,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionService>.Instance);
    }

    private static CheckoutSessionController CreateController(
        CheckoutSessionService service,
        Guid tenantId,
        CartDto? authoritativeCart = null,
        ICheckoutSessionTokenService? tokenService = null)
    {
        var cartService = new Mock<ICartService>();
        cartService
            .Setup(item => item.GetCartAsync(
                It.IsAny<Guid>(),
                It.IsAny<PointOfSale.API.DTOs.Cart.CartOwner>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(authoritativeCart!);

        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), "CustomerJwt"))
            .ReturnsAsync(AuthenticateResult.NoResult());
        authenticationService
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), "StorefrontJwt"))
            .ReturnsAsync(AuthenticateResult.NoResult());

        var requestServices = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .BuildServiceProvider();

        tokenService ??= CreateCheckoutSessionTokenService();

        var controller = new CheckoutSessionController(
            service,
            tokenService,
            cartService.Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = requestServices
                }
            }
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        return controller;
    }

    private static CartDto BuildAuthoritativeCart(Guid tenantId, Guid cartId, string sessionId)
    {
        return new CartDto
        {
            CartId = cartId,
            TenantId = tenantId,
            OwnerType = "session",
            OwnerId = sessionId,
            Currency = "EGP",
            Subtotal = 150m,
            GrandTotal = 150m,
            LastUpdatedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Items =
            [
                new CartItemDto
                {
                    ProductId = Guid.NewGuid(),
                    Name = "Canonical Product",
                    Quantity = 1,
                    UnitPrice = 150m
                }
            ]
        };
    }

    private static ICheckoutSessionTokenService CreateCheckoutSessionTokenService()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        return new CheckoutSessionTokenService(
            dataProtectionProvider,
            Options.Create(new CheckoutSessionSecurityOptions
            {
                RecoveryTokenTtlMinutes = 20,
                PaymentTokenTtlMinutes = 20,
                ManualWalletUploadTokenTtlMinutes = 20
            }),
            NullLogger<CheckoutSessionTokenService>.Instance);
    }

    private static async Task<SeededDuplicateSessions> SeedLegacyDuplicateSessionsAsync(ICheckoutSessionTestScope scope)
    {
        var cartId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        await using var context = await scope.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync(
            """DROP INDEX IF EXISTS "IX_CheckoutSessions_TenantId_CartId_Active_Unique";""");

        var olderSession = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = scope.TenantId,
            CartId = cartId,
            SessionId = "guest-session-legacy",
            RecoveryToken = "1111111111111111111111111111111111111111111111111111111111111111",
            Status = CheckoutSessionStatus.Active,
            ItemsSnapshotJson = """[{"productName":"Older Product","quantity":1,"price":120.0}]""",
            CartTotal = 120m,
            Currency = "EGP",
            CreatedAtUtc = nowUtc.AddMinutes(-20),
            FirstActivityAtUtc = nowUtc.AddMinutes(-18),
            LastActivityAtUtc = nowUtc.AddMinutes(-10)
        };

        var newerSession = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = scope.TenantId,
            CartId = cartId,
            SessionId = "guest-session-legacy",
            RecoveryToken = "2222222222222222222222222222222222222222222222222222222222222222",
            Status = CheckoutSessionStatus.Active,
            ItemsSnapshotJson = """[{"productName":"Canonical Product","quantity":1,"price":150.0}]""",
            CartTotal = 150m,
            Currency = "EGP",
            CustomerName = "Deterministic Customer",
            Phone = "01011112222",
            CreatedAtUtc = nowUtc.AddMinutes(-5),
            FirstActivityAtUtc = nowUtc.AddMinutes(-4),
            LastActivityAtUtc = nowUtc.AddMinutes(-1)
        };

        context.CheckoutSessions.AddRange(olderSession, newerSession);
        await context.SaveChangesAsync();

        return new SeededDuplicateSessions(olderSession, newerSession);
    }

    private sealed record SeededDuplicateSessions(
        CheckoutSession OlderSession,
        CheckoutSession NewerSession);

    private sealed class ThrowingCheckoutSessionStartPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            if (@event is CheckoutSessionStartedEvent)
            {
                throw new InvalidOperationException("outbox unavailable");
            }

            return Task.CompletedTask;
        }

        public async Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                await PublishAsync(@event, cancellationToken);
            }
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent =>
            Task.CompletedTask;
    }

    private interface ICheckoutSessionTestScope : IAsyncDisposable
    {
        Guid TenantId { get; }
        Task<ApplicationDbContext> CreateDbContextAsync();
    }

    private sealed class SqliteCheckoutSessionScope : ICheckoutSessionTestScope
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _rootConnection;

        private SqliteCheckoutSessionScope(string connectionString, SqliteConnection rootConnection, Guid tenantId)
        {
            _connectionString = connectionString;
            _rootConnection = rootConnection;
            TenantId = tenantId;
        }

        public Guid TenantId { get; }

        public static async Task<SqliteCheckoutSessionScope> CreateAsync()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"checkout-session-consistency-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                ForeignKeys = true,
                DefaultTimeout = 30
            }.ToString();

            var rootConnection = new SqliteConnection(connectionString);
            await rootConnection.OpenAsync();
            RegisterSqliteFunctions(rootConnection);

            var scope = new SqliteCheckoutSessionScope(connectionString, rootConnection, Guid.NewGuid());

            await using var context = await scope.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();

            context.Tenants.Add(new Tenant
            {
                Id = scope.TenantId,
                Name = "Checkout Consistency Tenant",
                Currency = "EGP",
                IsActive = true,
                Status = TenantStatus.Active,
                Slug = $"checkout-{scope.TenantId:N}"
            });
            await context.SaveChangesAsync();

            return scope;
        }

        public async Task<ApplicationDbContext> CreateDbContextAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            RegisterSqliteFunctions(connection);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .EnableDetailedErrors()
                .Options;

            return new SqliteCheckoutSessionDbContext(options, new TestTenantContext(TenantId));
        }

        public async ValueTask DisposeAsync()
        {
            await _rootConnection.DisposeAsync();
        }
    }

    private sealed class SqliteCheckoutSessionDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
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
            }
        }
    }

    private sealed class PostgresCheckoutSessionScope : ICheckoutSessionTestScope
    {
        private readonly TestDatabaseScope _databaseScope;
        private readonly TestTenantContext _tenantContext;

        private PostgresCheckoutSessionScope(TestDatabaseScope databaseScope, Guid tenantId)
        {
            _databaseScope = databaseScope;
            TenantId = tenantId;
            _tenantContext = new TestTenantContext(tenantId);
        }

        public Guid TenantId { get; }

        public static async Task<PostgresCheckoutSessionScope> CreateAsync()
        {
            if (!RealPostgresTestSupport.TryCreateScope(
                out var databaseScope,
                out var tenantId,
                out var tenantContext))
            {
                throw new InvalidOperationException(
                    "PostgreSQL test infrastructure is not available. " +
                    "Ensure ConnectionStrings:DefaultConnection is configured in test settings.");
            }

            var scope = new PostgresCheckoutSessionScope(databaseScope!, tenantId);

            await using var context = scope.CreateDbContext();
            context.Tenants.Add(new Tenant
            {
                Id = scope.TenantId,
                Name = "Checkout Consistency Tenant (Postgres)",
                Currency = "EGP",
                IsActive = true,
                Status = TenantStatus.Active,
                Slug = $"checkout-pg-{scope.TenantId:N}"
            });
            await context.SaveChangesAsync();

            return scope;
        }

        public Task<ApplicationDbContext> CreateDbContextAsync()
        {
            return Task.FromResult(_databaseScope.CreateDbContext(_tenantContext));
        }

        public ApplicationDbContext CreateDbContext()
        {
            return _databaseScope.CreateDbContext(_tenantContext);
        }

        public async ValueTask DisposeAsync()
        {
            await _databaseScope.DisposeAsync();
        }
    }

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>(
            "btrim",
            input => input?.Trim());
        connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (input, characters) => input?.Trim(characters?.ToCharArray() ?? []));
        connection.CreateFunction<string?, long?>(
            "char_length",
            input => input?.Length);

        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                return Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }
}
