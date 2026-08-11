using PointOfSale.Core.Certificates;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Fakes;

internal sealed class FakeCertificateProvider : ICertificateProvider
{
    private int _ensureAccountCallCount;
    private int _createOrderCallCount;
    private int _operationStatusCallCount;
    private int _revocationCallCount;

    public FakeCertificateProviderScenario Scenario { get; set; } = FakeCertificateProviderScenario.SuccessfulIssuanceMetadata;
    public TimeSpan ArtificialDelay { get; set; }
    public int EnsureAccountCallCount => _ensureAccountCallCount;
    public int CreateOrderCallCount => _createOrderCallCount;
    public int OperationStatusCallCount => _operationStatusCallCount;
    public int RevocationCallCount => _revocationCallCount;

    public Task<CertificateProviderAccountResult> EnsureAccountAsync(
        AcmeAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _ensureAccountCallCount);
        DelayIfConfigured(cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        if (Scenario == FakeCertificateProviderScenario.ProviderUnavailable)
        {
            return Task.FromResult(CertificateProviderAccountResult.Failure(Error(
                CertificateProviderErrorCategory.ProviderUnavailable,
                "FAKE_PROVIDER_UNAVAILABLE")));
        }

        return Task.FromResult(CertificateProviderAccountResult.Success(new AcmeAccountReference(
            request.Provider,
            request.Environment,
            request.AccountReference,
            AcmeAccountRegistrationStatus.Valid,
            CertificateSecretCustody.ExternalEdgeStorage,
            ProviderAccountIdentifier: request.AccountReference,
            ContactEmail: request.ContactEmail,
            CorrelationId: request.CorrelationId)));
    }

    public Task<CertificateProviderOrderResult> CreateOrderAsync(
        CertificateIssuanceRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _createOrderCallCount);
        DelayIfConfigured(cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        if (Scenario == FakeCertificateProviderScenario.OrderFailure)
        {
            return Task.FromResult(CertificateProviderOrderResult.Failure(Error(
                CertificateProviderErrorCategory.InvalidOrder,
                "FAKE_INVALID_ORDER")));
        }

        if (Scenario == FakeCertificateProviderScenario.RateLimited)
        {
            return Task.FromResult(CertificateProviderOrderResult.Failure(Error(
                CertificateProviderErrorCategory.RateLimited,
                "FAKE_RATE_LIMITED")));
        }

        if (Scenario == FakeCertificateProviderScenario.Timeout)
        {
            return Task.FromResult(CertificateProviderOrderResult.Failure(Error(
                CertificateProviderErrorCategory.Timeout,
                "FAKE_TIMEOUT")));
        }

        var order = new CertificateOrder(
            request.Context.OperationId,
            ProviderOrderReference: $"fake-order:{request.Context.OperationId:N}",
            CertificateProviderOrderStatus.Pending,
            request.NormalizedHostName,
            request.SubjectAlternativeNames,
            Array.AsReadOnly(new[] { $"fake-authz:{request.NormalizedHostName}" }),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            FinalizedAtUtc: null);

        var metadata = new CertificateProviderOperationMetadata(
            ProviderOperationReference: $"fake-operation:{request.Context.OperationId:N}",
            ProviderRequestReference: request.ProviderOperationReference,
            OrderReference: order.ProviderOrderReference,
            AuthorizationReferences: order.AuthorizationReferences,
            CertificateMetadata: Scenario == FakeCertificateProviderScenario.SuccessfulIssuanceMetadata
                ? new CertificateMetadata(
                    "Fake CA",
                    $"CN={request.NormalizedHostName}",
                     request.SubjectAlternativeNames,
                     "FAKE_THUMBPRINT",
                     "FAKE_FINGERPRINT_SHA256",
                     DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(90),
                    "FAKE_SERIAL",
                    "FAKE_SHA256")
                : null,
            ProviderStatus: Scenario == FakeCertificateProviderScenario.SuccessfulIssuanceMetadata
                ? CertificateProviderOperationStatus.Succeeded
                : CertificateProviderOperationStatus.Pending);

        return Task.FromResult(CertificateProviderOrderResult.Success(order, metadata));
    }

    public Task<CertificateProviderAuthorizationResult> GetAuthorizationAsync(
        CertificateAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        var authorization = new CertificateAuthorization(
            request.AuthorizationReference,
            "example.com",
            CertificateProviderAuthorizationStatus.Pending,
            DateTimeOffset.UtcNow.AddHours(1),
            Array.AsReadOnly(new[] { CertificateChallengeType.Http01, CertificateChallengeType.TlsAlpn01 }),
            Array.AsReadOnly(new[] { "fake-challenge:http", "fake-challenge:alpn" }));

        return Task.FromResult(CertificateProviderAuthorizationResult.Success(authorization));
    }

    public Task<CertificateProviderChallengeResult> GetChallengeAsync(
        CertificateChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        if (Scenario == FakeCertificateProviderScenario.ChallengeFailure)
        {
            return Task.FromResult(CertificateProviderChallengeResult.Failure(Error(
                CertificateProviderErrorCategory.ChallengeFailed,
                "FAKE_CHALLENGE_FAILED")));
        }

        var challenge = new CertificateChallenge(
            request.ChallengeReference,
            request.ChallengeType,
            "example.com",
            new CertificateChallengeSecret(
                "transient-token",
                "transient-key-authorization",
                request.ChallengeType == CertificateChallengeType.Http01 ? "/.well-known/acme-challenge/transient-token" : null,
                request.ChallengeType == CertificateChallengeType.TlsAlpn01 ? "fake-tls-alpn-challenge-ref" : null),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["challenge_reference"] = request.ChallengeReference,
                ["challenge_type"] = request.ChallengeType.ToString()
            });

        return Task.FromResult(CertificateProviderChallengeResult.Success(challenge));
    }

    public Task<CertificateProviderOperationResult> GetOperationStatusAsync(
        CertificateProviderOperationQuery query,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _operationStatusCallCount);
        DelayIfConfigured(cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        query.Validate();

        var metadata = new CertificateProviderOperationMetadata(
            query.ProviderOperationReference,
            ProviderRequestReference: null,
            OrderReference: "fake-order",
            AuthorizationReferences: Array.AsReadOnly(new[] { "fake-authz" }),
            CertificateMetadata: Scenario == FakeCertificateProviderScenario.SuccessfulIssuanceMetadata
                ? new CertificateMetadata(
                    "Fake CA",
                    "CN=example.com",
                     Array.AsReadOnly(new[] { "example.com" }),
                     "FAKE_THUMBPRINT",
                     "FAKE_FINGERPRINT_SHA256",
                     DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(90),
                    "FAKE_SERIAL",
                    "FAKE_SHA256")
                : null,
            ProviderStatus: CertificateProviderOperationStatus.Succeeded);

        return Task.FromResult(CertificateProviderOperationResult.Success(metadata));
    }

    public Task<CertificateProviderRevocationResult> RevokeCertificateAsync(
        CertificateRevocationProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _revocationCallCount);
        DelayIfConfigured(cancellationToken).GetAwaiter().GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        if (Scenario == FakeCertificateProviderScenario.ProviderUnavailable)
        {
            return Task.FromResult(CertificateProviderRevocationResult.Failure(Error(
                CertificateProviderErrorCategory.ProviderUnavailable,
                "FAKE_PROVIDER_UNAVAILABLE")));
        }

        if (Scenario == FakeCertificateProviderScenario.OrderFailure)
        {
            return Task.FromResult(CertificateProviderRevocationResult.Failure(Error(
                CertificateProviderErrorCategory.CertificateRejected,
                "FAKE_REVOCATION_REJECTED")));
        }

        return Task.FromResult(CertificateProviderRevocationResult.Success(
            $"fake-revocation:{request.Context.OperationId:N}"));
    }

    private static CertificateProviderError Error(CertificateProviderErrorCategory category, string code) =>
        CertificateProviderError.Create(
            category,
            code,
            CertificateProviderRetryability.IsRetryable(category),
            "Fake certificate provider normalized failure.");

    private Task DelayIfConfigured(CancellationToken cancellationToken) =>
        ArtificialDelay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(ArtificialDelay, cancellationToken);
}

internal enum FakeCertificateProviderScenario
{
    AccountAvailable = 0,
    OrderCreated = 1,
    AuthorizationPending = 2,
    ChallengeAvailable = 3,
    ChallengeFailure = 4,
    OrderFailure = 5,
    RateLimited = 6,
    Timeout = 7,
    ProviderUnavailable = 8,
    SuccessfulIssuanceMetadata = 9,
}
