using System.Collections.ObjectModel;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Certificates;

public interface ICertificateProvider
{
    Task<CertificateProviderAccountResult> EnsureAccountAsync(
        AcmeAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<CertificateProviderOrderResult> CreateOrderAsync(
        CertificateIssuanceRequest request,
        CancellationToken cancellationToken = default);

    Task<CertificateProviderAuthorizationResult> GetAuthorizationAsync(
        CertificateAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    Task<CertificateProviderChallengeResult> GetChallengeAsync(
        CertificateChallengeRequest request,
        CancellationToken cancellationToken = default);

    Task<CertificateProviderOperationResult> GetOperationStatusAsync(
        CertificateProviderOperationQuery query,
        CancellationToken cancellationToken = default);

    Task<CertificateProviderRevocationResult> RevokeCertificateAsync(
        CertificateRevocationProviderRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICertificateProviderAccountSelector
{
    CertificateProviderAccountSelection Select(CertificateProviderAccountSelectionRequest request);
}

public enum CertificateProviderEnvironment
{
    Staging = 0,
    Production = 1,
}

public enum AcmeAccountRegistrationStatus
{
    Unknown = 0,
    Pending = 1,
    Valid = 2,
    Deactivated = 3,
    Revoked = 4,
}

public enum CertificateSecretCustody
{
    ExternalEdgeStorage = 0,
    ExternalSecretStore = 1,
}

public enum CertificateProviderOrderStatus
{
    Unknown = 0,
    Pending = 1,
    Ready = 2,
    Processing = 3,
    Valid = 4,
    Invalid = 5,
    Expired = 6,
}

public enum CertificateProviderAuthorizationStatus
{
    Unknown = 0,
    Pending = 1,
    Valid = 2,
    Invalid = 3,
    Expired = 4,
    Revoked = 5,
}

public enum CertificateChallengeType
{
    Http01 = 0,
    TlsAlpn01 = 1,
    Dns01 = 2,
}

public enum CertificateProviderOperationStatus
{
    Unknown = 0,
    Accepted = 1,
    Pending = 2,
    Succeeded = 3,
    Failed = 4,
}

public enum CertificateProviderErrorCategory
{
    ConfigurationError = 0,
    AuthenticationError = 1,
    AuthorizationDenied = 2,
    RateLimited = 3,
    BadNonce = 4,
    InvalidOrder = 5,
    ChallengeFailed = 6,
    DomainRejected = 7,
    CertificateRejected = 8,
    NetworkFailure = 9,
    Timeout = 10,
    ProviderUnavailable = 11,
    Conflict = 12,
    InvalidState = 13,
    UnknownProviderError = 14,
}

public sealed record CertificateTenantOperationContext(
    Guid TenantId,
    Guid DomainId,
    Guid? CertificateId,
    Guid OperationId,
    CertificateOperationType OperationType,
    string IdempotencyKey,
    int Generation,
    long FencingToken)
{
    public void Validate()
    {
        if (TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(TenantId));
        }

        if (DomainId == Guid.Empty)
        {
            throw new ArgumentException("DomainId is required.", nameof(DomainId));
        }

        if (OperationId == Guid.Empty)
        {
            throw new ArgumentException("OperationId is required.", nameof(OperationId));
        }

        if (string.IsNullOrWhiteSpace(IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(IdempotencyKey));
        }

        if (Generation < 1)
        {
            throw new ArgumentException("Generation must be positive.", nameof(Generation));
        }

        if (FencingToken < 1)
        {
            throw new ArgumentException("FencingToken must be positive.", nameof(FencingToken));
        }
    }
}

public sealed record AcmeAccountReference(
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string AccountReference,
    AcmeAccountRegistrationStatus RegistrationStatus,
    CertificateSecretCustody KeyCustody,
    string? ProviderAccountIdentifier,
    string? ContactEmail,
    string? CorrelationId)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountReference))
        {
            throw new ArgumentException("Account reference is required.", nameof(AccountReference));
        }

        if (KeyCustody is not CertificateSecretCustody.ExternalEdgeStorage
            and not CertificateSecretCustody.ExternalSecretStore)
        {
            throw new ArgumentException("Account key custody must be external.", nameof(KeyCustody));
        }
    }
}

public sealed record AcmeAccountRequest(
    CertificateTenantOperationContext Context,
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string AccountReference,
    string? ContactEmail,
    string? ConfiguredIssuer,
    string? CorrelationId)
{
    public void Validate()
    {
        Context.Validate();
        if (string.IsNullOrWhiteSpace(AccountReference))
        {
            throw new ArgumentException("Account reference is required.", nameof(AccountReference));
        }
    }
}

public sealed record CertificateProviderAccountSelectionRequest(
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string AccountReference,
    string? ConfiguredIssuer,
    Guid TenantId,
    Guid DomainId)
{
    public void Validate()
    {
        if (TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(TenantId));
        }

        if (DomainId == Guid.Empty)
        {
            throw new ArgumentException("DomainId is required.", nameof(DomainId));
        }

        if (string.IsNullOrWhiteSpace(AccountReference))
        {
            throw new ArgumentException("Account reference is required.", nameof(AccountReference));
        }
    }
}

public sealed record CertificateProviderAccountSelection(
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string AccountReference,
    string? ConfiguredIssuer);

public sealed record CertificateIssuanceRequest(
    CertificateTenantOperationContext Context,
    string NormalizedHostName,
    IReadOnlyCollection<string> SubjectAlternativeNames,
    AcmeAccountReference Account,
    CertificateChallengePolicy ChallengePolicy,
    string? ProviderOperationReference,
    string? CorrelationId)
{
    public void Validate()
    {
        Context.Validate();
        Account.Validate();
        CertificateHostName.Validate(NormalizedHostName);

        if (SubjectAlternativeNames is null || SubjectAlternativeNames.Count == 0)
        {
            throw new ArgumentException("At least one requested identifier is required.", nameof(SubjectAlternativeNames));
        }

        foreach (var name in SubjectAlternativeNames)
        {
            CertificateHostName.Validate(name);
        }

        ChallengePolicy.Validate();
    }
}

public sealed record CertificateChallengePolicy(IReadOnlyCollection<CertificateChallengeType> AllowedChallengeTypes)
{
    public static CertificateChallengePolicy Default { get; } = new(
        Array.AsReadOnly(new[] { CertificateChallengeType.Http01, CertificateChallengeType.TlsAlpn01 }));

    public void Validate()
    {
        if (AllowedChallengeTypes is null || AllowedChallengeTypes.Count == 0)
        {
            throw new ArgumentException("At least one challenge type is required.", nameof(AllowedChallengeTypes));
        }

        if (AllowedChallengeTypes.Contains(CertificateChallengeType.Dns01))
        {
            throw new NotSupportedException("DNS-01 is outside the DOMAIN-5 tenant certificate boundary.");
        }
    }
}

public sealed record CertificateOrder(
    Guid OperationId,
    string ProviderOrderReference,
    CertificateProviderOrderStatus Status,
    string PrimaryIdentifier,
    IReadOnlyCollection<string> RequestedIdentifiers,
    IReadOnlyCollection<string> AuthorizationReferences,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? FinalizedAtUtc)
{
    public void Validate()
    {
        if (OperationId == Guid.Empty)
        {
            throw new ArgumentException("OperationId is required.", nameof(OperationId));
        }

        if (string.IsNullOrWhiteSpace(ProviderOrderReference))
        {
            throw new ArgumentException("Provider order reference is required.", nameof(ProviderOrderReference));
        }

        CertificateHostName.Validate(PrimaryIdentifier);
    }
}

public sealed record CertificateAuthorization(
    string AuthorizationReference,
    string DomainIdentifier,
    CertificateProviderAuthorizationStatus Status,
    DateTimeOffset? ExpiresAtUtc,
    IReadOnlyCollection<CertificateChallengeType> AvailableChallengeTypes,
    IReadOnlyCollection<string> ChallengeReferences)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AuthorizationReference))
        {
            throw new ArgumentException("Authorization reference is required.", nameof(AuthorizationReference));
        }

        CertificateHostName.Validate(DomainIdentifier);

        if (AvailableChallengeTypes.Contains(CertificateChallengeType.Dns01))
        {
            throw new NotSupportedException("DNS-01 is outside the DOMAIN-5 tenant certificate boundary.");
        }
    }
}

public sealed record CertificateChallenge(
    string ChallengeReference,
    CertificateChallengeType Type,
    string DomainIdentifier,
    CertificateChallengeSecret? Secret,
    IReadOnlyDictionary<string, string> SafeMetadata)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ChallengeReference))
        {
            throw new ArgumentException("Challenge reference is required.", nameof(ChallengeReference));
        }

        if (Type == CertificateChallengeType.Dns01)
        {
            throw new NotSupportedException("DNS-01 is outside the DOMAIN-5 tenant certificate boundary.");
        }

        CertificateHostName.Validate(DomainIdentifier);
    }

    public CertificateChallenge WithoutSecret() => this with { Secret = null };
}

public sealed record CertificateChallengeSecret
{
    public string Token { get; }
    public string KeyAuthorization { get; }
    public string? HttpPath { get; }
    public string? TlsAlpnCertificateReference { get; }

    public CertificateChallengeSecret(
        string token,
        string keyAuthorization,
        string? httpPath = null,
        string? tlsAlpnCertificateReference = null)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Challenge token is required.", nameof(token));
        }

        if (string.IsNullOrWhiteSpace(keyAuthorization))
        {
            throw new ArgumentException("Challenge key authorization is required.", nameof(keyAuthorization));
        }

        Token = token;
        KeyAuthorization = keyAuthorization;
        HttpPath = httpPath;
        TlsAlpnCertificateReference = tlsAlpnCertificateReference;
    }

    public override string ToString() => "[CERTIFICATE_CHALLENGE_SECRET_REDACTED]";
}

public sealed record CertificateMetadata(
    string? Issuer,
    string? Subject,
    IReadOnlyCollection<string> SubjectAlternativeNames,
    string? Thumbprint,
    string? Fingerprint,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    string? SerialNumber,
    string? SignatureAlgorithm);

public sealed record CertificateProviderOperationMetadata(
    string? ProviderOperationReference,
    string? ProviderRequestReference,
    string? OrderReference,
    IReadOnlyCollection<string> AuthorizationReferences,
    CertificateMetadata? CertificateMetadata,
    CertificateProviderOperationStatus ProviderStatus);

public sealed record CertificateProviderOperationQuery(
    CertificateTenantOperationContext Context,
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string ProviderOperationReference,
    string? CorrelationId)
{
    public void Validate()
    {
        Context.Validate();
        if (string.IsNullOrWhiteSpace(ProviderOperationReference))
        {
            throw new ArgumentException("Provider operation reference is required.", nameof(ProviderOperationReference));
        }
    }
}

public sealed record CertificateRevocationProviderRequest(
    CertificateTenantOperationContext Context,
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string? ProviderCertificateReference,
    string? EdgeCertificateReference,
    string ReasonCode,
    string? CorrelationId,
    CertificateRevocationCertificateIdentity? CertificateIdentity = null)
{
    public void Validate()
    {
        Context.Validate();
        if (Context.CertificateId is null || Context.CertificateId == Guid.Empty)
        {
            throw new ArgumentException("CertificateId is required for revocation.");
        }

        if (string.IsNullOrWhiteSpace(ReasonCode))
        {
            throw new ArgumentException("Revocation reason is required.", nameof(ReasonCode));
        }

        if (string.IsNullOrWhiteSpace(ProviderCertificateReference)
            && string.IsNullOrWhiteSpace(EdgeCertificateReference)
            && CertificateIdentity?.HasExactIdentifier != true)
        {
            throw new ArgumentException("Revocation requires an exact certificate identifier.");
        }
    }
}

public sealed record CertificateRevocationCertificateIdentity(
    int Generation,
    string NormalizedHostName,
    string? Thumbprint,
    string? Fingerprint,
    string? SerialNumber,
    string? Issuer,
    string? Subject,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc)
{
    public bool HasExactIdentifier =>
        !string.IsNullOrWhiteSpace(Thumbprint)
        || !string.IsNullOrWhiteSpace(Fingerprint)
        || !string.IsNullOrWhiteSpace(SerialNumber);

    public void Validate()
    {
        if (Generation < 1)
        {
            throw new ArgumentException("Certificate generation must be positive.", nameof(Generation));
        }

        CertificateHostName.Validate(NormalizedHostName);

        if (!HasExactIdentifier)
        {
            throw new ArgumentException("Certificate identity requires a thumbprint, fingerprint, or serial number.");
        }
    }
}

public enum CertificateProviderRevocationStatus
{
    Success = 0,
    AlreadyRevoked = 1,
}

public sealed record CertificateAuthorizationRequest(
    CertificateTenantOperationContext Context,
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string AuthorizationReference,
    string? CorrelationId)
{
    public void Validate()
    {
        Context.Validate();
        if (string.IsNullOrWhiteSpace(AuthorizationReference))
        {
            throw new ArgumentException("Authorization reference is required.", nameof(AuthorizationReference));
        }
    }
}

public sealed record CertificateChallengeRequest(
    CertificateTenantOperationContext Context,
    ProviderType Provider,
    CertificateProviderEnvironment Environment,
    string ChallengeReference,
    CertificateChallengeType ChallengeType,
    string? CorrelationId)
{
    public void Validate()
    {
        Context.Validate();
        if (string.IsNullOrWhiteSpace(ChallengeReference))
        {
            throw new ArgumentException("Challenge reference is required.", nameof(ChallengeReference));
        }

        if (ChallengeType == CertificateChallengeType.Dns01)
        {
            throw new NotSupportedException("DNS-01 is outside the DOMAIN-5 tenant certificate boundary.");
        }
    }
}

public sealed record CertificateProviderError(
    CertificateProviderErrorCategory Category,
    string Code,
    bool Retryable,
    string SafeMessage,
    string? ProviderStatus,
    string? ProviderReference,
    string? CorrelationId,
    DateTimeOffset? RetryAfterUtc,
    IReadOnlyDictionary<string, string> SafeMetadata)
{
    private static readonly string[] SensitiveTerms =
    [
        "privatekey",
        "private_key",
        "jwk",
        "pem",
        "pfx",
        "pkcs12",
        "token",
        "keyauthorization",
        "key_authorization",
        "secret",
        "credential",
        "password",
        "dnscredential"
    ];

    public static CertificateProviderError Create(
        CertificateProviderErrorCategory category,
        string code,
        bool retryable,
        string safeMessage,
        string? providerStatus = null,
        string? providerReference = null,
        string? correlationId = null,
        DateTimeOffset? retryAfterUtc = null,
        IReadOnlyDictionary<string, string>? safeMetadata = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Provider error code is required.", nameof(code));
        }

        return new CertificateProviderError(
            category,
            code.Trim(),
            retryable,
            SanitizeValue(safeMessage, "Provider error.") ?? "Provider error.",
            SanitizeValue(providerStatus, null),
            SanitizeValue(providerReference, null),
            SanitizeValue(correlationId, null),
            retryAfterUtc,
            SanitizeMetadata(safeMetadata));
    }

    private static IReadOnlyDictionary<string, string> SanitizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        }

        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            if (ContainsSensitiveTerm(key))
            {
                continue;
            }

            sanitized[key] = SanitizeValue(value, string.Empty) ?? string.Empty;
        }

        return new ReadOnlyDictionary<string, string>(sanitized);
    }

    private static string? SanitizeValue(string? value, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return ContainsSensitiveTerm(value) ? "[REDACTED]" : value.Trim();
    }

    private static bool ContainsSensitiveTerm(string value)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SensitiveTerms.Any(term => normalized.Contains(
            term.Replace("_", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal));
    }
}

public abstract record CertificateProviderResult(bool Succeeded, CertificateProviderError? Error)
{
    protected static void ThrowIfInvalidFailure(bool succeeded, CertificateProviderError? error)
    {
        if (!succeeded && error is null)
        {
            throw new ArgumentException("Failed provider results require a normalized provider error.");
        }
    }
}

public sealed record CertificateProviderAccountResult(
    bool Succeeded,
    AcmeAccountReference? Account,
    CertificateProviderError? Error)
    : CertificateProviderResult(Succeeded, Error)
{
    public static CertificateProviderAccountResult Success(AcmeAccountReference account) => new(true, account, null);
    public static CertificateProviderAccountResult Failure(CertificateProviderError error) => new(false, null, error);
}

public sealed record CertificateProviderOrderResult(
    bool Succeeded,
    CertificateOrder? Order,
    CertificateProviderOperationMetadata? Operation,
    CertificateProviderError? Error)
    : CertificateProviderResult(Succeeded, Error)
{
    public static CertificateProviderOrderResult Success(CertificateOrder order, CertificateProviderOperationMetadata operation) =>
        new(true, order, operation, null);

    public static CertificateProviderOrderResult Failure(CertificateProviderError error) => new(false, null, null, error);
}

public sealed record CertificateProviderAuthorizationResult(
    bool Succeeded,
    CertificateAuthorization? Authorization,
    CertificateProviderError? Error)
    : CertificateProviderResult(Succeeded, Error)
{
    public static CertificateProviderAuthorizationResult Success(CertificateAuthorization authorization) => new(true, authorization, null);
    public static CertificateProviderAuthorizationResult Failure(CertificateProviderError error) => new(false, null, error);
}

public sealed record CertificateProviderChallengeResult(
    bool Succeeded,
    CertificateChallenge? Challenge,
    CertificateProviderError? Error)
    : CertificateProviderResult(Succeeded, Error)
{
    public static CertificateProviderChallengeResult Success(CertificateChallenge challenge) => new(true, challenge, null);
    public static CertificateProviderChallengeResult Failure(CertificateProviderError error) => new(false, null, error);
}

public sealed record CertificateProviderOperationResult(
    bool Succeeded,
    CertificateProviderOperationMetadata? Operation,
    CertificateProviderError? Error)
    : CertificateProviderResult(Succeeded, Error)
{
    public static CertificateProviderOperationResult Success(CertificateProviderOperationMetadata operation) => new(true, operation, null);
    public static CertificateProviderOperationResult Failure(CertificateProviderError error) => new(false, null, error);
}

public sealed record CertificateProviderRevocationResult(
    bool Succeeded,
    string? ProviderRevocationReference,
    CertificateProviderRevocationStatus Status,
    CertificateProviderError? Error)
    : CertificateProviderResult(Succeeded, Error)
{
    public static CertificateProviderRevocationResult Success(string? providerRevocationReference) =>
        new(true, providerRevocationReference, CertificateProviderRevocationStatus.Success, null);

    public static CertificateProviderRevocationResult AlreadyRevoked(string? providerRevocationReference) =>
        new(true, providerRevocationReference, CertificateProviderRevocationStatus.AlreadyRevoked, null);

    public static CertificateProviderRevocationResult Failure(CertificateProviderError error) =>
        new(false, null, CertificateProviderRevocationStatus.Success, error);
}

public static class CertificateProviderRetryability
{
    public static bool IsRetryable(CertificateProviderErrorCategory category) =>
        category is CertificateProviderErrorCategory.RateLimited
            or CertificateProviderErrorCategory.BadNonce
            or CertificateProviderErrorCategory.NetworkFailure
            or CertificateProviderErrorCategory.Timeout
            or CertificateProviderErrorCategory.ProviderUnavailable
            or CertificateProviderErrorCategory.Conflict;
}

internal static class CertificateHostName
{
    public static void Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Hostname is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > 253
            || normalized.Contains(" ", StringComparison.Ordinal)
            || normalized.StartsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith(".", StringComparison.Ordinal))
        {
            throw new ArgumentException("Hostname must be a normalized DNS host name.");
        }
    }
}
