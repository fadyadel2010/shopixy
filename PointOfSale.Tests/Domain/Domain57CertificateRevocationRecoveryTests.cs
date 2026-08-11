using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Fakes;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CustomDomains;

public sealed class Domain57CertificateRevocationRecoveryTests
{
    [Fact]
    public async Task Revocation_ReplaysSameIdempotencyKeyWithoutDuplicateProviderCall()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "old.example.com", DomainLifecycleState.Verified);
        var certificate = AddCertificate(context, tenantId, domain, CertificateStatus.Issued, authoritative: false);
        await context.SaveChangesAsync();
        var provider = new FakeCertificateProvider();
        var events = new RecordingEventPublisher();
        var service = CreateService(context, provider, new StaticRenewalService(false), events);

        var request = new CertificateRevocationRequest(
            certificate.Id,
            CertificateRevocationReasonCodes.AdminRequested,
            "same-revoke-key");

        var first = await service.RevokeAsync(domain.Id, request, tenantId, Guid.NewGuid(), "corr-1");
        var second = await service.RevokeAsync(domain.Id, request, tenantId, Guid.NewGuid(), "corr-2");

        var reloaded = await context.SslCertificates.IgnoreQueryFilters().SingleAsync(c => c.Id == certificate.Id);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(1, provider.RevocationCallCount);
        Assert.Equal(1, await context.SslCertificateOperations.CountAsync(o => o.OperationType == CertificateOperationType.Revocation));
        Assert.Equal(CertificateStatus.Revoked, reloaded.Status);
        Assert.Contains(events.PublishedEvents, e => e is CertificateRevokedEvent);
    }

    [Fact]
    public async Task Revocation_ForValidAuthoritativeCertificate_ReplacesBeforeRevokingOldGeneration()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "active.example.com", DomainLifecycleState.SslActive);
        var old = AddCertificate(context, tenantId, domain, CertificateStatus.Active, authoritative: true);
        await context.SaveChangesAsync();
        var provider = new FakeCertificateProvider();
        var service = CreateService(context, provider, new AuthorityMovingRenewalService(context), new RecordingEventPublisher());

        var result = await service.RevokeAsync(
            domain.Id,
            new CertificateRevocationRequest(old.Id, CertificateRevocationReasonCodes.AdminRequested, "replace-first"),
            tenantId,
            Guid.NewGuid(),
            "corr-replace");

        var certificates = await context.SslCertificates.IgnoreQueryFilters().OrderBy(c => c.Generation).ToListAsync();
        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(CertificateStatus.Revoked, certificates[0].Status);
        Assert.Equal(CertificateStatus.Active, certificates[1].Status);
        Assert.Equal(certificates[1].Id, reloadedDomain.SslCertificateId);
        Assert.Equal(1, provider.RevocationCallCount);
    }

    [Fact]
    public async Task Revocation_WhenReplacementDoesNotBecomeAuthoritative_PreservesOldAndSkipsProviderRevocation()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "preserve.example.com", DomainLifecycleState.SslActive);
        var old = AddCertificate(context, tenantId, domain, CertificateStatus.Active, authoritative: true);
        await context.SaveChangesAsync();
        var provider = new FakeCertificateProvider();
        var service = CreateService(context, provider, new StaticRenewalService(true), new RecordingEventPublisher());

        var result = await service.RevokeAsync(
            domain.Id,
            new CertificateRevocationRequest(old.Id, CertificateRevocationReasonCodes.SecurityCompromise, "unsafe-success"),
            tenantId,
            actorUserId: null,
            "corr-preserve");

        var reloadedOld = await context.SslCertificates.IgnoreQueryFilters().SingleAsync(c => c.Id == old.Id);
        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);

        Assert.False(result.Succeeded);
        Assert.Equal("REPLACEMENT_NOT_AUTHORITATIVE", result.FailureCode);
        Assert.Equal(CertificateStatus.Active, reloadedOld.Status);
        Assert.Equal(old.Id, reloadedDomain.SslCertificateId);
        Assert.Equal(0, provider.RevocationCallCount);
    }

    [Fact]
    public async Task ImmediateRevocation_OfPrimaryAuthority_RemovesPrimaryAndClearsCertificateLink()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        var domain = AddDomain(context, tenantId, "primary-compromised.example.com", DomainLifecycleState.Primary);
        domain.DomainType = DomainType.Primary;
        var certificate = AddCertificate(context, tenantId, domain, CertificateStatus.Active, authoritative: true);
        await context.SaveChangesAsync();
        var events = new RecordingEventPublisher();
        var service = CreateService(context, new FakeCertificateProvider(), new StaticRenewalService(false), events);

        var result = await service.RevokeAsync(
            domain.Id,
            new CertificateRevocationRequest(
                certificate.Id,
                CertificateRevocationReasonCodes.SecurityCompromise,
                "immediate-primary-revocation",
                ImmediateRevocation: true),
            tenantId,
            actorUserId: null,
            "corr-primary-revocation");

        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(DomainLifecycleState.Verified, reloadedDomain.LifecycleState);
        Assert.Equal(DomainType.Alias, reloadedDomain.DomainType);
        Assert.Null(reloadedDomain.SslCertificateId);
        Assert.Contains(events.PublishedEvents, e => e is CertificateRevokedEvent);
    }

    [Fact]
    public async Task Revocation_RejectsWrongTenantCertificateCorrelation()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        AddTenant(context, tenantId);
        AddTenant(context, otherTenantId);
        var domain = AddDomain(context, otherTenantId, "foreign.example.com", DomainLifecycleState.Verified);
        var certificate = AddCertificate(context, otherTenantId, domain, CertificateStatus.Issued, authoritative: false);
        await context.SaveChangesAsync();
        var service = CreateService(context, new FakeCertificateProvider(), new StaticRenewalService(false), new RecordingEventPublisher());

        await Assert.ThrowsAsync<DomainNotFoundException>(() => service.RevokeAsync(
            domain.Id,
            new CertificateRevocationRequest(certificate.Id, CertificateRevocationReasonCodes.AdminRequested, "wrong-tenant"),
            tenantId,
            Guid.NewGuid(),
            "corr-wrong-tenant"));
    }

    private static CertificateRevocationService CreateService(
        ApplicationDbContext context,
        FakeCertificateProvider provider,
        ICertificateRenewalService renewalService,
        RecordingEventPublisher events)
    {
        var providerOptions = new FixedOptionsMonitor<CertificateProviderOptions>(ProviderOptions());
        return new CertificateRevocationService(
            context,
            provider,
            new CertificateProviderAccountSelector(providerOptions),
            providerOptions,
            Options.Create(RenewalOptions()),
            renewalService,
            new SuccessfulEdgeClient(),
            events,
            new CertificateRenewalTelemetry(),
            NullLogger<CertificateRevocationService>.Instance);
    }

    private sealed class SuccessfulEdgeClient : ICaddyEdgeClient
    {
        public Task<CaddyDomainRouteResult> EnsureDomainRouteAsync(CaddyDomainRouteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaddyDomainRouteResult.Success(
                new CaddyDomainRouteState(true, true, request.NormalizedHostName, "test-edge", "api:8080", null, null),
                false,
                "test-edge"));

        public Task<CaddyDomainRouteState> GetDomainRouteStateAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaddyDomainRouteState(true, true, query.NormalizedHostName, "test-edge", "api:8080", null, null));

        public Task<CaddyDomainRouteResult> RemoveDomainRouteAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaddyDomainRouteResult.Success(CaddyDomainRouteState.Missing(query.NormalizedHostName), true, null));
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Domain57_{Guid.NewGuid():N}")
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
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
    }

    private static Domain AddDomain(ApplicationDbContext context, Guid tenantId, string host, DomainLifecycleState lifecycleState)
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
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
            VerifiedAtUtc = DateTime.UtcNow.AddDays(-10),
        };
        context.Domains.Add(domain);
        return domain;
    }

    private static SslCertificate AddCertificate(
        ApplicationDbContext context,
        Guid tenantId,
        Domain domain,
        CertificateStatus status,
        bool authoritative)
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
            Status = status,
            OperationPhase = CertificateOperationPhase.ActivationProbe,
            Generation = 1,
            Issuer = "Fake CA",
            Subject = $"CN={domain.NormalizedHostName}",
            SubjectAlternativeNamesJson = JsonSerializer.Serialize(new[] { domain.NormalizedHostName }),
            Thumbprint = "OLD_THUMBPRINT",
            Fingerprint = "OLD_THUMBPRINT",
            SerialNumber = "OLD_SERIAL",
            SignatureAlgorithm = "sha256RSA",
            NotBeforeUtc = DateTime.UtcNow.AddDays(-30),
            NotAfterUtc = DateTime.UtcNow.AddDays(30),
            NextRenewalUtc = DateTime.UtcNow.AddDays(20),
            RenewalEligible = status == CertificateStatus.Active,
            ActivatedAtUtc = status == CertificateStatus.Active ? DateTime.UtcNow.AddDays(-30) : null,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            IsDeleted = false,
        };
        context.SslCertificates.Add(certificate);
        if (authoritative)
        {
            domain.SslCertificateId = certificate.Id;
        }
        return certificate;
    }

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

    private static CertificateRenewalSchedulerOptions RenewalOptions() => new()
    {
        Enabled = true,
        MaxAttempts = 5,
        RetryBackoffSeconds = 30,
        MaxRetryDelayMinutes = 60,
        StaleOperationTimeoutMinutes = 30,
    };

    private sealed class StaticRenewalService(bool succeeds) : ICertificateRenewalService
    {
        public Task<CertificateRenewalServiceResult> RenewAsync(
            CertificateRenewalSchedulerCandidate candidate,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CertificateRenewalServiceResult(
                candidate.TenantId,
                candidate.DomainId,
                candidate.CertificateId,
                ReplacementCertificateId: succeeds ? Guid.NewGuid() : null,
                OperationId: Guid.NewGuid(),
                candidate.ProviderType,
                candidate.Generation + 1,
                CertificateOperationPhase.RenewalWindow,
                succeeds ? "Renewed" : "ProviderFailure",
                succeeds,
                Retryable: true,
                FailureCode: succeeds ? null : "FAKE_RENEWAL_FAILURE",
                RetryAfterUtc: succeeds ? null : DateTime.UtcNow.AddMinutes(5)));
    }

    private sealed class AuthorityMovingRenewalService(ApplicationDbContext context) : ICertificateRenewalService
    {
        public async Task<CertificateRenewalServiceResult> RenewAsync(
            CertificateRenewalSchedulerCandidate candidate,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            var domain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == candidate.DomainId, cancellationToken);
            var old = await context.SslCertificates.IgnoreQueryFilters().SingleAsync(c => c.Id == candidate.CertificateId, cancellationToken);
            var replacement = new SslCertificate
            {
                Id = Guid.NewGuid(),
                TenantId = old.TenantId,
                DomainId = old.DomainId,
                NormalizedHostName = old.NormalizedHostName,
                Provider = old.Provider,
                ProviderImplementation = "test-renewal",
                ProviderAccountReference = old.ProviderAccountReference,
                ProviderEnvironment = old.ProviderEnvironment,
                Status = CertificateStatus.Active,
                OperationPhase = CertificateOperationPhase.ActivationProbe,
                Generation = old.Generation + 1,
                Issuer = old.Issuer,
                Subject = old.Subject,
                SubjectAlternativeNamesJson = old.SubjectAlternativeNamesJson,
                Thumbprint = "NEW_THUMBPRINT",
                Fingerprint = "NEW_THUMBPRINT",
                SerialNumber = "NEW_SERIAL",
                SignatureAlgorithm = old.SignatureAlgorithm,
                NotBeforeUtc = DateTime.UtcNow.AddDays(-1),
                NotAfterUtc = DateTime.UtcNow.AddDays(90),
                RenewalEligible = true,
                ActivatedAtUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            old.Status = CertificateStatus.Renewing;
            old.ReplacementCertificateId = replacement.Id;
            old.RenewalEligible = false;
            domain.SslCertificateId = replacement.Id;
            context.SslCertificates.Add(replacement);
            await context.SaveChangesAsync(cancellationToken);

            return new CertificateRenewalServiceResult(
                candidate.TenantId,
                candidate.DomainId,
                candidate.CertificateId,
                replacement.Id,
                Guid.NewGuid(),
                candidate.ProviderType,
                replacement.Generation,
                CertificateOperationPhase.ActivationProbe,
                "Renewed",
                true,
                false,
                null,
                null);
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

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
