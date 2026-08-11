using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Jobs;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Architecture;
using PointOfSale.Tests.Fakes;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CustomDomains;

public sealed class Domain56CertificateRenewalSchedulerTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CandidateQuery_SelectsDueActivePrimaryLinkedCertificatesOnly()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var dueDomain = AddDomain(context, tenantId, "due.example.com", DomainLifecycleState.SslActive);
        var dueCertificate = AddActiveCertificate(context, tenantId, dueDomain, nextRenewalUtc: NowUtc.AddMinutes(-1), notAfterUtc: NowUtc.AddDays(20));
        AddActiveCertificate(context, tenantId, AddDomain(context, tenantId, "later.example.com", DomainLifecycleState.SslActive), nextRenewalUtc: NowUtc.AddDays(10), notAfterUtc: NowUtc.AddDays(80));
        AddActiveCertificate(context, tenantId, AddDomain(context, tenantId, "archived.example.com", DomainLifecycleState.Archived), nextRenewalUtc: NowUtc.AddMinutes(-1), notAfterUtc: NowUtc.AddDays(20));
        AddActiveCertificate(context, tenantId, AddDomain(context, tenantId, "deleted.example.com", DomainLifecycleState.SslActive, isDeleted: true), nextRenewalUtc: NowUtc.AddMinutes(-1), notAfterUtc: NowUtc.AddDays(20));
        await context.SaveChangesAsync();

        var candidates = await new CertificateRenewalSchedulerCandidateQuery(context)
            .GetCandidatesAsync(NowUtc, Options());

        var candidate = Assert.Single(candidates);
        Assert.Equal(dueDomain.Id, candidate.DomainId);
        Assert.Equal(dueCertificate.Id, candidate.CertificateId);
        Assert.Equal("renewal_due", candidate.ExpirySeverity);
    }

    [Fact]
    public async Task Renewal_ReconcilesObservedReplacementActivatesNewGenerationAndRetiresOldAfterProof()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "renew.example.com", DomainLifecycleState.SslActive);
        var oldCertificate = AddActiveCertificate(context, tenantId, domain, nextRenewalUtc: NowUtc.AddMinutes(-5), notAfterUtc: NowUtc.AddDays(10));
        await context.SaveChangesAsync();

        var events = new RecordingEventPublisher();
        var probe = new RenewalProbe("NEW_THUMBPRINT", "NEW_SERIAL", NowUtc.AddDays(90));
        var activation = new CertificateActivationService(
            context,
            new FakeCaddyEdgeClient(),
            probe,
            events,
            NullLogger<CertificateActivationService>.Instance);
        var renewal = CreateRenewalService(context, activation, probe, events);

        var result = await renewal.RenewAsync(ToCandidate(oldCertificate, domain), "corr-renewal");

        var certificates = await context.SslCertificates.IgnoreQueryFilters().OrderBy(c => c.Generation).ToListAsync();
        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Renewed", result.Outcome);
        Assert.Equal(2, certificates.Count);
        Assert.Equal(CertificateStatus.Retired, certificates[0].Status);
        Assert.Equal(CertificateStatus.Active, certificates[1].Status);
        Assert.Equal(certificates[1].Id, reloadedDomain.SslCertificateId);
        Assert.Equal(certificates[1].Id, certificates[0].ReplacementCertificateId);
        Assert.Contains(events.PublishedEvents, e => e is CertificateRenewalStartedEvent);
        Assert.Contains(events.PublishedEvents, e => e is CertificateReplacementIssuedEvent);
        Assert.Contains(events.PublishedEvents, e => e is CertificateRenewedEvent);
        Assert.Contains(events.PublishedEvents, e => e is CertificateGenerationRetiredEvent);
    }

    [Fact]
    public async Task RenewalFailure_PreservesOldActiveCertificateAndSchedulesBoundedRetry()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "fail-renew.example.com", DomainLifecycleState.SslActive);
        var oldCertificate = AddActiveCertificate(context, tenantId, domain, nextRenewalUtc: NowUtc.AddMinutes(-5), notAfterUtc: NowUtc.AddDays(10));
        await context.SaveChangesAsync();

        var events = new RecordingEventPublisher();
        var probe = new RenewalProbe("OLD_THUMBPRINT", "OLD_SERIAL", oldCertificate.NotAfterUtc!.Value);
        var activation = new CertificateActivationService(
            context,
            new FakeCaddyEdgeClient(),
            probe,
            events,
            NullLogger<CertificateActivationService>.Instance);
        var renewal = CreateRenewalService(
            context,
            activation,
            probe,
            events,
            provider: new FakeCertificateProvider { Scenario = FakeCertificateProviderScenario.RateLimited });

        var result = await renewal.RenewAsync(ToCandidate(oldCertificate, domain), "corr-failure");

        var reloadedOld = await context.SslCertificates.IgnoreQueryFilters().SingleAsync(c => c.Id == oldCertificate.Id);
        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        var operation = await context.SslCertificateOperations.IgnoreQueryFilters().SingleAsync(o => o.OperationType == CertificateOperationType.Renewal);

        Assert.False(result.Succeeded);
        Assert.Equal(CertificateStatus.Active, reloadedOld.Status);
        Assert.Equal(oldCertificate.Id, reloadedDomain.SslCertificateId);
        Assert.Equal(CertificateOperationStatus.Failed, operation.Status);
        Assert.True(operation.Retryable);
        Assert.NotNull(operation.RetryAfterUtc);
    }

    [Fact]
    public async Task RenewalFailure_AfterPrimaryCertificateExpiry_RemovesPrimaryAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "expired-primary.example.com", DomainLifecycleState.Primary);
        domain.DomainType = DomainType.Primary;
        var certificate = AddActiveCertificate(
            context,
            tenantId,
            domain,
            nextRenewalUtc: DateTime.UtcNow.AddMinutes(-10),
            notAfterUtc: DateTime.UtcNow.AddMinutes(-1));
        await context.SaveChangesAsync();

        var events = new RecordingEventPublisher();
        var probe = new RenewalProbe("OLD_THUMBPRINT", "OLD_SERIAL", certificate.NotAfterUtc!.Value);
        var activation = new CertificateActivationService(
            context,
            new FakeCaddyEdgeClient(),
            probe,
            events,
            NullLogger<CertificateActivationService>.Instance);
        var renewal = CreateRenewalService(
            context,
            activation,
            probe,
            events,
            provider: new FakeCertificateProvider { Scenario = FakeCertificateProviderScenario.RateLimited });

        var result = await renewal.RenewAsync(ToCandidate(certificate, domain), "corr-expired-primary");

        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        Assert.False(result.Succeeded);
        Assert.Equal(DomainLifecycleState.Verified, reloadedDomain.LifecycleState);
        Assert.Equal(DomainType.Alias, reloadedDomain.DomainType);
        Assert.Null(reloadedDomain.SslCertificateId);
        Assert.Contains(events.PublishedEvents, e => e is CertificateExpiredEvent);
        Assert.Contains(events.PublishedEvents, e => e is DomainSslDegradedEvent);
    }

    [Fact]
    public async Task CandidateQuery_ExcludesRenewalExhaustedValidCertificateUntilExpiry()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "exhausted.example.com", DomainLifecycleState.SslActive);
        var certificate = AddActiveCertificate(context, tenantId, domain, nextRenewalUtc: NowUtc.AddMinutes(-5), notAfterUtc: NowUtc.AddDays(10));
        certificate.FailureCode = CertificateRevocationReasonCodes.RenewalExhausted;
        certificate.Retryable = false;
        await context.SaveChangesAsync();

        var candidates = await new CertificateRenewalSchedulerCandidateQuery(context)
            .GetCandidatesAsync(NowUtc, Options());

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task CandidateQuery_SelectsExpiredRecoveryCertificate()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "expired.example.com", DomainLifecycleState.Verified);
        var certificate = AddActiveCertificate(context, tenantId, domain, nextRenewalUtc: null, notAfterUtc: NowUtc.AddDays(-1));
        certificate.Status = CertificateStatus.Expired;
        certificate.ExpiredAtUtc = NowUtc;
        certificate.FailureCode = CertificateRevocationReasonCodes.Expired;
        certificate.RenewalEligible = false;
        domain.SslCertificateId = null;
        await context.SaveChangesAsync();

        var candidates = await new CertificateRenewalSchedulerCandidateQuery(context)
            .GetCandidatesAsync(NowUtc, Options());

        var candidate = Assert.Single(candidates);
        Assert.Equal(certificate.Id, candidate.CertificateId);
        Assert.Equal("expired_recovery", candidate.ExpirySeverity);
    }

    [Fact]
    public void SchedulerRegistration_IsSingleHangfireJobAndUsesExistingInitializer()
    {
        var hangfire = CertificationSource.ReadRepoFile("PointOfSale.API/Jobs/HangfireInitializationHostedService.cs");
        var job = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/CertificateRenewalSchedulerJob.cs");
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/CertificateRenewalService.cs");
        var telemetry = CertificationSource.ReadRepoFile("PointOfSale.API/Instrumentation/CertificateRenewalTelemetry.cs");

        Assert.Contains("CertificateRenewalSchedulerJob.RecurringJobId", hangfire, StringComparison.Ordinal);
        Assert.Contains("RecurringJob.AddOrUpdate<CertificateRenewalSchedulerJob>", hangfire, StringComparison.Ordinal);
        Assert.DoesNotContain("BackgroundService", job + service, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", job + service, StringComparison.Ordinal);
        Assert.DoesNotContain("Quartz", job + service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SslCertificateOperation", service, StringComparison.Ordinal);
        Assert.Contains("ICaddyEdgeClient", service, StringComparison.Ordinal);
        Assert.Contains("IEdgeCertificateProbe", service, StringComparison.Ordinal);
        Assert.DoesNotContain("private key", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pem", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new(\"domain_id\"", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"certificate_id\"", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"operation_id\"", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_ValidateRenewalPolicyConfiguration()
    {
        Assert.True(Options().Validate(out _));
        Assert.True(Options(scanIntervalMinutes: 1).Validate(out _));
        Assert.True(Options(scanIntervalMinutes: 1440).Validate(out _));
        Assert.False(Options(scanIntervalMinutes: 0).Validate(out _));
        Assert.False(Options(scanIntervalMinutes: -1).Validate(out _));
        Assert.False(Options(scanIntervalMinutes: 90).Validate(out _));
        Assert.False(Options(scanIntervalMinutes: 1500).Validate(out _));
        Assert.False(Options(batchSize: 0).Validate(out _));
        Assert.False(Options(maxConcurrent: 0).Validate(out _));
        Assert.False(Options(jitterPercent: 90).Validate(out _));
    }

    [Fact]
    public void RenewalCronBuilder_UsesValidHourlyCronForDefaultSixtyMinuteInterval()
    {
        var cron = HangfireInitializationHostedService.BuildCertificateRenewalSchedulerCron(60);

        Assert.Equal("0 */1 * * *", cron);
        Assert.Equal(cron, HangfireInitializationHostedService.ValidateCronExpression(
            CertificateRenewalSchedulerJob.RecurringJobId,
            "CertificateRenewalScheduler:ScanIntervalMinutes",
            60,
            cron));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(90)]
    [InlineData(1500)]
    public void RenewalCronBuilder_RejectsInvalidIntervalsDeterministically(int scanIntervalMinutes)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HangfireInitializationHostedService.BuildCertificateRenewalSchedulerCron(scanIntervalMinutes));

        Assert.Contains(CertificateRenewalSchedulerJob.RecurringJobId, ex.Message, StringComparison.Ordinal);
        Assert.Contains("CertificateRenewalScheduler:ScanIntervalMinutes", ex.Message, StringComparison.Ordinal);
        Assert.Contains(scanIntervalMinutes.ToString(), ex.Message, StringComparison.Ordinal);
    }

    private static CertificateRenewalService CreateRenewalService(
        ApplicationDbContext context,
        ICertificateActivationService activation,
        IEdgeCertificateProbe probe,
        RecordingEventPublisher events,
        FakeCertificateProvider? provider = null)
    {
        var providerOptions = new FixedOptionsMonitor<CertificateProviderOptions>(ProviderOptions());
        return new CertificateRenewalService(
            context,
            provider ?? new FakeCertificateProvider(),
            new CertificateProviderAccountSelector(providerOptions),
            providerOptions,
            Microsoft.Extensions.Options.Options.Create(Options()),
            new FakeCaddyEdgeClient(),
            probe,
            activation,
            events,
            NullLogger<CertificateRenewalService>.Instance);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Domain56_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Database.EnsureCreated();
        return context;
    }

    private static void AddTenant(ApplicationDbContext context, Guid tenantId)
    {
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"Tenant {tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = NowUtc.AddDays(-1),
        });
    }

    private static Domain AddDomain(ApplicationDbContext context, Guid tenantId, string host, DomainLifecycleState lifecycleState, bool isDeleted = false)
    {
        var domain = new Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = host,
            NormalizedHostName = host,
            DomainType = DomainType.Alias,
            LifecycleState = lifecycleState,
            VerificationMethod = VerificationMethod.DnsTxt,
            VerificationToken = "token",
            RedirectType = RedirectType.None,
            CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
            CreatedAtUtc = NowUtc.AddDays(-10),
            UpdatedAtUtc = NowUtc.AddDays(-1),
            VerifiedAtUtc = NowUtc.AddDays(-10),
            IsDeleted = isDeleted,
        };
        context.Domains.Add(domain);
        return domain;
    }

    private static SslCertificate AddActiveCertificate(
        ApplicationDbContext context,
        Guid tenantId,
        Domain domain,
        DateTime? nextRenewalUtc,
        DateTime? notAfterUtc)
    {
        var certificate = new SslCertificate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DomainId = domain.Id,
            NormalizedHostName = domain.NormalizedHostName,
            Provider = ProviderType.LetsEncrypt,
            ProviderImplementation = "test",
            ProviderAccountReference = "le-staging-account",
            ProviderEnvironment = CertificateProviderEnvironment.Staging.ToString(),
            Status = CertificateStatus.Active,
            OperationPhase = CertificateOperationPhase.ActivationProbe,
            Generation = 1,
            Issuer = "Fake CA",
            Subject = $"CN={domain.NormalizedHostName}",
            SubjectAlternativeNamesJson = JsonSerializer.Serialize(new[] { domain.NormalizedHostName }),
            Thumbprint = "OLD_THUMBPRINT",
            Fingerprint = "OLD_THUMBPRINT",
            SerialNumber = "OLD_SERIAL",
            SignatureAlgorithm = "sha256RSA",
            NotBeforeUtc = NowUtc.AddDays(-30),
            NotAfterUtc = notAfterUtc,
            NextRenewalUtc = nextRenewalUtc,
            RenewalEligible = true,
            ActivatedAtUtc = NowUtc.AddDays(-30),
            CreatedAt = NowUtc.AddDays(-30),
            UpdatedAt = NowUtc.AddDays(-1),
            IsDeleted = false,
        };
        context.SslCertificates.Add(certificate);
        domain.SslCertificateId = certificate.Id;
        return certificate;
    }

    private static CertificateRenewalSchedulerCandidate ToCandidate(SslCertificate certificate, Domain domain) =>
        new(
            certificate.TenantId,
            domain.Id,
            certificate.Id,
            certificate.Provider,
            certificate.Generation,
            domain.NormalizedHostName,
            certificate.NextRenewalUtc,
            certificate.NotAfterUtc,
            "warning");

    private static CertificateRenewalSchedulerOptions Options(
        int scanIntervalMinutes = 60,
        int batchSize = 50,
        int maxConcurrent = 4,
        int jitterPercent = 0) =>
        new()
        {
            Enabled = true,
            ScanIntervalMinutes = scanIntervalMinutes,
            BatchSize = batchSize,
            MaxConcurrentRenewals = maxConcurrent,
            RenewalThresholdDays = 30,
            ExpiryWarningThresholdDays = 14,
            ExpiryUrgentThresholdDays = 7,
            MaxAttempts = 5,
            RetryBackoffSeconds = 30,
            MaxRetryDelayMinutes = 60,
            ProviderRateLimitWindowMinutes = 60,
            ProviderAttemptBudget = 5,
            StaleOperationTimeoutMinutes = 30,
            JitterPercent = jitterPercent,
        };

    private static CertificateProviderOptions ProviderOptions() => new()
    {
        Enabled = true,
        Provider = ProviderType.LetsEncrypt,
        Environment = CertificateProviderEnvironment.Staging,
        AccountReference = "le-staging-account",
        ConfiguredIssuer = "lets-encrypt",
        ContactEmail = "ops@example.com",
        DirectoryUrl = new Uri("https://acme-staging-v02.api.letsencrypt.org/directory"),
    };

    private sealed class FakeCaddyEdgeClient : ICaddyEdgeClient
    {
        public Task<CaddyDomainRouteResult> EnsureDomainRouteAsync(CaddyDomainRouteRequest request, CancellationToken cancellationToken = default)
        {
            var state = new CaddyDomainRouteState(true, true, request.NormalizedHostName, "fake-edge", "127.0.0.1:5010", null, null);
            return Task.FromResult(CaddyDomainRouteResult.Success(state, mutated: false, edgeIdentifier: "fake-edge"));
        }

        public Task<CaddyDomainRouteState> GetDomainRouteStateAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaddyDomainRouteState(true, true, query.NormalizedHostName, "fake-edge", "127.0.0.1:5010", null, null));

        public Task<CaddyDomainRouteResult> RemoveDomainRouteAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaddyDomainRouteResult.Success(CaddyDomainRouteState.Missing(query.NormalizedHostName), true, "fake-edge"));
    }

    private sealed class RenewalProbe(string replacementThumbprint, string replacementSerial, DateTime replacementNotAfterUtc) : IEdgeCertificateProbe
    {
        public Task<EdgeCertificateProbeResult> ProbeAsync(EdgeCertificateProbeRequest request, CancellationToken cancellationToken = default)
        {
            var thumbprint = request.ExpectedThumbprint ?? replacementThumbprint;
            var serial = request.ExpectedSerialNumber ?? replacementSerial;
            var notAfter = request.ExpectedNotAfterUtc ?? replacementNotAfterUtc;
            return Task.FromResult(new EdgeCertificateProbeResult(
                true,
                false,
                request.NormalizedHostName,
                null,
                null,
                new EdgeCertificateIdentity(
                    $"CN={request.NormalizedHostName}",
                    request.ExpectedIssuer ?? "Fake CA",
                    Array.AsReadOnly(new[] { request.NormalizedHostName }),
                    thumbprint,
                    thumbprint,
                    serial,
                    "sha256RSA",
                    NowUtc.AddDays(-1),
                    notAfter)));
        }
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> PublishedEvents { get; } = new();
        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IntegrationEvent
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default) where T : IntegrationEvent
        {
            PublishedEvents.AddRange(events.Cast<object>());
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public FixedOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
