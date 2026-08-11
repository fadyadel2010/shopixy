using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CustomDomains;

public sealed class DomainVerificationSchedulerTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CandidateQuery_DiscoversOnlyEligiblePendingVerificationDomains()
    {
        var tenantId = Guid.NewGuid();
        var inactiveTenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        AddTenant(context, inactiveTenantId, active: false);

        var pending = AddDomain(context, tenantId, "pending.example.com", DomainLifecycleState.PendingVerification);
        AddDomain(context, tenantId, "draft.example.com", DomainLifecycleState.Draft);
        AddDomain(context, tenantId, "verified.example.com", DomainLifecycleState.Verified);
        AddDomain(context, tenantId, "archived.example.com", DomainLifecycleState.Archived);
        AddDomain(context, tenantId, "disabled.example.com", DomainLifecycleState.Disabled);
        AddDomain(context, tenantId, "deleted.example.com", DomainLifecycleState.PendingVerification, isDeleted: true);
        AddDomain(context, tenantId, "expired.example.com", DomainLifecycleState.PendingVerification, updatedAtUtc: NowUtc.AddMinutes(-6));
        AddDomain(context, inactiveTenantId, "inactive-tenant.example.com", DomainLifecycleState.PendingVerification);
        await context.SaveChangesAsync();

        var query = new DomainVerificationSchedulerCandidateQuery(context);

        var candidates = await query.GetCandidatesAsync(NowUtc, CreateOptions());

        var candidate = Assert.Single(candidates);
        Assert.Equal(pending.Id, candidate.DomainId);
        Assert.Equal(tenantId, candidate.TenantId);
    }

    [Fact]
    public async Task CandidateQuery_RespectsBatchSizePollingIntervalAndAttemptRateLimit()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);

        var first = AddDomain(context, tenantId, "first.example.com", DomainLifecycleState.PendingVerification, updatedAtUtc: NowUtc.AddMinutes(-4));
        var second = AddDomain(context, tenantId, "second.example.com", DomainLifecycleState.PendingVerification, updatedAtUtc: NowUtc.AddMinutes(-3));
        var recentAttempt = AddDomain(context, tenantId, "recent.example.com", DomainLifecycleState.PendingVerification);
        var rateLimited = AddDomain(context, tenantId, "rate-limited.example.com", DomainLifecycleState.PendingVerification);

        AddVerification(context, recentAttempt.Id, NowUtc.AddSeconds(-5), VerificationStatus.Failed);
        for (var i = 0; i < 5; i++)
        {
            AddVerification(context, rateLimited.Id, NowUtc.AddMinutes(-(i + 1)), VerificationStatus.Failed);
        }

        await context.SaveChangesAsync();

        var query = new DomainVerificationSchedulerCandidateQuery(context);
        var options = CreateOptions(batchSize: 1);

        var candidates = await query.GetCandidatesAsync(NowUtc, options);

        var candidate = Assert.Single(candidates);
        Assert.Equal(first.Id, candidate.DomainId);
        Assert.DoesNotContain(candidates, c => c.DomainId == second.Id);
        Assert.DoesNotContain(candidates, c => c.DomainId == recentAttempt.Id);
        Assert.DoesNotContain(candidates, c => c.DomainId == rateLimited.Id);
    }

    [Fact]
    public async Task CandidateQuery_DiscoversNinetyDayReverificationWithoutTakingDomainOffline()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var stale = AddDomain(context, tenantId, "stale-active.example.com", DomainLifecycleState.SslActive);
        stale.VerifiedAtUtc = NowUtc.AddDays(-91);
        var recent = AddDomain(context, tenantId, "recent-active.example.com", DomainLifecycleState.SslActive);
        recent.VerifiedAtUtc = NowUtc.AddDays(-30);
        await context.SaveChangesAsync();

        var candidates = await new DomainVerificationSchedulerCandidateQuery(context)
            .GetCandidatesAsync(NowUtc, CreateOptions());

        var candidate = Assert.Single(candidates);
        Assert.Equal(stale.Id, candidate.DomainId);
        Assert.Equal(DomainLifecycleState.SslActive, stale.LifecycleState);
        Assert.DoesNotContain(candidates, item => item.DomainId == recent.Id);
    }

    [Fact]
    public async Task Job_InvokesVerificationServiceWithDomainTenantCorrelationAndCancellationToken()
    {
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();
        var query = new SequenceCandidateQuery(
        [
            [new DomainVerificationSchedulerCandidate(domainId, tenantId, NowUtc)]
        ]);
        var verificationService = new RecordingVerificationService();
        using var telemetry = new DomainVerificationSchedulerTelemetry();
        var job = CreateJob(query, verificationService, telemetry);

        using var cts = new CancellationTokenSource();
        await job.ExecuteAsync(cancellationToken: cts.Token);

        var invocation = Assert.Single(verificationService.Invocations);
        Assert.Equal(domainId, invocation.DomainId);
        Assert.Equal(tenantId, invocation.TenantId);
        Assert.Contains(domainId.ToString("N"), invocation.CorrelationId, StringComparison.Ordinal);
        Assert.True(invocation.CancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task Job_ProcessesMultipleBatchesAndIsolatesSingleDomainFailure()
    {
        var tenantId = Guid.NewGuid();
        var domainA = Guid.NewGuid();
        var domainB = Guid.NewGuid();
        var domainC = Guid.NewGuid();
        var query = new SequenceCandidateQuery(
        [
            [
                new DomainVerificationSchedulerCandidate(domainA, tenantId, NowUtc),
                new DomainVerificationSchedulerCandidate(domainB, tenantId, NowUtc)
            ],
            [
                new DomainVerificationSchedulerCandidate(domainC, tenantId, NowUtc)
            ]
        ]);
        var verificationService = new RecordingVerificationService
        {
            ThrowForDomainId = domainB
        };
        using var telemetry = new DomainVerificationSchedulerTelemetry();
        var job = CreateJob(query, verificationService, telemetry, batchSize: 2, maxConcurrent: 2);

        await job.ExecuteAsync();

        Assert.Equal(3, verificationService.Invocations.Count);
        Assert.Contains(verificationService.Invocations, invocation => invocation.DomainId == domainA);
        Assert.Contains(verificationService.Invocations, invocation => invocation.DomainId == domainB);
        Assert.Contains(verificationService.Invocations, invocation => invocation.DomainId == domainC);
    }

    [Fact]
    public async Task Job_SurfacesInfrastructureFailureFromCandidateDiscovery()
    {
        var query = new ThrowingCandidateQuery();
        var verificationService = new RecordingVerificationService();
        using var telemetry = new DomainVerificationSchedulerTelemetry();
        var job = CreateJob(query, verificationService, telemetry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());
        Assert.Empty(verificationService.Invocations);
    }

    [Fact]
    public void Options_ValidateSchedulerConfiguration()
    {
        Assert.True(CreateOptions().Validate(out _));
        Assert.False(CreateOptions(pollIntervalSeconds: 0).Validate(out _));
        Assert.False(CreateOptions(batchSize: 0).Validate(out _));
        Assert.False(CreateOptions(maxConcurrent: 0).Validate(out _));
        Assert.False(CreateOptions(pollIntervalSeconds: 600, maxWindowMinutes: 5).Validate(out _));
    }

    [Fact]
    public void SchedulerSource_MustNotContainVerificationBusinessLogicOrSecretLogging()
    {
        var schedulerSource = File.ReadAllText(Path.Combine(
            TestConfiguration.GetSolutionRoot(),
            "PointOfSale.API",
            "Services",
            "Storefront",
            "DomainVerificationSchedulerJob.cs"));
        var querySource = File.ReadAllText(Path.Combine(
            TestConfiguration.GetSolutionRoot(),
            "PointOfSale.API",
            "Services",
            "Storefront",
            "DomainVerificationSchedulerCandidateQuery.cs"));

        var combined = schedulerSource + querySource;

        Assert.DoesNotContain("DnsClient", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ILookupClient", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveTxtAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveCnameAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveHttpFileAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("VerificationToken", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedValue", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolvedValue", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkVerified", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkPendingVerification", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("DomainVerifiedEvent", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("DomainVerificationFailedEvent", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", combined, StringComparison.Ordinal);
        Assert.Contains("IDomainVerificationService", schedulerSource, StringComparison.Ordinal);
    }

    private static DomainVerificationSchedulerJob CreateJob(
        IDomainVerificationSchedulerCandidateQuery query,
        IDomainVerificationService verificationService,
        DomainVerificationSchedulerTelemetry telemetry,
        int batchSize = 50,
        int maxConcurrent = 10)
    {
        var services = new ServiceCollection()
            .AddSingleton(verificationService)
            .BuildServiceProvider();

        return new DomainVerificationSchedulerJob(
            query,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(CreateOptions(batchSize: batchSize, maxConcurrent: maxConcurrent)),
            telemetry,
            NullLogger<DomainVerificationSchedulerJob>.Instance);
    }

    private static DomainVerificationSchedulerOptions CreateOptions(
        int pollIntervalSeconds = 10,
        int maxWindowMinutes = 5,
        int batchSize = 50,
        int maxConcurrent = 10) =>
        new()
        {
            Enabled = true,
            PollIntervalSeconds = pollIntervalSeconds,
            MaxVerificationWindowMinutes = maxWindowMinutes,
            BatchSize = batchSize,
            MaxConcurrentVerifications = maxConcurrent,
        };

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DomainVerificationSchedulerTests_{Guid.NewGuid():N}", new InMemoryDatabaseRoot())
            .Options;
        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Database.EnsureCreated();
        return context;
    }

    private static void AddTenant(ApplicationDbContext context, Guid tenantId, bool active = true)
    {
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"Tenant {tenantId:N}",
            IsActive = active,
            Status = active ? TenantStatus.Active : TenantStatus.Suspended,
            CreatedAtUtc = NowUtc.AddDays(-1),
            DeletedAtUtc = null,
        });
    }

    private static Domain AddDomain(
        ApplicationDbContext context,
        Guid tenantId,
        string hostname,
        DomainLifecycleState lifecycleState,
        bool isDeleted = false,
        DateTime? updatedAtUtc = null)
    {
        var domain = new Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = hostname,
            NormalizedHostName = hostname,
            DomainType = DomainType.Alias,
            LifecycleState = lifecycleState,
            VerificationMethod = VerificationMethod.DnsTxt,
            VerificationToken = "shopixy-domain-verify-test",
            RedirectType = RedirectType.None,
            CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
            CreatedAtUtc = NowUtc.AddMinutes(-4),
            UpdatedAtUtc = updatedAtUtc ?? NowUtc.AddMinutes(-1),
            IsDeleted = isDeleted,
        };

        context.Domains.Add(domain);
        return domain;
    }

    private static void AddVerification(
        ApplicationDbContext context,
        Guid domainId,
        DateTime attemptedAtUtc,
        VerificationStatus status)
    {
        context.DomainVerifications.Add(new DomainVerification
        {
            Id = Guid.NewGuid(),
            DomainId = domainId,
            Method = VerificationMethod.DnsTxt,
            Token = "shopixy-domain-verify-test",
            Status = status,
            AttemptedAtUtc = attemptedAtUtc,
            FailureReason = status == VerificationStatus.Failed ? DomainVerificationFailureReasons.RecordMissing : null,
            Attempt = 1,
            CreatedAt = attemptedAtUtc,
            IsDeleted = false,
        });
    }

    private sealed class SequenceCandidateQuery : IDomainVerificationSchedulerCandidateQuery
    {
        private readonly Queue<IReadOnlyList<DomainVerificationSchedulerCandidate>> _batches;

        public SequenceCandidateQuery(IEnumerable<IReadOnlyList<DomainVerificationSchedulerCandidate>> batches)
        {
            _batches = new Queue<IReadOnlyList<DomainVerificationSchedulerCandidate>>(batches);
        }

        public Task<IReadOnlyList<DomainVerificationSchedulerCandidate>> GetCandidatesAsync(
            DateTime nowUtc,
            DomainVerificationSchedulerOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_batches.Count == 0 ? [] : _batches.Dequeue());
        }
    }

    private sealed class ThrowingCandidateQuery : IDomainVerificationSchedulerCandidateQuery
    {
        public Task<IReadOnlyList<DomainVerificationSchedulerCandidate>> GetCandidatesAsync(
            DateTime nowUtc,
            DomainVerificationSchedulerOptions options,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database unavailable");
    }

    private sealed class RecordingVerificationService : IDomainVerificationService
    {
        public Guid? ThrowForDomainId { get; init; }
        public List<Invocation> Invocations { get; } = [];

        public Task<VerificationResult> VerifyAsync(
            Guid domainId,
            Guid tenantId,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(new Invocation(domainId, tenantId, correlationId, cancellationToken));

            if (ThrowForDomainId == domainId)
            {
                throw new InvalidOperationException("single domain failed");
            }

            return Task.FromResult(new VerificationResult
            {
                DomainId = domainId,
                TenantId = tenantId,
                CorrelationId = correlationId,
                Status = VerificationStatus.Success,
                Method = VerificationMethod.DnsTxt,
                AttemptNumber = 1,
                ResolvedAtUtc = NowUtc,
                ResolverEndpoint = "fake",
                RetryEligible = false,
            });
        }
    }

    private sealed record Invocation(
        Guid DomainId,
        Guid TenantId,
        string CorrelationId,
        CancellationToken CancellationToken);
}
