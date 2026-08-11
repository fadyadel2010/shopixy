using System.Collections.ObjectModel;

namespace PointOfSale.Core.Certificates;

public interface ICaddyEdgeClient
{
    Task<CaddyDomainRouteResult> EnsureDomainRouteAsync(
        CaddyDomainRouteRequest request,
        CancellationToken cancellationToken = default);

    Task<CaddyDomainRouteState> GetDomainRouteStateAsync(
        CaddyDomainRouteQuery query,
        CancellationToken cancellationToken = default);

    Task<CaddyDomainRouteResult> RemoveDomainRouteAsync(
        CaddyDomainRouteQuery query,
        CancellationToken cancellationToken = default);
}

public interface IEdgeCertificateProbe
{
    Task<EdgeCertificateProbeResult> ProbeAsync(
        EdgeCertificateProbeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CaddyDomainRouteRequest(
    Guid TenantId,
    Guid DomainId,
    Guid CertificateId,
    Guid OperationId,
    string NormalizedHostName,
    string CorrelationId,
    long FencingToken)
{
    public void Validate()
    {
        EdgeCertificateHostName.Validate(NormalizedHostName);
        if (TenantId == Guid.Empty || DomainId == Guid.Empty || CertificateId == Guid.Empty || OperationId == Guid.Empty)
        {
            throw new ArgumentException("Tenant, domain, certificate, and operation identifiers are required.");
        }
        if (FencingToken < 1)
        {
            throw new ArgumentException("FencingToken must be positive.");
        }
    }
}

public sealed record CaddyDomainRouteQuery(
    Guid TenantId,
    Guid DomainId,
    string NormalizedHostName,
    string CorrelationId,
    long? FencingToken = null,
    Guid? CertificateId = null,
    Guid? OperationId = null)
{
    public void Validate()
    {
        EdgeCertificateHostName.Validate(NormalizedHostName);
        if (TenantId == Guid.Empty || DomainId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and domain identifiers are required.");
        }
        if (FencingToken.HasValue && FencingToken.Value < 1)
        {
            throw new ArgumentException("FencingToken must be positive when supplied.");
        }
    }
}

public sealed record CaddyDomainRouteState(
    bool Exists,
    bool MatchesDesiredState,
    string NormalizedHostName,
    string? EdgeIdentifier,
    string? Upstream,
    string? FailureCode,
    string? SafeFailureReason)
{
    public static CaddyDomainRouteState Missing(string host) =>
        new(false, false, host, null, null, null, null);
}

public sealed record CaddyDomainRouteResult(
    bool Succeeded,
    bool Mutated,
    bool Retryable,
    CaddyDomainRouteState State,
    string? EdgeIdentifier,
    string? FailureCode,
    string? SafeFailureReason)
{
    public static CaddyDomainRouteResult Success(CaddyDomainRouteState state, bool mutated, string? edgeIdentifier) =>
        new(true, mutated, false, state, edgeIdentifier, null, null);

    public static CaddyDomainRouteResult Failure(
        CaddyDomainRouteState state,
        string failureCode,
        string safeFailureReason,
        bool retryable) =>
        new(false, false, retryable, state, null, failureCode, safeFailureReason);
}

public sealed record EdgeCertificateProbeRequest(
    Guid TenantId,
    Guid DomainId,
    Guid CertificateId,
    Guid OperationId,
    string NormalizedHostName,
    string? ExpectedThumbprint,
    string? ExpectedFingerprint,
    string? ExpectedSerialNumber,
    string? ExpectedIssuer,
    DateTime? ExpectedNotAfterUtc,
    string CorrelationId)
{
    public void Validate()
    {
        EdgeCertificateHostName.Validate(NormalizedHostName);
        if (TenantId == Guid.Empty || DomainId == Guid.Empty || CertificateId == Guid.Empty || OperationId == Guid.Empty)
        {
            throw new ArgumentException("Tenant, domain, certificate, and operation identifiers are required.");
        }
    }
}

public sealed record EdgeCertificateProbeResult(
    bool Succeeded,
    bool Retryable,
    string NormalizedHostName,
    string? FailureCode,
    string? SafeFailureReason,
    EdgeCertificateIdentity? Certificate);

public sealed record EdgeCertificateIdentity(
    string Subject,
    string Issuer,
    IReadOnlyCollection<string> SubjectAlternativeNames,
    string Thumbprint,
    string Fingerprint,
    string SerialNumber,
    string SignatureAlgorithm,
    DateTime NotBeforeUtc,
    DateTime NotAfterUtc)
{
    public IReadOnlyDictionary<string, string> ToSafeMetadata() =>
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["issuer"] = Issuer,
            ["thumbprint"] = Thumbprint,
            ["fingerprint"] = Fingerprint,
            ["serial"] = SerialNumber,
            ["notBeforeUtc"] = NotBeforeUtc.ToString("O"),
            ["notAfterUtc"] = NotAfterUtc.ToString("O"),
        });
}

public static class EdgeCertificateHostName
{
    private static readonly string[] ReservedExactHosts =
    [
        "localhost",
        "localhost.localdomain",
        "127.0.0.1",
        "::1",
        "0.0.0.0",
        "169.254.169.254",
    ];

    public static void Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Hostname is required.");
        }

        var host = value.Trim().ToLowerInvariant();
        if (host.Length > 253 ||
            host.Contains(' ') ||
            host.Contains('/') ||
            host.Contains('\\') ||
            host.Contains(':') ||
            host.StartsWith('.') ||
            host.EndsWith('.') ||
            host.StartsWith("*.") ||
            Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            throw new ArgumentException("Hostname must be a normalized DNS host name.");
        }

        if (ReservedExactHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Hostname is reserved for internal use.");
        }
    }
}
