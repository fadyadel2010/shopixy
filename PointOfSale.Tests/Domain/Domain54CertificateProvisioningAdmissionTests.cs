using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using PointOfSale.Tests.Architecture;
using PointOfSale.Tests.Fakes;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CustomDomains;

public sealed class Domain54CertificateProvisioningAdmissionTests
{
    [Fact]
    public async Task Provisioning_RejectsUnverifiedDeletedReservedUnsupportedAndWrongTenantDomains()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var service = CreateService(context);

        var unverified = AddDomain(context, tenantId, "unverified.example.com", DomainLifecycleState.PendingVerification);
        await Assert.ThrowsAsync<DomainOperationException>(() => ProvisionAsync(service, unverified.Id, tenantId));

        var deleted = AddDomain(context, tenantId, "deleted.example.com", DomainLifecycleState.Deleted, isDeleted: true);
        await Assert.ThrowsAsync<DomainOperationException>(() => ProvisionAsync(service, deleted.Id, tenantId));

        var reserved = AddDomain(context, tenantId, "admin", DomainLifecycleState.Verified);
        await Assert.ThrowsAsync<DomainOperationException>(() => ProvisionAsync(service, reserved.Id, tenantId));

        var unsupported = AddDomain(context, tenantId, "preview.example.com", DomainLifecycleState.Verified, DomainType.Preview);
        await Assert.ThrowsAsync<DomainOperationException>(() => ProvisionAsync(service, unsupported.Id, tenantId));

        var otherTenantDomain = AddDomain(context, Guid.NewGuid(), "other.example.com", DomainLifecycleState.Verified);
        await Assert.ThrowsAsync<DomainNotFoundException>(() => ProvisionAsync(service, otherTenantDomain.Id, tenantId));

        Assert.Empty(context.SslCertificateOperations);
        Assert.Equal(0, service.Provider.CreateOrderCallCount);
    }

    [Fact]
    public async Task Provisioning_RejectsInvalidProviderConfigurationBeforeProviderCall()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, "configured.example.com", DomainLifecycleState.Verified);
        var service = CreateService(context, options: ProviderOptions(enabled: false));

        await Assert.ThrowsAsync<DomainOperationException>(() => ProvisionAsync(service, domain.Id, tenantId));

        Assert.Empty(context.SslCertificateOperations);
        Assert.Equal(0, service.Provider.EnsureAccountCallCount);
        Assert.Equal(0, service.Provider.CreateOrderCallCount);
    }

    [Fact]
    public async Task Provisioning_PersistsIssuedMetadataWithoutActiveDomainTransitionOrSecrets()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, "issued.example.com", DomainLifecycleState.Verified);
        var events = new RecordingEventPublisher();
        var service = CreateService(context, eventPublisher: events);

        var response = await ProvisionAsync(service, domain.Id, tenantId, "issue-key");

        var certificate = await context.SslCertificates.SingleAsync();
        var operation = await context.SslCertificateOperations.SingleAsync();
        var reloadedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);

        Assert.Equal(CertificateStatus.Issued, certificate.Status);
        Assert.Equal(CertificateOperationStatus.Succeeded, operation.Status);
        Assert.Equal(CertificateOperationPhase.MetadataSync, operation.Phase);
        Assert.Equal(1, certificate.Generation);
        Assert.Equal("Fake CA", certificate.Issuer);
        Assert.Equal("FAKE_THUMBPRINT", certificate.Thumbprint);
        Assert.Null(reloadedDomain.SslCertificateId);
        Assert.Equal(DomainLifecycleState.Verified, reloadedDomain.LifecycleState);
        Assert.Equal("PendingEdgeActivation", response.NextAction);
        Assert.DoesNotContain("private", SerializedCertificatePersistence(context), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transient-token", SerializedCertificatePersistence(context), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events.PublishedEvents, e => e is CertificateIssuedEvent);
        Assert.Contains(events.PublishedEvents, e => e is CertificatePendingActivationEvent);
        Assert.DoesNotContain(events.PublishedEvents, e => e.GetType().Name.Contains("Active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Provisioning_ReplaysSameIdempotencyKeyAndDoesNotCreateDuplicateProviderOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, "idempotent.example.com", DomainLifecycleState.Verified);
        var service = CreateService(context);

        var first = await ProvisionAsync(service, domain.Id, tenantId, "same-key");
        var second = await ProvisionAsync(service, domain.Id, tenantId, "same-key");

        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(first.CertificateId, second.CertificateId);
        Assert.Equal(1, await context.SslCertificateOperations.CountAsync());
        Assert.Equal(1, await context.SslCertificates.CountAsync());
        Assert.Equal(1, service.Provider.CreateOrderCallCount);
    }

    [Fact]
    public async Task Provisioning_SupersededIdempotencyKeyIsTerminalAndCannotBeReacquired()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, "superseded.example.com", DomainLifecycleState.Verified);
        var service = CreateService(context);

        var first = await ProvisionAsync(service, domain.Id, tenantId, "superseded-key");
        var operation = await context.SslCertificateOperations.SingleAsync();
        operation.Status = CertificateOperationStatus.Superseded;
        operation.CompletedAtUtc = DateTime.UtcNow;
        operation.LeaseExpiresAtUtc = null;
        await context.SaveChangesAsync();

        var replay = await ProvisionAsync(service, domain.Id, tenantId, "superseded-key");

        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal("Superseded", replay.Outcome);
        Assert.Equal(CertificateOperationStatus.Superseded, operation.Status);
        Assert.Equal(1, service.Provider.CreateOrderCallCount);
    }

    [Fact]
    public async Task Provisioning_ConcurrentSameIdempotencyKeyCreatesOneOperationAndOneProviderOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, "concurrent.example.com", DomainLifecycleState.Verified);
        var service = CreateService(context, provider: new FakeCertificateProvider
        {
            Scenario = FakeCertificateProviderScenario.SuccessfulIssuanceMetadata,
            ArtificialDelay = TimeSpan.FromMilliseconds(25),
        });

        var requests = Enumerable.Range(0, 5)
            .Select(_ => ProvisionAsync(service, domain.Id, tenantId, "concurrent-key"))
            .ToArray();

        var responses = await Task.WhenAll(requests);

        Assert.Single(responses.Select(r => r.OperationId).Distinct());
        Assert.Equal(1, await context.SslCertificateOperations.CountAsync());
        Assert.Equal(1, await context.SslCertificates.CountAsync());
        Assert.Equal(1, service.Provider.CreateOrderCallCount);
    }

    [Fact]
    public async Task Provisioning_ReplacementGenerationAdvancesWhenPriorCertificateExists()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, "replace.example.com", DomainLifecycleState.Verified);
        context.SslCertificates.Add(new SslCertificate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DomainId = domain.Id,
            NormalizedHostName = domain.NormalizedHostName,
            Provider = ProviderType.LetsEncrypt,
            Status = CertificateStatus.Failed,
            OperationPhase = CertificateOperationPhase.RetryBackoff,
            Generation = 1,
            FailureCode = "OLD_FAILURE",
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
        });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var response = await ProvisionAsync(service, domain.Id, tenantId, "replacement-key");

        Assert.Equal(2, response.Generation);
        Assert.Equal(2, await context.SslCertificates.CountAsync());
        Assert.Contains(await context.SslCertificates.ToListAsync(), c => c.Generation == 1 && c.Status == CertificateStatus.Failed);
    }

    [Fact]
    public async Task Provisioning_NormalizesProviderFailuresAndPersistsRetryability()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, "failure.example.com", DomainLifecycleState.Verified);
        var service = CreateService(context, provider: new FakeCertificateProvider { Scenario = FakeCertificateProviderScenario.RateLimited });

        var response = await ProvisionAsync(service, domain.Id, tenantId, "rate-key");

        var certificate = await context.SslCertificates.SingleAsync();
        var operation = await context.SslCertificateOperations.SingleAsync();
        Assert.Equal(CertificateStatus.Failed, certificate.Status);
        Assert.Equal(CertificateOperationStatus.Failed, operation.Status);
        Assert.Equal("FAKE_RATE_LIMITED", operation.FailureCode);
        Assert.True(operation.Retryable);
        Assert.True(response.Retryable);
    }

    [Fact]
    public void ProvisioningArchitecture_UsesProviderAbstractionAndKeepsForbiddenBoundariesOut()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Storefront/CertificateProvisioningService.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AdminDomainsController.cs");

        Assert.Contains("ICertificateProvider", service);
        Assert.Contains("CertificateIssuanceRequest", service);
        Assert.Contains("CertificateChallengePolicy.Default", service);
        Assert.DoesNotContain("Certes", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ACMESharp", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BouncyCastle", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Caddyfile", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dns01", service, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkSslActive", service, StringComparison.Ordinal);
        Assert.DoesNotContain("domain.SslCertificateId =", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ICertificateProvider", controller, StringComparison.Ordinal);
    }

    private static Task<CertificateProvisioningResponse> ProvisionAsync(
        TestProvisioningService service,
        Guid domainId,
        Guid tenantId,
        string idempotencyKey = "provision-key") =>
        service.Service.ProvisionAsync(
            domainId,
            new CertificateProvisioningRequest { IdempotencyKey = idempotencyKey },
            tenantId,
            actorUserId: Guid.NewGuid(),
            correlationId: $"corr-{idempotencyKey}");

    private static TestProvisioningService CreateService(
        ApplicationDbContext context,
        FakeCertificateProvider? provider = null,
        CertificateProviderOptions? options = null,
        RecordingEventPublisher? eventPublisher = null)
    {
        var providerOptions = new FixedOptionsMonitor<CertificateProviderOptions>(options ?? ProviderOptions(enabled: true));
        var certificateProvider = provider ?? new FakeCertificateProvider();
        var service = new CertificateProvisioningService(
            context,
            certificateProvider,
            new CertificateProviderAccountSelector(providerOptions),
            providerOptions,
            eventPublisher ?? new RecordingEventPublisher(),
            NullLogger<CertificateProvisioningService>.Instance);

        return new TestProvisioningService(service, certificateProvider);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Domain54_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Database.EnsureCreated();
        return context;
    }

    private static Domain AddDomain(
        ApplicationDbContext context,
        Guid tenantId,
        string host,
        DomainLifecycleState lifecycleState,
        DomainType domainType = DomainType.Alias,
        bool isDeleted = false)
    {
        var domain = new Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = host,
            NormalizedHostName = host.ToLowerInvariant(),
            DomainType = domainType,
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

    private static CertificateProviderOptions ProviderOptions(bool enabled) => new()
    {
        Enabled = enabled,
        Provider = ProviderType.LetsEncrypt,
        Environment = CertificateProviderEnvironment.Staging,
        AccountReference = enabled ? "le-staging-account" : string.Empty,
        ConfiguredIssuer = "lets-encrypt",
        ContactEmail = "ops@example.com",
        DirectoryUrl = enabled ? new Uri("https://acme-staging-v02.api.letsencrypt.org/directory") : null,
    };

    private static string SerializedCertificatePersistence(ApplicationDbContext context)
    {
        var certificates = context.SslCertificates.AsNoTracking().ToList();
        var operations = context.SslCertificateOperations.AsNoTracking().ToList();
        var audits = context.DomainAuditEntries.AsNoTracking().ToList();
        return System.Text.Json.JsonSerializer.Serialize(new { certificates, operations, audits });
    }

    private sealed record TestProvisioningService(
        CertificateProvisioningService Service,
        FakeCertificateProvider Provider);

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

    private sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public FixedOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
