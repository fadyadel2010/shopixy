using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Tests.Architecture;
using PointOfSale.Tests.Fakes;

namespace PointOfSale.Tests.CustomDomains;

public sealed class Domain53AcmeFoundationTests
{
    [Fact]
    public void ProviderBoundary_IsProviderNeutralAndDoesNotExposeAcmeLibraries()
    {
        var contracts = CertificationSource.ReadRepoFile("PointOfSale.Core/Certificates/CertificateProviderContracts.cs");
        var coreProject = CertificationSource.ReadRepoFile("PointOfSale.Core/PointOfSale.Core.csproj");

        Assert.Contains("public interface ICertificateProvider", contracts);
        Assert.Contains("CertificateIssuanceRequest", contracts);
        Assert.Contains("CertificateProviderError", contracts);
        Assert.DoesNotContain("Certes", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BouncyCastle", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ACMESharp", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Caddy", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpResponseMessage", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonPropertyName", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", coreProject, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountModel_UsesOpaqueReferencesAndExternalKeyCustody()
    {
        var account = new AcmeAccountReference(
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "le-staging-platform-account",
            AcmeAccountRegistrationStatus.Valid,
            CertificateSecretCustody.ExternalEdgeStorage,
            ProviderAccountIdentifier: "acct-123",
            ContactEmail: "ops@example.com",
            CorrelationId: "corr-1");

        account.Validate();

        Assert.Equal("le-staging-platform-account", account.AccountReference);
        Assert.Equal(CertificateSecretCustody.ExternalEdgeStorage, account.KeyCustody);

        var contracts = CertificationSource.ReadRepoFile("PointOfSale.Core/Certificates/CertificateProviderContracts.cs");
        Assert.DoesNotContain("PrivateKey", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("Jwk", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("Pem", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountKey", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountSelection_SeparatesProviderEnvironmentAndAccountReference()
    {
        var selector = new CertificateProviderAccountSelector(new FixedOptionsMonitor(new CertificateProviderOptions
        {
            Enabled = true,
            Provider = ProviderType.LetsEncrypt,
            Environment = CertificateProviderEnvironment.Production,
            AccountReference = "le-production-platform-account",
            ConfiguredIssuer = "lets-encrypt"
        }));

        var selection = selector.Select(new CertificateProviderAccountSelectionRequest(
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Production,
            "le-production-platform-account",
            "lets-encrypt",
            Guid.NewGuid(),
            Guid.NewGuid()));

        Assert.Equal(CertificateProviderEnvironment.Production, selection.Environment);
        Assert.Equal("le-production-platform-account", selection.AccountReference);

        Assert.Throws<InvalidOperationException>(() => selector.Select(new CertificateProviderAccountSelectionRequest(
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "le-production-platform-account",
            "lets-encrypt",
            Guid.NewGuid(),
            Guid.NewGuid())));
    }

    [Fact]
    public async Task OrderContract_ConstructsSafeProviderOrderWithSanAndIdempotencyCorrelation()
    {
        var provider = new FakeCertificateProvider { Scenario = FakeCertificateProviderScenario.OrderCreated };
        var request = BuildIssuanceRequest();

        var result = await provider.CreateOrderAsync(request);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Order);
        Assert.Equal(request.Context.OperationId, result.Order!.OperationId);
        Assert.Equal("store.example.com", result.Order.PrimaryIdentifier);
        Assert.Contains("www.store.example.com", result.Order.RequestedIdentifiers);
        Assert.DoesNotContain("transient-token", result.Order.ProviderOrderReference, StringComparison.Ordinal);
        Assert.Equal(CertificateProviderOperationStatus.Pending, result.Operation!.ProviderStatus);
    }

    [Fact]
    public async Task AuthorizationContract_OffersHttp01AndTlsAlpn01AndRejectsDns01()
    {
        var provider = new FakeCertificateProvider { Scenario = FakeCertificateProviderScenario.AuthorizationPending };
        var result = await provider.GetAuthorizationAsync(new CertificateAuthorizationRequest(
            BuildContext(),
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "fake-authz",
            "corr-1"));

        Assert.True(result.Succeeded);
        Assert.Contains(CertificateChallengeType.Http01, result.Authorization!.AvailableChallengeTypes);
        Assert.Contains(CertificateChallengeType.TlsAlpn01, result.Authorization.AvailableChallengeTypes);
        Assert.DoesNotContain(CertificateChallengeType.Dns01, result.Authorization.AvailableChallengeTypes);

        var dnsAuthorization = result.Authorization with
        {
            AvailableChallengeTypes = Array.AsReadOnly(new[] { CertificateChallengeType.Dns01 })
        };

        Assert.Throws<NotSupportedException>(() => dnsAuthorization.Validate());
    }

    [Fact]
    public async Task ChallengeContract_RedactsSecretAndKeepsItTransient()
    {
        var provider = new FakeCertificateProvider { Scenario = FakeCertificateProviderScenario.ChallengeAvailable };
        var result = await provider.GetChallengeAsync(new CertificateChallengeRequest(
            BuildContext(),
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "fake-challenge:http",
            CertificateChallengeType.Http01,
            "corr-1"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Challenge!.Secret);
        Assert.Equal("[CERTIFICATE_CHALLENGE_SECRET_REDACTED]", result.Challenge.Secret!.ToString());

        var safeChallenge = result.Challenge.WithoutSecret();
        Assert.Null(safeChallenge.Secret);
        Assert.Throws<NotSupportedException>(() => new CertificateChallengeRequest(
            BuildContext(),
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "fake-challenge:dns",
            CertificateChallengeType.Dns01,
            "corr-1").Validate());
    }

    [Fact]
    public void CertificateRequest_RejectsMissingTenantDomainOperationAndDnsChallengePolicy()
    {
        Assert.Throws<ArgumentException>(() => BuildContext(tenantId: Guid.Empty).Validate());
        Assert.Throws<ArgumentException>(() => BuildContext(domainId: Guid.Empty).Validate());
        Assert.Throws<ArgumentException>(() => BuildContext(operationId: Guid.Empty).Validate());
        Assert.Throws<ArgumentException>(() => BuildContext(idempotencyKey: " ").Validate());
        Assert.Throws<NotSupportedException>(() => new CertificateChallengePolicy(
            Array.AsReadOnly(new[] { CertificateChallengeType.Dns01 })).Validate());
    }

    [Fact]
    public async Task ProviderErrorNormalization_CoversEveryCanonicalCategoryWithRetryability()
    {
        foreach (var category in Enum.GetValues<CertificateProviderErrorCategory>())
        {
            var error = CertificateProviderError.Create(
                category,
                $"CERT_{category}",
                CertificateProviderRetryability.IsRetryable(category),
                "Safe provider message.",
                safeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["http_status"] = "429",
                    ["challenge_token"] = "must-not-leak"
                });

            Assert.Equal(category, error.Category);
            Assert.Equal(CertificateProviderRetryability.IsRetryable(category), error.Retryable);
            Assert.DoesNotContain("challenge_token", error.SafeMetadata.Keys);
            Assert.DoesNotContain("must-not-leak", string.Join('|', error.SafeMetadata.Values), StringComparison.Ordinal);
        }

        var provider = new FakeCertificateProvider { Scenario = FakeCertificateProviderScenario.RateLimited };
        var rateLimited = await provider.CreateOrderAsync(BuildIssuanceRequest());

        Assert.False(rateLimited.Succeeded);
        Assert.Equal(CertificateProviderErrorCategory.RateLimited, rateLimited.Error!.Category);
        Assert.True(rateLimited.Error.Retryable);
    }

    [Fact]
    public async Task FakeProvider_CanSimulateRequiredProviderOutcomes()
    {
        foreach (var scenario in Enum.GetValues<FakeCertificateProviderScenario>())
        {
            var provider = new FakeCertificateProvider { Scenario = scenario };
            var account = await provider.EnsureAccountAsync(BuildAccountRequest());
            Assert.True(account.Succeeded || scenario == FakeCertificateProviderScenario.ProviderUnavailable);

            var order = await provider.CreateOrderAsync(BuildIssuanceRequest());
            Assert.True(order.Succeeded ||
                scenario is FakeCertificateProviderScenario.OrderFailure
                    or FakeCertificateProviderScenario.RateLimited
                    or FakeCertificateProviderScenario.Timeout);
        }
    }

    [Fact]
    public async Task Cancellation_PropagatesBeforeProviderWork()
    {
        var provider = new FakeCertificateProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.CreateOrderAsync(BuildIssuanceRequest(), cts.Token));
    }

    [Fact]
    public void InfrastructureBoundary_ExecutesThroughTypedCaddyAndExternalSniBoundaries()
    {
        var infrastructure = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Certificates/CertificateProviderFoundation.cs");

        Assert.Contains("CertificateProviderOptions", infrastructure);
        Assert.Contains("CertificateProvider:DirectoryUrl", infrastructure);
        Assert.Contains("ICaddyEdgeClient", infrastructure);
        Assert.Contains("IEdgeCertificateProbe", infrastructure);
        Assert.Contains("SupportsLiveIssuance = true", infrastructure);
        Assert.Contains("ICertificateRevocationControlClient", infrastructure);
        Assert.Contains("HttpCertificateRevocationControlClient", infrastructure);
        Assert.Contains("SupportsLiveRevocation = true", infrastructure);
        Assert.DoesNotContain("CERTIFICATE_PROVIDER_EXECUTION_OUT_OF_SCOPE", infrastructure);
        Assert.Contains("CertificateProviderHealthCheck", infrastructure);
        Assert.DoesNotContain("Caddyfile", infrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("/config/", infrastructure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderHealthCheck_FailsWhenConfiguredAdapterCannotExecuteLiveOperations()
    {
        var healthCheck = new CertificateProviderHealthCheck(new FixedOptionsMonitor(new CertificateProviderOptions
        {
            Enabled = true,
            Provider = ProviderType.LetsEncrypt,
            Environment = CertificateProviderEnvironment.Staging,
            AccountReference = "le-staging-account",
            DirectoryUrl = new Uri("https://acme-staging-v02.api.letsencrypt.org/directory"),
        }));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("revocation control", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionProvider_AdmitsRealEdgeRoutePollsExternalSniAndRevokesThroughControlSurface()
    {
        var providerOptions = new FixedOptionsMonitor(new CertificateProviderOptions
        {
            Enabled = true,
            Provider = ProviderType.LetsEncrypt,
            Environment = CertificateProviderEnvironment.Staging,
            AccountReference = "le-staging-platform-account",
            DirectoryUrl = new Uri("https://acme-staging-v02.api.letsencrypt.org/directory"),
            RevocationControl = new CertificateRevocationControlOptions
            {
                Enabled = true,
                BaseUrl = new Uri("https://revocation-control.example.test"),
                BearerToken = "test-token",
                ControlSurfaceReference = "test-revocation-control",
            },
        });
        var edge = new RecordingEdgeClient();
        var probe = new MutableEdgeProbe();
        var revocationControl = new RecordingRevocationControlClient();
        var provider = new CaddyManagedAcmeCertificateProvider(
            providerOptions,
            new FixedEdgeOptionsMonitor(new CaddyEdgeOptions
            {
                Enabled = true,
                ManagedRoutesDirectory = "managed",
                StorefrontRoot = "storefront",
                LogRoot = "logs",
                ApiUpstream = "api.example.com:443",
                ValidateCommand = "caddy validate",
                ReloadCommand = "caddy reload",
            }),
            edge,
            probe,
            revocationControl,
            NullLogger<CaddyManagedAcmeCertificateProvider>.Instance);

        var request = BuildIssuanceRequest() with
        {
            SubjectAlternativeNames = Array.AsReadOnly(new[] { "store.example.com" })
        };
        var order = await provider.CreateOrderAsync(request);

        Assert.True(order.Succeeded);
        Assert.Equal(CertificateProviderOperationStatus.Pending, order.Operation!.ProviderStatus);
        Assert.Equal(1, edge.EnsureCalls);

        probe.Certificate = new EdgeCertificateIdentity(
            "CN=store.example.com",
            "CN=Test CA",
            Array.AsReadOnly(new[] { "store.example.com" }),
            "THUMBPRINT",
            "FINGERPRINT",
            "SERIAL",
            "sha256RSA",
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddDays(90));
        var status = await provider.GetOperationStatusAsync(new CertificateProviderOperationQuery(
            request.Context,
            request.Account.Provider,
            request.Account.Environment,
            order.Operation.ProviderOperationReference,
            "corr-1"));

        Assert.True(status.Succeeded);
        Assert.Equal(CertificateProviderOperationStatus.Succeeded, status.Operation!.ProviderStatus);
        Assert.Equal("THUMBPRINT", status.Operation.CertificateMetadata!.Thumbprint);

        var revocation = await provider.RevokeCertificateAsync(new CertificateRevocationProviderRequest(
            request.Context with { OperationType = CertificateOperationType.Revocation },
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "THUMBPRINT",
            order.Operation.ProviderOperationReference,
            "key_compromise",
            "corr-revoke",
            new CertificateRevocationCertificateIdentity(
                request.Context.Generation,
                request.NormalizedHostName,
                "THUMBPRINT",
                "FINGERPRINT",
                "SERIAL",
                "CN=Test CA",
                "CN=store.example.com",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(89))));
        Assert.True(revocation.Succeeded);
        Assert.Equal(CertificateProviderRevocationStatus.Success, revocation.Status);
        Assert.Equal("control-revocation", revocation.ProviderRevocationReference);
        Assert.Equal(1, revocationControl.RevocationCalls);
        Assert.Equal("SERIAL", revocationControl.LastRequest!.CertificateIdentity.SerialNumber);
    }

    [Theory]
    [InlineData("SUCCESS", true, null, null)]
    [InlineData("ALREADY_REVOKED", true, null, null)]
    [InlineData("RATE_LIMITED", false, "CA_REVOCATION_RATE_LIMITED", true)]
    [InlineData("UNAUTHORIZED", false, "CA_REVOCATION_UNAUTHORIZED", false)]
    [InlineData("INVALID_CERTIFICATE", false, "CA_REVOCATION_INVALID_CERTIFICATE", false)]
    public async Task RevocationControlClient_NormalizesCanonicalProviderResults(
        string status,
        bool succeeded,
        string? expectedCode,
        bool? expectedRetryable)
    {
        var client = new HttpCertificateRevocationControlClient(
            new StaticHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"status":"{{status}}","revocationReference":"rev-ref","safeMessage":"safe","retryAfterUtc":"2030-01-01T00:00:00Z"}""",
                    Encoding.UTF8,
                    "application/json")
            }),
            new FixedOptionsMonitor(new CertificateProviderOptions
            {
                Enabled = true,
                Provider = ProviderType.LetsEncrypt,
                Environment = CertificateProviderEnvironment.Staging,
                AccountReference = "le-staging-platform-account",
                DirectoryUrl = new Uri("https://acme-staging-v02.api.letsencrypt.org/directory"),
                RevocationControl = new CertificateRevocationControlOptions
                {
                    Enabled = true,
                    BaseUrl = new Uri("https://revocation-control.example.test"),
                    BearerToken = "test-token",
                },
            }),
            NullLogger<HttpCertificateRevocationControlClient>.Instance);

        var result = await client.RevokeAsync(BuildRevocationControlRequest(), CancellationToken.None);

        Assert.Equal(succeeded, result.Succeeded);
        if (succeeded)
        {
            Assert.Equal(status, result.Status);
            Assert.Equal("rev-ref", result.RevocationReference);
        }
        else
        {
            Assert.Equal(expectedCode, result.Error!.Code);
            Assert.Equal(expectedRetryable, result.Error.Retryable);
        }
    }

    [Fact]
    public async Task RevocationControlClient_RejectsInsecureOrUnconfiguredControlSurfaceWithoutHttpCall()
    {
        var factory = new StaticHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpCertificateRevocationControlClient(
            factory,
            new FixedOptionsMonitor(new CertificateProviderOptions
            {
                Enabled = true,
                Provider = ProviderType.LetsEncrypt,
                Environment = CertificateProviderEnvironment.Staging,
                AccountReference = "le-staging-platform-account",
                DirectoryUrl = new Uri("https://acme-staging-v02.api.letsencrypt.org/directory"),
                RevocationControl = new CertificateRevocationControlOptions
                {
                    Enabled = true,
                    BaseUrl = new Uri("http://revocation-control.example.test"),
                    BearerToken = "test-token",
                },
            }),
            NullLogger<HttpCertificateRevocationControlClient>.Instance);

        var result = await client.RevokeAsync(BuildRevocationControlRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CADDY_CA_REVOCATION_CONTROL_UNAVAILABLE", result.Error!.Code);
        Assert.Equal(0, factory.SendCount);
    }

    [Fact]
    public void ArchitectureBoundary_NoAcmePackageLeaksIntoCoreOrApplication()
    {
        var coreProject = CertificationSource.ReadRepoFile("PointOfSale.Core/PointOfSale.Core.csproj");
        var apiProject = CertificationSource.ReadRepoFile("PointOfSale.API/PointOfSale.API.csproj");
        var infrastructureProject = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/PointOfSale.Infrastructure.csproj");

        foreach (var source in new[] { coreProject, apiProject, infrastructureProject })
        {
            Assert.DoesNotContain("Certes", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ACMESharp", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BouncyCastle", source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("CaddyManagedAcmeCertificateProvider", coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain("CaddyManagedAcmeCertificateProvider", apiProject, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretLeakageScan_FoundationDoesNotPersistPrivateKeyOrChallengeSecrets()
    {
        var checkedFiles = new[]
        {
            "PointOfSale.Core/Certificates/CertificateProviderContracts.cs",
            "PointOfSale.Infrastructure/Certificates/CertificateProviderFoundation.cs",
            "PointOfSale.Core/Entities/SslCertificate.cs",
            "PointOfSale.Core/Entities/SslCertificateOperation.cs",
            "PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs",
            "PointOfSale.Infrastructure/Data/Configurations/SslCertificateOperationConfiguration.cs",
        };

        var forbiddenMembers = new[]
        {
            "public string PrivateKey",
            "public string? PrivateKey",
            "public byte[] PrivateKey",
            "BEGIN PRIVATE KEY",
            "AcmeAccountPrivateKey",
            "JwkPrivate",
            "PrivateKeyPem",
            "DnsProviderCredential",
            "ProviderCredential",
            "ChallengeSecretEntity",
            "DbSet<CertificateChallenge",
        };

        foreach (var path in checkedFiles)
        {
            var source = CertificationSource.ReadRepoFile(path);
            foreach (var forbidden in forbiddenMembers)
            {
                Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void MigrationDecision_Domain53DoesNotIntroduceNewPersistence()
    {
        var migrationFiles = Directory.EnumerateFiles(FindRepoRoot(), "*DOMAIN_5_3*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(migrationFiles);
    }

    private static AcmeAccountRequest BuildAccountRequest() => new(
        BuildContext(),
        ProviderType.LetsEncrypt,
        CertificateProviderEnvironment.Staging,
        "le-staging-platform-account",
        "ops@example.com",
        "lets-encrypt",
        "corr-1");

    private static CertificateIssuanceRequest BuildIssuanceRequest() => new(
        BuildContext(),
        "store.example.com",
        Array.AsReadOnly(new[] { "store.example.com", "www.store.example.com" }),
        new AcmeAccountReference(
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "le-staging-platform-account",
            AcmeAccountRegistrationStatus.Valid,
            CertificateSecretCustody.ExternalEdgeStorage,
            "acct-123",
            "ops@example.com",
            "corr-1"),
        CertificateChallengePolicy.Default,
        ProviderOperationReference: "operation-ref",
        CorrelationId: "corr-1");

    private static CertificateTenantOperationContext BuildContext(
        Guid? tenantId = null,
        Guid? domainId = null,
        Guid? operationId = null,
        string idempotencyKey = "idem-1") => new(
        tenantId ?? Guid.NewGuid(),
        domainId ?? Guid.NewGuid(),
        CertificateId: Guid.NewGuid(),
        operationId ?? Guid.NewGuid(),
        CertificateOperationType.Provisioning,
        idempotencyKey,
        Generation: 1,
        FencingToken: 1);

    private static CertificateRevocationControlRequest BuildRevocationControlRequest()
    {
        var context = BuildContext();
        return new CertificateRevocationControlRequest(
            context.TenantId,
            context.DomainId,
            context.CertificateId!.Value,
            context.OperationId,
            context.IdempotencyKey,
            ProviderType.LetsEncrypt,
            CertificateProviderEnvironment.Staging,
            "le-staging-platform-account",
            "key_compromise",
            "provider-cert-ref",
            "edge-cert-ref",
            new CertificateRevocationCertificateIdentity(
                1,
                "store.example.com",
                "THUMBPRINT",
                "FINGERPRINT",
                "SERIAL",
                "CN=Test CA",
                "CN=store.example.com",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(90)),
            "corr-1");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private sealed class FixedOptionsMonitor : IOptionsMonitor<CertificateProviderOptions>
    {
        public FixedOptionsMonitor(CertificateProviderOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public CertificateProviderOptions CurrentValue { get; }

        public CertificateProviderOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<CertificateProviderOptions, string?> listener) => null;
    }

    private sealed class FixedEdgeOptionsMonitor : IOptionsMonitor<CaddyEdgeOptions>
    {
        public FixedEdgeOptionsMonitor(CaddyEdgeOptions value) => CurrentValue = value;
        public CaddyEdgeOptions CurrentValue { get; }
        public CaddyEdgeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<CaddyEdgeOptions, string?> listener) => null;
    }

    private sealed class RecordingEdgeClient : ICaddyEdgeClient
    {
        public int EnsureCalls { get; private set; }

        public Task<CaddyDomainRouteResult> EnsureDomainRouteAsync(CaddyDomainRouteRequest request, CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            return Task.FromResult(CaddyDomainRouteResult.Success(
                new CaddyDomainRouteState(true, true, request.NormalizedHostName, "edge:test", "api.example.com:443", null, null),
                true,
                "edge:test"));
        }

        public Task<CaddyDomainRouteState> GetDomainRouteStateAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaddyDomainRouteState(true, true, query.NormalizedHostName, "edge:test", "api.example.com:443", null, null));

        public Task<CaddyDomainRouteResult> RemoveDomainRouteAsync(CaddyDomainRouteQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaddyDomainRouteResult.Success(CaddyDomainRouteState.Missing(query.NormalizedHostName), true, null));
    }

    private sealed class MutableEdgeProbe : IEdgeCertificateProbe
    {
        public EdgeCertificateIdentity? Certificate { get; set; }

        public Task<EdgeCertificateProbeResult> ProbeAsync(EdgeCertificateProbeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Certificate is null
                ? new EdgeCertificateProbeResult(false, true, request.NormalizedHostName, "SNI_DNS_OR_CONNECT_FAILURE", "Certificate is not observable yet.", null)
                : new EdgeCertificateProbeResult(true, false, request.NormalizedHostName, null, null, Certificate));
    }

    private sealed class RecordingRevocationControlClient : ICertificateRevocationControlClient
    {
        public int RevocationCalls { get; private set; }
        public CertificateRevocationControlRequest? LastRequest { get; private set; }

        public Task<CertificateRevocationControlResult> RevokeAsync(
            CertificateRevocationControlRequest request,
            CancellationToken cancellationToken)
        {
            RevocationCalls++;
            LastRequest = request;
            return Task.FromResult(new CertificateRevocationControlResult(
                true,
                "SUCCESS",
                "control-revocation",
                null));
        }

        public Task<CertificateRevocationControlHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CertificateRevocationControlHealthResult(true, "healthy", null));
    }

    private sealed class StaticHttpClientFactory(HttpResponseMessage response) : IHttpClientFactory
    {
        private readonly CountingHandler _handler = new(response);

        public int SendCount => _handler.SendCount;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false);

        private sealed class CountingHandler(HttpResponseMessage response) : HttpMessageHandler
        {
            public int SendCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SendCount++;
                return Task.FromResult(response);
            }
        }
    }
}
