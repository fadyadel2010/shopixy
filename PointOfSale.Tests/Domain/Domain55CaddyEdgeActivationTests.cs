using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CustomDomains;

public sealed class Domain55CaddyEdgeActivationTests
{
    [Fact]
    public async Task Activation_CaddyAdmissionAndSniSuccess_MarksCertificateAndDomainActive()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddVerifiedDomain(context, tenantId, "secure.example.com");
        var certificate = AddIssuedCertificate(context, tenantId, domain);
        var events = new RecordingEventPublisher();
        var service = CreateService(context, events: events);

        var response = await service.ActivateAsync(
            domain.Id,
            new CertificateActivationRequest { IdempotencyKey = "activate-key", CertificateId = certificate.Id },
            tenantId,
            Guid.NewGuid(),
            "corr-activate");

        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        var reloadedCertificate = await context.SslCertificates.SingleAsync(c => c.Id == certificate.Id);

        Assert.Equal(CertificateStatus.Active, reloadedCertificate.Status);
        Assert.Equal(DomainLifecycleState.SslActive, reloadedDomain.LifecycleState);
        Assert.Equal(certificate.Id, reloadedDomain.SslCertificateId);
        Assert.Equal(reloadedDomain.CertificateOperationFencingToken, reloadedCertificate.FencingToken);
        Assert.Equal(
            reloadedDomain.CertificateOperationFencingToken,
            await context.SslCertificateOperations
                .Where(o => o.OperationType == CertificateOperationType.Activation)
                .Select(o => o.FencingToken)
                .SingleAsync());
        Assert.Equal("Activated", response.Outcome);
        Assert.Contains(events.PublishedEvents, e => e is CertificateActivatedEvent);
        Assert.Contains(events.PublishedEvents, e => e is DomainSslActivatedEvent);
    }

    [Fact]
    public async Task Activation_CaddyAdmissionSuccessButSniFailure_DoesNotMarkActive()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddVerifiedDomain(context, tenantId, "notready.example.com");
        var certificate = AddIssuedCertificate(context, tenantId, domain);
        var probe = new FakeEdgeCertificateProbe
        {
            Result = new EdgeCertificateProbeResult(
                false,
                true,
                domain.NormalizedHostName,
                "SNI_DNS_OR_CONNECT_FAILURE",
                "The hostname was not reachable through the public HTTPS edge.",
                null),
        };
        var events = new RecordingEventPublisher();
        var service = CreateService(context, probe: probe, events: events);

        var response = await service.ActivateAsync(
            domain.Id,
            new CertificateActivationRequest { IdempotencyKey = "sni-fail-key", CertificateId = certificate.Id },
            tenantId,
            Guid.NewGuid(),
            "corr-sni-fail");

        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        var reloadedCertificate = await context.SslCertificates.SingleAsync(c => c.Id == certificate.Id);

        Assert.NotEqual(CertificateStatus.Active, reloadedCertificate.Status);
        Assert.Equal(DomainLifecycleState.Verified, reloadedDomain.LifecycleState);
        Assert.Null(reloadedDomain.SslCertificateId);
        Assert.Equal("SniProbeFailed", response.Outcome);
        Assert.DoesNotContain(events.PublishedEvents, e => e is CertificateActivatedEvent);
        Assert.DoesNotContain(events.PublishedEvents, e => e is DomainSslActivatedEvent);
    }

    [Fact]
    public async Task Activation_CaddyFailure_DoesNotRunSniProbeOrMarkActive()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddVerifiedDomain(context, tenantId, "edgefail.example.com");
        var certificate = AddIssuedCertificate(context, tenantId, domain);
        var edge = new FakeCaddyEdgeClient
        {
            EnsureResult = CaddyDomainRouteResult.Failure(
                CaddyDomainRouteState.Missing(domain.NormalizedHostName),
                "CADDY_UNAVAILABLE",
                "Caddy is unavailable.",
                retryable: true),
        };
        var probe = new FakeEdgeCertificateProbe();
        var service = CreateService(context, edge, probe);

        var response = await service.ActivateAsync(
            domain.Id,
            new CertificateActivationRequest { IdempotencyKey = "edge-fail-key", CertificateId = certificate.Id },
            tenantId,
            Guid.NewGuid(),
            "corr-edge-fail");

        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        Assert.Equal("EdgeAdmissionFailed", response.Outcome);
        Assert.Equal(0, probe.CallCount);
        Assert.Null(reloadedDomain.SslCertificateId);
    }

    [Fact]
    public async Task Activation_ReplaysSameIdempotencyKeyWithoutDuplicateRouteOrProbe()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddVerifiedDomain(context, tenantId, "repeat.example.com");
        var certificate = AddIssuedCertificate(context, tenantId, domain);
        var edge = new FakeCaddyEdgeClient();
        var probe = new FakeEdgeCertificateProbe();
        var service = CreateService(context, edge, probe);

        var first = await service.ActivateAsync(
            domain.Id,
            new CertificateActivationRequest { IdempotencyKey = "same-activation-key", CertificateId = certificate.Id },
            tenantId,
            Guid.NewGuid(),
            "corr-first");
        var second = await service.ActivateAsync(
            domain.Id,
            new CertificateActivationRequest { IdempotencyKey = "same-activation-key", CertificateId = certificate.Id },
            tenantId,
            Guid.NewGuid(),
            "corr-second");

        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(1, edge.EnsureCallCount);
        Assert.Equal(1, probe.CallCount);
        Assert.Equal(1, await context.SslCertificateOperations.CountAsync(o => o.OperationType == CertificateOperationType.Activation));
    }

    [Fact]
    public async Task Activation_RejectsWrongTenantUnverifiedDeletedAndPendingCertificate()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var service = CreateService(context);

        var unverified = AddVerifiedDomain(context, tenantId, "unverified-edge.example.com", DomainLifecycleState.PendingVerification);
        AddIssuedCertificate(context, tenantId, unverified);
        await Assert.ThrowsAsync<DomainOperationException>(() => ActivateAsync(service, unverified.Id, tenantId));

        var deleted = AddVerifiedDomain(context, tenantId, "deleted-edge.example.com", DomainLifecycleState.Deleted, isDeleted: true);
        AddIssuedCertificate(context, tenantId, deleted);
        await Assert.ThrowsAsync<DomainOperationException>(() => ActivateAsync(service, deleted.Id, tenantId));

        var pending = AddVerifiedDomain(context, tenantId, "pending-cert.example.com");
        AddIssuedCertificate(context, tenantId, pending, CertificateStatus.Pending);
        await Assert.ThrowsAsync<DomainOperationException>(() => ActivateAsync(service, pending.Id, tenantId));

        var otherTenantDomain = AddVerifiedDomain(context, Guid.NewGuid(), "foreign-edge.example.com");
        AddIssuedCertificate(context, otherTenantDomain.TenantId, otherTenantDomain);
        await Assert.ThrowsAsync<DomainNotFoundException>(() => ActivateAsync(service, otherTenantDomain.Id, tenantId));
    }

    [Fact]
    public void CaddyEdgeOptions_RejectsArbitraryUrlUpstream()
    {
        Assert.False(CaddyEdgeOptionsValidator.IsSafeTrustedUpstream("http://127.0.0.1:5010"));
        Assert.False(CaddyEdgeOptionsValidator.IsSafeTrustedUpstream("file:///tmp/socket"));
        Assert.False(CaddyEdgeOptionsValidator.IsSafeTrustedUpstream("169.254.169.254/latest"));
        Assert.False(CaddyEdgeOptionsValidator.IsSafeTrustedUpstream("127.0.0.1:5010"));
        Assert.True(CaddyEdgeOptionsValidator.IsSafeTrustedUpstream("127.0.0.1:5010", true));
        Assert.True(CaddyEdgeOptionsValidator.IsSafeTrustedUpstream("storefront-edge.internal:5010"));
    }

    [Fact]
    public async Task CaddyRouteRemoval_WhenEdgeDisabled_FailsClosedBeforeFilesystemAccess()
    {
        var client = new CaddyfileEdgeClient(
            new FixedOptionsMonitor<CaddyEdgeOptions>(new CaddyEdgeOptions
            {
                Enabled = false,
                ManagedRoutesDirectory = string.Empty,
            }),
            NullLogger<CaddyfileEdgeClient>.Instance);

        var result = await client.RemoveDomainRouteAsync(new CaddyDomainRouteQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "disabled-edge.example.com",
            "corr-disabled-edge"));

        Assert.False(result.Succeeded);
        Assert.False(result.Mutated);
        Assert.Equal("CADDY_EDGE_DISABLED", result.FailureCode);
        Assert.False(File.Exists("disabled-edge.example.com.caddy"));
    }

    [Fact]
    public async Task CaddyRoute_HigherFenceRejectsStaleOverwriteAndRemoval()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shopixy-caddy-fence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = new CaddyEdgeOptions
            {
                Enabled = true,
                ManagedRoutesDirectory = Path.Combine(root, "routes"),
                StorefrontRoot = Path.Combine(root, "storefront"),
                LogRoot = Path.Combine(root, "logs"),
                ApiUpstream = "storefront-edge.internal:5010",
                ValidateCommand = "exit 0",
                ReloadCommand = "exit 0",
            };
            var client = new CaddyfileEdgeClient(
                new FixedOptionsMonitor<CaddyEdgeOptions>(options),
                NullLogger<CaddyfileEdgeClient>.Instance);
            var tenantId = Guid.NewGuid();
            var domainId = Guid.NewGuid();
            var certificateId = Guid.NewGuid();
            const string host = "fenced.example.com";

            var accepted = await client.EnsureDomainRouteAsync(new CaddyDomainRouteRequest(
                tenantId, domainId, certificateId, Guid.NewGuid(), host, "corr-new", 9));
            var routePath = Path.Combine(options.ManagedRoutesDirectory, $"{host}.caddy");
            var acceptedContent = await File.ReadAllTextAsync(routePath);

            var staleWrite = await client.EnsureDomainRouteAsync(new CaddyDomainRouteRequest(
                tenantId, domainId, certificateId, Guid.NewGuid(), host, "corr-stale-write", 8));
            var staleRemoval = await client.RemoveDomainRouteAsync(new CaddyDomainRouteQuery(
                tenantId, domainId, host, "corr-stale-remove", 8, certificateId, Guid.NewGuid()));

            Assert.True(accepted.Succeeded);
            Assert.False(staleWrite.Succeeded);
            Assert.Equal("CADDY_STALE_FENCE", staleWrite.FailureCode);
            Assert.False(staleRemoval.Succeeded);
            Assert.Equal("CADDY_STALE_FENCE", staleRemoval.FailureCode);
            Assert.Equal(acceptedContent, await File.ReadAllTextAsync(routePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("storefront.internal:5010\nrespond hacked")]
    [InlineData("storefront.internal:5010 { respond hacked }")]
    [InlineData("storefront_internal:5010")]
    public void CaddyEdgeOptions_RejectsConfigInjectionUpstream(string upstream)
    {
        Assert.False(CaddyEdgeOptionsValidator.IsSafeTrustedUpstream(upstream));
    }

    private static Task<CertificateActivationResponse> ActivateAsync(
        CertificateActivationService service,
        Guid domainId,
        Guid tenantId) =>
        service.ActivateAsync(
            domainId,
            new CertificateActivationRequest { IdempotencyKey = $"key-{Guid.NewGuid():N}" },
            tenantId,
            Guid.NewGuid(),
            "corr-test");

    private static CertificateActivationService CreateService(
        ApplicationDbContext context,
        FakeCaddyEdgeClient? edge = null,
        FakeEdgeCertificateProbe? probe = null,
        RecordingEventPublisher? events = null) =>
        new(
            context,
            edge ?? new FakeCaddyEdgeClient(),
            probe ?? new FakeEdgeCertificateProbe(),
            events ?? new RecordingEventPublisher(),
            NullLogger<CertificateActivationService>.Instance);

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Domain55_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Database.EnsureCreated();
        return context;
    }

    private static Domain AddVerifiedDomain(
        ApplicationDbContext context,
        Guid tenantId,
        string host,
        DomainLifecycleState lifecycleState = DomainLifecycleState.Verified,
        bool isDeleted = false)
    {
        var domain = new Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = host,
            NormalizedHostName = host.ToLowerInvariant(),
            DomainType = DomainType.Alias,
            LifecycleState = lifecycleState,
            VerificationToken = "domain-token",
            VerificationMethod = VerificationMethod.DnsTxt,
            RedirectType = RedirectType.None,
            CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            VerifiedAtUtc = lifecycleState == DomainLifecycleState.Verified ? DateTime.UtcNow : null,
            IsDeleted = isDeleted,
        };

        context.Domains.Add(domain);
        context.SaveChanges();
        return domain;
    }

    private static SslCertificate AddIssuedCertificate(
        ApplicationDbContext context,
        Guid tenantId,
        Domain domain,
        CertificateStatus status = CertificateStatus.Issued)
    {
        var certificate = new SslCertificate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DomainId = domain.Id,
            NormalizedHostName = domain.NormalizedHostName,
            Provider = ProviderType.LetsEncrypt,
            Status = status,
            OperationPhase = CertificateOperationPhase.MetadataSync,
            Generation = 1,
            Issuer = "Fake CA",
            Subject = $"CN={domain.NormalizedHostName}",
            SubjectAlternativeNamesJson = JsonSerializer.Serialize(new[] { domain.NormalizedHostName }),
            Thumbprint = "FAKE_THUMBPRINT",
            Fingerprint = "FAKE_FINGERPRINT",
            SerialNumber = "FAKE_SERIAL",
            SignatureAlgorithm = "sha256RSA",
            NotBeforeUtc = DateTime.UtcNow.AddDays(-1),
            NotAfterUtc = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };

        context.SslCertificates.Add(certificate);
        context.SaveChanges();
        return certificate;
    }

    private sealed class FakeCaddyEdgeClient : ICaddyEdgeClient
    {
        public int EnsureCallCount { get; private set; }
        public CaddyDomainRouteResult? EnsureResult { get; set; }

        public Task<CaddyDomainRouteResult> EnsureDomainRouteAsync(CaddyDomainRouteRequest request, CancellationToken cancellationToken = default)
        {
            EnsureCallCount++;
            var state = new CaddyDomainRouteState(true, true, request.NormalizedHostName, "fake-edge", "127.0.0.1:5010", null, null);
            return Task.FromResult(EnsureResult ?? CaddyDomainRouteResult.Success(state, mutated: EnsureCallCount == 1, edgeIdentifier: "fake-edge"));
        }

        public Task<CaddyDomainRouteState> GetDomainRouteStateAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaddyDomainRouteState(true, true, query.NormalizedHostName, "fake-edge", "127.0.0.1:5010", null, null));

        public Task<CaddyDomainRouteResult> RemoveDomainRouteAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaddyDomainRouteResult.Success(CaddyDomainRouteState.Missing(query.NormalizedHostName), true, "fake-edge"));
    }

    private sealed class FakeEdgeCertificateProbe : IEdgeCertificateProbe
    {
        public int CallCount { get; private set; }
        public EdgeCertificateProbeResult? Result { get; set; }

        public Task<EdgeCertificateProbeResult> ProbeAsync(EdgeCertificateProbeRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result ?? new EdgeCertificateProbeResult(
                true,
                false,
                request.NormalizedHostName,
                null,
                null,
                new EdgeCertificateIdentity(
                    $"CN={request.NormalizedHostName}",
                    request.ExpectedIssuer ?? "Fake CA",
                    Array.AsReadOnly(new[] { request.NormalizedHostName }),
                    request.ExpectedThumbprint ?? "FAKE_THUMBPRINT",
                    request.ExpectedFingerprint ?? "FAKE_FINGERPRINT",
                    request.ExpectedSerialNumber ?? "FAKE_SERIAL",
                    "sha256RSA",
                    DateTime.UtcNow.AddDays(-1),
                    request.ExpectedNotAfterUtc ?? DateTime.UtcNow.AddDays(30))));
        }
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> PublishedEvents { get; } = new();

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            PublishedEvents.AddRange(events.Cast<object>());
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
