using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.API.Controllers;
using PointOfSale.Infrastructure.Analytics.Delivery;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Analytics;

public sealed class ProviderDeliveryInfrastructureTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void RetryEngine_HonorsRetryAfterAndMaxAttempts()
    {
        var engine = new ProviderDeliveryRetryEngine();
        var options = new ProviderDeliveryOptions { MaxAttempts = 3 };
        var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

        var retry = engine.Classify(
            ProviderDeliverySendResult.Fail(429, "rate limit", "HTTP 429", TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(42)),
            nextAttemptNumber: 2,
            options,
            now);

        Assert.True(retry.ShouldRetry);
        Assert.Equal("rate_limited", retry.FailureClass);
        Assert.Equal(now.AddSeconds(42), retry.NextRetryAtUtc);

        var terminal = engine.Classify(
            ProviderDeliverySendResult.Fail(500, "down", "HTTP 500", TimeSpan.FromMilliseconds(10)),
            nextAttemptNumber: 3,
            options,
            now);

        Assert.True(terminal.IsTerminal);
        Assert.Equal("max_attempts_exceeded", terminal.Diagnostic);
    }

    [Fact]
    public async Task DuplicateProtection_CollapsesSameProviderDeliveryKey()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IProviderDeliveryOutboxService>();
        var request = CreateRequest("same-key");

        var first = await outbox.EnqueueAsync(request);
        var second = await outbox.EnqueueAsync(request);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(first.Enqueued);
        Assert.True(second.Duplicate);
        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(1, await db.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Processor_DeadLettersTerminalPayloadFailuresWithMaskedDiagnostics()
    {
        using var provider = CreateProvider(new QueueingSender(
            ProviderDeliverySendResult.Fail(
                400,
                "bad payload access_token=super-secret",
                "HTTP 400 bearer token-value",
                TimeSpan.FromMilliseconds(5))));

        var messageId = await EnqueueAsync(provider, "terminal-key");

        await ProcessAsync(provider);

        var message = await FindMessageAsync(provider, messageId);
        Assert.True(message.IsDeadLettered);
        Assert.True(message.IsFailed);
        Assert.Equal("payload_rejected", message.FailureClass);
        Assert.False(message.LastResponseBody!.Contains("super-secret", StringComparison.Ordinal));
        Assert.False(message.LastError!.Contains("token-value", StringComparison.Ordinal));
        Assert.False(message.ReplayEligible);
    }

    [Fact]
    public async Task Replay_ReopensReplayEligibleDeadLetters()
    {
        using var provider = CreateProvider();
        var messageId = await EnqueueAsync(provider, "replay-key");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().SingleAsync();
            message.RecordTerminalFailure(
                DateTime.UtcNow,
                "credential_rejected",
                "credential rejected",
                401,
                null,
                replayEligible: true,
                metadataJson: null);
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IProviderDeliveryOutboxService>();
            Assert.True(await outbox.ReplayAsync(messageId, "manual replay"));
        }

        var replayed = await FindMessageAsync(provider, messageId);
        Assert.False(replayed.IsDeadLettered);
        Assert.False(replayed.IsFailed);
        Assert.Equal(1, replayed.ReplayCount);
        Assert.Equal(0, replayed.AttemptCount);

        using var auditScope = provider.CreateScope();
        var auditDb = auditScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await auditDb.Set<ProviderDeliveryReplayAudit>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(messageId, audit.MessageId);
        Assert.Equal("manual replay", audit.Reason);
        Assert.Equal("system", audit.ReplaySource);
        Assert.Contains("\"isDeadLettered\":true", audit.PreviousStateJson, StringComparison.Ordinal);
        Assert.Contains("\"isDeadLettered\":false", audit.NewStateJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayAfterCredentialChange_ReopensCredentialFailuresOnly()
    {
        using var provider = CreateProvider();
        var credentialId = await EnqueueAsync(provider, "credential-key");
        var payloadId = await EnqueueAsync(provider, "payload-key");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var messages = await db.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().ToListAsync();
            messages.Single(m => m.Id == credentialId).RecordTerminalFailure(DateTime.UtcNow, "credential_rejected", "bad token", 401, null, true, null);
            messages.Single(m => m.Id == payloadId).RecordTerminalFailure(DateTime.UtcNow, "payload_rejected", "bad payload", 400, null, false, null);
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IProviderDeliveryOutboxService>();
            Assert.Equal(1, await outbox.ReplayAfterCredentialChangeAsync(TenantId, AnalyticsProvider.GoogleAnalytics));
        }

        Assert.False((await FindMessageAsync(provider, credentialId)).IsDeadLettered);
        Assert.True((await FindMessageAsync(provider, payloadId)).IsDeadLettered);
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdAndResetsOnSuccess()
    {
        var breaker = new InMemoryProviderCircuitBreaker();
        var options = new ProviderDeliveryOptions { CircuitFailureThreshold = 2, CircuitOpenMinutes = 5 };

        await breaker.RecordFailureAsync(AnalyticsProvider.Meta, TenantId, options);
        Assert.True(await breaker.CanAttemptAsync(AnalyticsProvider.Meta, TenantId));

        await breaker.RecordFailureAsync(AnalyticsProvider.Meta, TenantId, options);
        Assert.False(await breaker.CanAttemptAsync(AnalyticsProvider.Meta, TenantId));

        await breaker.RecordSuccessAsync(AnalyticsProvider.Meta, TenantId);
        Assert.True(await breaker.CanAttemptAsync(AnalyticsProvider.Meta, TenantId));
    }

    [Fact]
    public async Task CircuitBreaker_TransitionsOpenToHalfOpenAndLimitsProbes()
    {
        var breaker = new InMemoryProviderCircuitBreaker();
        var options = new ProviderDeliveryOptions
        {
            CircuitFailureThreshold = 1,
            CircuitOpenMinutes = -1,
            CircuitHalfOpenMaxProbes = 1,
            CircuitRecoveryWindowSeconds = 3600
        };

        await breaker.RecordFailureAsync(AnalyticsProvider.Meta, TenantId, options);

        var state = await breaker.GetStateAsync(AnalyticsProvider.Meta, TenantId);
        Assert.Equal(ProviderCircuitStates.HalfOpen, state.State);
        Assert.True(await breaker.CanAttemptAsync(AnalyticsProvider.Meta, TenantId));
        Assert.False(await breaker.CanAttemptAsync(AnalyticsProvider.Meta, TenantId));

        await breaker.RecordSuccessAsync(AnalyticsProvider.Meta, TenantId);
        Assert.Equal(ProviderCircuitStates.Closed, (await breaker.GetStateAsync(AnalyticsProvider.Meta, TenantId)).State);
    }

    [Fact]
    public async Task Processor_RecoversExpiredLeases()
    {
        using var provider = CreateProvider(new QueueingSender(
            ProviderDeliverySendResult.Ok(204, "", TimeSpan.FromMilliseconds(5))));
        var messageId = await EnqueueAsync(provider, "lease-key");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().SingleAsync();
            message.ClaimedUntilUtc = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(1));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await ProcessAsync(provider));
        Assert.NotNull((await FindMessageAsync(provider, messageId)).DeliveredAtUtc);
    }

    [Fact]
    public async Task Processor_DoesNotStealActiveLeaseButProcessesAfterExpiration()
    {
        using var provider = CreateProvider(new QueueingSender(
            ProviderDeliverySendResult.Ok(204, "", TimeSpan.FromMilliseconds(5))));
        var messageId = await EnqueueAsync(provider, "crash-recovery-key");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().SingleAsync();
            message.ClaimedUntilUtc = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await ProcessAsync(provider));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().SingleAsync();
            message.ClaimedUntilUtc = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(1));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await ProcessAsync(provider));
        Assert.NotNull((await FindMessageAsync(provider, messageId)).DeliveredAtUtc);
    }

    [Fact]
    public async Task Processor_ConcurrentWorkersAcquireLeaseOnce()
    {
        await using var provider = CreateSqliteProvider(new CountingSender(TimeSpan.FromMilliseconds(100)));
        var messageId = await EnqueueAsync(provider, "atomic-lease-key");

        var processors = Enumerable.Range(0, 8)
            .Select(_ => ProcessAsync(provider))
            .ToArray();

        var results = await Task.WhenAll(processors);

        Assert.Equal(1, results.Sum());
        Assert.NotNull((await FindMessageAsync(provider, messageId)).DeliveredAtUtc);

        var sender = provider.GetRequiredService<CountingSender>();
        Assert.Equal(1, sender.SendCount);
    }

    [Fact]
    public async Task ProviderDeliveryHttpSender_SendsGenericRequestWithoutProviderRouting()
    {
        var handler = new CapturingHandler();
        var sender = new ProviderDeliveryHttpSender(
            new FixedHttpClientFactory(new HttpClient(handler)),
            Array.Empty<IProviderDeliveryHttpRequestCustomizer>(),
            Options.Create(new ProviderDeliveryOptions()),
            NullLogger<ProviderDeliveryHttpSender>.Instance);

        var message = ProviderDeliveryOutboxMessage.Create(
            TenantId,
            "AnyProvider",
            "event",
            "any-key",
            "hash",
            """{"ok":true}""",
            "correlation-id",
            DateTime.UtcNow,
            "https://provider.example.test/events",
            "PUT",
            "application/json",
            """{"X-Test":"yes"}""",
            7);

        var result = await sender.SendAsync(message);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal("https://provider.example.test/events", handler.Request.RequestUri!.ToString());
        Assert.True(handler.Request.Headers.TryGetValues("X-Test", out var values));
        Assert.Equal("yes", Assert.Single(values));
    }

    [Fact]
    public async Task AdminDeadLetterApis_ListAndReturnDetailsWithReplayAudits()
    {
        using var provider = CreateProvider();
        var messageId = await EnqueueAsync(provider, "admin-read-key");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().SingleAsync();
            message.RecordTerminalFailure(DateTime.UtcNow, "credential_rejected", "bad token", 401, null, true, null);
            await db.SaveChangesAsync();

            var outbox = scope.ServiceProvider.GetRequiredService<IProviderDeliveryOutboxService>();
            Assert.True(await outbox.ReplayAsync(TenantId, messageId, "operator replay", "admin", "operator-1"));
            message.RecordTerminalFailure(DateTime.UtcNow, "credential_rejected", "bad token", 401, null, true, null);
            await db.SaveChangesAsync();
        }

        using var readScope = provider.CreateScope();
        var controller = new AdminProviderDeliveryController(
            readScope.ServiceProvider.GetRequiredService<IProviderDeliveryOutboxService>(),
            readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            new StaticTenantContext(TenantId));

        var listResult = await controller.ListDeadLetters(
            new ProviderDeliveryDeadLetterQuery(AnalyticsProvider.GoogleAnalytics, TenantId, "credential_rejected", null, null, "dead_lettered", 1, 10),
            CancellationToken.None);
        var page = Assert.IsType<ProviderDeliveryDeadLetterPageResponse>(Assert.IsType<OkObjectResult>(listResult.Result).Value);
        Assert.Equal(1, page.Total);

        var detailResult = await controller.GetDeadLetter(messageId, CancellationToken.None);
        var detail = Assert.IsType<ProviderDeliveryDeadLetterDetail>(Assert.IsType<OkObjectResult>(detailResult.Result).Value);
        Assert.Equal(messageId, detail.MessageId);
        Assert.Single(detail.ReplayAudits);
    }

    [Fact]
    public void ProviderDeliveryTelemetry_ExposesActivitySource()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ProviderDeliveryTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = ProviderDeliveryTelemetry.ActivitySource.StartActivity("test");

        Assert.NotNull(activity);
    }

    private static async Task<int> ProcessAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IProviderDeliveryProcessor>();
        return await processor.ProcessPendingMessagesAsync(10);
    }

    private static async Task<Guid> EnqueueAsync(ServiceProvider provider, string key)
    {
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IProviderDeliveryOutboxService>();
        return (await outbox.EnqueueAsync(CreateRequest(key))).MessageId;
    }

    private static async Task<ProviderDeliveryOutboxMessage> FindMessageAsync(
        ServiceProvider provider,
        Guid messageId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .SingleAsync(message => message.Id == messageId);
    }

    private static ProviderDeliveryRequest CreateRequest(string key) =>
        new(
            TenantId,
            AnalyticsProvider.GoogleAnalytics,
            "purchase",
            key,
            """{"events":[{"name":"purchase"}]}""",
            "correlation-id",
            DateTime.UtcNow);

    private static ServiceProvider CreateProvider(QueueingSender? sender = null)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddLogging();
        services.AddSingleton(Options.Create(new ProviderDeliveryOptions
        {
            MaxAttempts = 2,
            BaseRetryDelaySeconds = 1,
            CircuitFailureThreshold = 100,
            MaxParallelism = 1
        }));
        services.AddSingleton<ProviderDeliveryMetrics>();
        services.AddScoped<IProviderDeliveryOutboxService, ProviderDeliveryOutboxService>();
        services.AddScoped<IProviderDeliveryProcessor, ProviderDeliveryProcessor>();
        services.AddSingleton<IProviderDeliveryRetryEngine, ProviderDeliveryRetryEngine>();
        services.AddSingleton<IProviderCircuitBreaker, InMemoryProviderCircuitBreaker>();
        services.AddSingleton(sender ?? new QueueingSender(
            ProviderDeliverySendResult.Ok(204, "", TimeSpan.FromMilliseconds(5))));
        services.AddScoped<IProviderDeliveryHttpSender>(sp => sp.GetRequiredService<QueueingSender>());

        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateSqliteProvider(CountingSender sender)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        CreateProviderDeliverySqliteSchema(connection);
        services.AddSingleton(connection);
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));
        services.AddLogging();
        services.AddSingleton(Options.Create(new ProviderDeliveryOptions
        {
            MaxAttempts = 2,
            BaseRetryDelaySeconds = 1,
            CircuitFailureThreshold = 100,
            MaxParallelism = 1,
            ProcessingLeaseSeconds = 5
        }));
        services.AddSingleton<ProviderDeliveryMetrics>();
        services.AddScoped<IProviderDeliveryOutboxService, ProviderDeliveryOutboxService>();
        services.AddScoped<IProviderDeliveryProcessor, ProviderDeliveryProcessor>();
        services.AddSingleton<IProviderDeliveryRetryEngine, ProviderDeliveryRetryEngine>();
        services.AddSingleton<IProviderCircuitBreaker, InMemoryProviderCircuitBreaker>();
        services.AddSingleton(sender);
        services.AddScoped<IProviderDeliveryHttpSender>(sp => sp.GetRequiredService<CountingSender>());

        return services.BuildServiceProvider();
    }

    private static void CreateProviderDeliverySqliteSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE "ProviderDeliveryOutbox" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProviderDeliveryOutbox" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "Provider" TEXT NOT NULL,
                "RequestType" TEXT NOT NULL,
                "DeliveryKey" TEXT NOT NULL,
                "PayloadHash" TEXT NOT NULL,
                "PayloadJson" TEXT NOT NULL,
                "EndpointUrl" TEXT NOT NULL,
                "HttpMethod" TEXT NOT NULL,
                "ContentType" TEXT NOT NULL,
                "HeadersJson" TEXT NULL,
                "TimeoutSeconds" INTEGER NULL,
                "CorrelationId" TEXT NULL,
                "OccurredAtUtc" TEXT NOT NULL,
                "DeliveredAtUtc" TEXT NULL,
                "ClaimedUntilUtc" TEXT NULL,
                "NextRetryAtUtc" TEXT NULL,
                "AttemptCount" INTEGER NOT NULL,
                "ReplayCount" INTEGER NOT NULL,
                "IsFailed" INTEGER NOT NULL,
                "IsDeadLettered" INTEGER NOT NULL,
                "ReplayEligible" INTEGER NOT NULL,
                "DeadLetteredAtUtc" TEXT NULL,
                "FailureClass" TEXT NULL,
                "LastError" TEXT NULL,
                "LastHttpStatusCode" INTEGER NULL,
                "LastResponseBody" TEXT NULL,
                "FailureMetadataJson" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX "IX_ProviderDelivery_Tenant_Provider_DeliveryKey"
                ON "ProviderDeliveryOutbox" ("TenantId", "Provider", "DeliveryKey");

            CREATE TABLE "ProviderDeliveryReplayAudits" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProviderDeliveryReplayAudits" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "ReplayId" TEXT NOT NULL,
                "MessageId" TEXT NOT NULL,
                "Provider" TEXT NOT NULL,
                "OperatorUserId" TEXT NULL,
                "ReplayedAtUtc" TEXT NOT NULL,
                "Reason" TEXT NOT NULL,
                "ReplaySource" TEXT NOT NULL,
                "PreviousStateJson" TEXT NOT NULL,
                "NewStateJson" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private sealed class QueueingSender : IProviderDeliveryHttpSender
    {
        private readonly Queue<ProviderDeliverySendResult> _results;

        public QueueingSender(params ProviderDeliverySendResult[] results)
        {
            _results = new Queue<ProviderDeliverySendResult>(results);
        }

        public Task<ProviderDeliverySendResult> SendAsync(
            ProviderDeliveryOutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results.Count == 0
                ? ProviderDeliverySendResult.Ok(204, "", TimeSpan.FromMilliseconds(1))
                : _results.Dequeue());
        }
    }

    private sealed class CountingSender : IProviderDeliveryHttpSender
    {
        private readonly TimeSpan _delay;
        private int _sendCount;

        public CountingSender(TimeSpan delay)
        {
            _delay = delay;
        }

        public int SendCount => _sendCount;

        public async Task<ProviderDeliverySendResult> SendAsync(
            ProviderDeliveryOutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sendCount);
            await Task.Delay(_delay, cancellationToken);
            return ProviderDeliverySendResult.Ok(204, "", TimeSpan.FromMilliseconds(5));
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FixedHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StaticTenantContext : PointOfSale.Core.Interfaces.ITenantContext
    {
        public StaticTenantContext(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public Guid? TenantId { get; }
        public Guid RequiredTenantId => TenantId!.Value;
    }
}
