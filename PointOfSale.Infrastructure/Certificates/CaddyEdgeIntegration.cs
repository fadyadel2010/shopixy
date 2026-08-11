using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Certificates;

namespace PointOfSale.Infrastructure.Certificates;

public sealed class CaddyEdgeOptions
{
    public const string SectionName = "CaddyEdge";

    public bool Enabled { get; set; }
    public string EdgeCluster { get; set; } = "staging-caddy";
    public string ManagedRoutesDirectory { get; set; } = string.Empty;
    public string StorefrontRoot { get; set; } = string.Empty;
    public string ApiUpstream { get; set; } = string.Empty;
    public string SecurityHeadersSnippet { get; set; } = "shopixy_security_headers";
    public bool AllowLocalTrustedUpstream { get; set; }
    public string LogRoot { get; set; } = string.Empty;
    public string? ValidateCommand { get; set; }
    public string? ReloadCommand { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 15;
    public int ProbeTimeoutSeconds { get; set; } = 10;

    public TimeSpan CommandTimeout => TimeSpan.FromSeconds(Math.Clamp(CommandTimeoutSeconds, 1, 60));
    public TimeSpan ProbeTimeout => TimeSpan.FromSeconds(Math.Clamp(ProbeTimeoutSeconds, 2, 30));
}

public sealed class CaddyEdgeOptionsValidator : IValidateOptions<CaddyEdgeOptions>
{
    public ValidateOptionsResult Validate(string? name, CaddyEdgeOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        RequirePath(options.ManagedRoutesDirectory, "CaddyEdge:ManagedRoutesDirectory", errors);
        RequirePath(options.StorefrontRoot, "CaddyEdge:StorefrontRoot", errors);
        RequirePath(options.LogRoot, "CaddyEdge:LogRoot", errors);

        if (string.IsNullOrWhiteSpace(options.ValidateCommand))
        {
            errors.Add("CaddyEdge:ValidateCommand is required when Caddy edge integration is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ReloadCommand))
        {
            errors.Add("CaddyEdge:ReloadCommand is required when Caddy edge integration is enabled.");
        }

        if (!IsSafeTrustedUpstream(options.ApiUpstream, options.AllowLocalTrustedUpstream))
        {
            errors.Add("CaddyEdge:ApiUpstream must be a trusted host:port upstream without a scheme or path.");
        }

        if (string.IsNullOrWhiteSpace(options.SecurityHeadersSnippet) ||
            options.SecurityHeadersSnippet.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
        {
            errors.Add("CaddyEdge:SecurityHeadersSnippet must contain only ASCII letters, digits, and underscores.");
        }

        if (options.CommandTimeoutSeconds is <= 0 or > 60)
        {
            errors.Add("CaddyEdge:CommandTimeoutSeconds must be between 1 and 60.");
        }

        if (options.ProbeTimeoutSeconds is < 2 or > 30)
        {
            errors.Add("CaddyEdge:ProbeTimeoutSeconds must be between 2 and 30.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void RequirePath(string value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is required when CaddyEdge is enabled.");
        }
    }

    public static bool IsSafeTrustedUpstream(string value, bool allowLocalTrustedUpstream = false)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            !int.TryParse(parts[1], out var port) ||
            port is <= 0 or > 65535)
        {
            return false;
        }

        var host = parts[0].Trim().ToLowerInvariant();
        if (host.Length > 253 ||
            host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) ||
            host.StartsWith('.') ||
            host.EndsWith('.') ||
            host.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }
        var localOrPrivate = host is "localhost" or "127.0.0.1" or "0.0.0.0" or "::1" ||
            host.StartsWith("10.", StringComparison.Ordinal) ||
            host.StartsWith("192.168.", StringComparison.Ordinal) ||
            host.StartsWith("172.16.", StringComparison.Ordinal) ||
            host.StartsWith("172.17.", StringComparison.Ordinal) ||
            host.StartsWith("172.18.", StringComparison.Ordinal) ||
            host.StartsWith("172.19.", StringComparison.Ordinal) ||
            host.StartsWith("172.2", StringComparison.Ordinal) ||
            host.StartsWith("172.30.", StringComparison.Ordinal) ||
            host.StartsWith("172.31.", StringComparison.Ordinal) ||
            host.StartsWith("169.254.", StringComparison.Ordinal);

        return allowLocalTrustedUpstream || !localOrPrivate;
    }
}

public sealed class CaddyfileEdgeClient : ICaddyEdgeClient
{
    private readonly IOptionsMonitor<CaddyEdgeOptions> _options;
    private readonly ILogger<CaddyfileEdgeClient> _logger;

    public CaddyfileEdgeClient(
        IOptionsMonitor<CaddyEdgeOptions> options,
        ILogger<CaddyfileEdgeClient> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<CaddyDomainRouteResult> EnsureDomainRouteAsync(
        CaddyDomainRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        var options = _options.CurrentValue;
        var disabled = ValidateEnabled(options, request.NormalizedHostName);
        if (disabled is not null)
        {
            return disabled;
        }

        var routePath = GetRoutePath(options, request.NormalizedHostName);
        await using var routeLock = await AcquireRouteLockAsync(routePath, options.CommandTimeout, cancellationToken);
        var desired = BuildManagedRoute(request, options);
        var current = File.Exists(routePath)
            ? await File.ReadAllTextAsync(routePath, cancellationToken)
            : null;

        var currentFence = ReadManagedLong(current, "FencingToken");
        if (currentFence.HasValue && currentFence.Value > request.FencingToken)
        {
            return CaddyDomainRouteResult.Failure(
                new CaddyDomainRouteState(true, false, request.NormalizedHostName, null, options.ApiUpstream, "CADDY_STALE_FENCE", "A newer edge operation already owns this route."),
                "CADDY_STALE_FENCE",
                "A newer edge operation already owns this route.",
                retryable: false);
        }

        if (string.Equals(current, desired, StringComparison.Ordinal))
        {
            var edgeIdentifier = BuildEdgeIdentifier(options, request.NormalizedHostName);
            var state = new CaddyDomainRouteState(true, true, request.NormalizedHostName, edgeIdentifier, options.ApiUpstream, null, null);
            return CaddyDomainRouteResult.Success(state, mutated: false, edgeIdentifier);
        }

        Directory.CreateDirectory(options.ManagedRoutesDirectory);
        var backup = current;
        await WriteRouteAtomicallyAsync(routePath, desired, cancellationToken);

        var commandResult = await RunCaddyCommandAsync(options.ValidateCommand, options.CommandTimeout, cancellationToken);
        if (!commandResult.Succeeded)
        {
            await RestoreRouteAsync(routePath, backup, cancellationToken);
            return CaddyDomainRouteResult.Failure(
                new CaddyDomainRouteState(backup is not null, false, request.NormalizedHostName, null, options.ApiUpstream, commandResult.FailureCode, commandResult.SafeFailureReason),
                commandResult.FailureCode,
                commandResult.SafeFailureReason,
                retryable: commandResult.Retryable);
        }

        commandResult = await RunCaddyCommandAsync(options.ReloadCommand, options.CommandTimeout, cancellationToken);
        if (!commandResult.Succeeded)
        {
            await RestoreRouteAsync(routePath, backup, cancellationToken);
            _ = await RunCaddyCommandAsync(options.ReloadCommand, options.CommandTimeout, CancellationToken.None);
            return CaddyDomainRouteResult.Failure(
                new CaddyDomainRouteState(backup is not null, false, request.NormalizedHostName, null, options.ApiUpstream, commandResult.FailureCode, commandResult.SafeFailureReason),
                commandResult.FailureCode,
                commandResult.SafeFailureReason,
                retryable: commandResult.Retryable);
        }

        var acceptedIdentifier = BuildEdgeIdentifier(options, request.NormalizedHostName);
        var accepted = new CaddyDomainRouteState(true, true, request.NormalizedHostName, acceptedIdentifier, options.ApiUpstream, null, null);
        _logger.LogInformation(
            "Caddy managed route reconciled | TenantId: {TenantId} | DomainId: {DomainId} | CertificateId: {CertificateId} | OperationId: {OperationId} | Host: {Host}",
            request.TenantId,
            request.DomainId,
            request.CertificateId,
            request.OperationId,
            request.NormalizedHostName);

        return CaddyDomainRouteResult.Success(accepted, mutated: true, edgeIdentifier: acceptedIdentifier);
    }

    public Task<CaddyDomainRouteState> GetDomainRouteStateAsync(
        CaddyDomainRouteQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return Task.FromResult(CaddyDomainRouteState.Missing(query.NormalizedHostName));
        }

        var routePath = GetRoutePath(options, query.NormalizedHostName);
        if (!File.Exists(routePath))
        {
            return Task.FromResult(CaddyDomainRouteState.Missing(query.NormalizedHostName));
        }

        var content = File.ReadAllText(routePath);
        var matches = IsManagedRouteContent(content, query.NormalizedHostName, options.ApiUpstream);
        return Task.FromResult(new CaddyDomainRouteState(
            true,
            matches,
            query.NormalizedHostName,
            BuildEdgeIdentifier(options, query.NormalizedHostName),
            options.ApiUpstream,
            matches ? null : "CADDY_ROUTE_DRIFT",
            matches ? null : "The managed Caddy route differs from the admitted route."));
    }

    public async Task<CaddyDomainRouteResult> RemoveDomainRouteAsync(
        CaddyDomainRouteQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();
        var options = _options.CurrentValue;
        var disabled = ValidateEnabled(options, query.NormalizedHostName);
        if (disabled is not null)
        {
            return disabled;
        }

        var routePath = GetRoutePath(options, query.NormalizedHostName);
        await using var routeLock = await AcquireRouteLockAsync(routePath, options.CommandTimeout, cancellationToken);
        if (!File.Exists(routePath))
        {
            return CaddyDomainRouteResult.Success(CaddyDomainRouteState.Missing(query.NormalizedHostName), mutated: false, edgeIdentifier: null);
        }


        var current = await File.ReadAllTextAsync(routePath, cancellationToken);
        var currentFence = ReadManagedLong(current, "FencingToken");
        if (query.FencingToken.HasValue && currentFence.HasValue && currentFence.Value > query.FencingToken.Value)
        {
            return CaddyDomainRouteResult.Failure(
                new CaddyDomainRouteState(true, false, query.NormalizedHostName, null, options.ApiUpstream, "CADDY_STALE_FENCE", "A newer edge operation already owns this route."),
                "CADDY_STALE_FENCE",
                "A newer edge operation already owns this route.",
                retryable: false);
        }

        var currentCertificate = ReadManagedGuid(current, "CertificateId");
        if (query.CertificateId.HasValue && currentCertificate.HasValue && currentCertificate.Value != query.CertificateId.Value)
        {
            var preserved = new CaddyDomainRouteState(true, true, query.NormalizedHostName, BuildEdgeIdentifier(options, query.NormalizedHostName), options.ApiUpstream, null, null);
            return CaddyDomainRouteResult.Success(preserved, mutated: false, preserved.EdgeIdentifier);
        }

        var backup = await File.ReadAllTextAsync(routePath, cancellationToken);
        File.Delete(routePath);
        var validation = await RunCaddyCommandAsync(options.ValidateCommand, options.CommandTimeout, cancellationToken);
        if (!validation.Succeeded)
        {
            await WriteRouteAtomicallyAsync(routePath, backup, cancellationToken);
            return CaddyDomainRouteResult.Failure(
                new CaddyDomainRouteState(true, true, query.NormalizedHostName, BuildEdgeIdentifier(options, query.NormalizedHostName), options.ApiUpstream, validation.FailureCode, validation.SafeFailureReason),
                validation.FailureCode,
                validation.SafeFailureReason,
                validation.Retryable);
        }

        var commandResult = await RunCaddyCommandAsync(options.ReloadCommand, options.CommandTimeout, cancellationToken);
        if (!commandResult.Succeeded)
        {
            await WriteRouteAtomicallyAsync(routePath, backup, cancellationToken);
            _ = await RunCaddyCommandAsync(options.ReloadCommand, options.CommandTimeout, CancellationToken.None);
            return CaddyDomainRouteResult.Failure(
                new CaddyDomainRouteState(true, true, query.NormalizedHostName, BuildEdgeIdentifier(options, query.NormalizedHostName), options.ApiUpstream, commandResult.FailureCode, commandResult.SafeFailureReason),
                commandResult.FailureCode,
                commandResult.SafeFailureReason,
                commandResult.Retryable);
        }

        return CaddyDomainRouteResult.Success(CaddyDomainRouteState.Missing(query.NormalizedHostName), mutated: true, edgeIdentifier: BuildEdgeIdentifier(options, query.NormalizedHostName));
    }

    private static CaddyDomainRouteResult? ValidateEnabled(CaddyEdgeOptions options, string host)
    {
        if (!options.Enabled)
        {
            return CaddyDomainRouteResult.Failure(
                CaddyDomainRouteState.Missing(host),
                "CADDY_EDGE_DISABLED",
                "Caddy edge integration is not enabled.",
                retryable: false);
        }

        var validation = new CaddyEdgeOptionsValidator().Validate(null, options);
        if (!validation.Succeeded)
        {
            return CaddyDomainRouteResult.Failure(
                CaddyDomainRouteState.Missing(host),
                "CADDY_EDGE_CONFIGURATION_INVALID",
                "Caddy edge integration configuration is invalid.",
                retryable: false);
        }

        return null;
    }

    private static string GetRoutePath(CaddyEdgeOptions options, string host)
    {
        var safeFileName = host.Replace("*", "_", StringComparison.Ordinal).Replace(":", "_", StringComparison.Ordinal);
        var directory = Path.GetFullPath(options.ManagedRoutesDirectory);
        var path = Path.GetFullPath(Path.Combine(directory, $"{safeFileName}.caddy"));
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Managed Caddy route path escaped its configured directory.");
        }

        return path;
    }

    private static string BuildEdgeIdentifier(CaddyEdgeOptions options, string host)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host))).ToLowerInvariant();
        return $"{options.EdgeCluster}:route:{digest[..24]}";
    }

    private static bool IsManagedRouteContent(string content, string host, string upstream) =>
        content.StartsWith("# Managed by Shopixy DOMAIN-5.5.", StringComparison.Ordinal) &&
        content.Contains($"{host} {{", StringComparison.Ordinal) &&
        content.Contains($"reverse_proxy {upstream} {{", StringComparison.Ordinal) &&
        !content.Contains("tls internal", StringComparison.OrdinalIgnoreCase) &&
        !content.Contains("tls off", StringComparison.OrdinalIgnoreCase);

    private static async Task WriteRouteAtomicallyAsync(string routePath, string content, CancellationToken cancellationToken)
    {
        var temporaryPath = routePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            File.Move(temporaryPath, routePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static Task RestoreRouteAsync(string routePath, string? backup, CancellationToken cancellationToken)
    {
        if (backup is null)
        {
            if (File.Exists(routePath))
            {
                File.Delete(routePath);
            }

            return Task.CompletedTask;
        }

        return WriteRouteAtomicallyAsync(routePath, backup, cancellationToken);
    }

    private static string BuildManagedRoute(CaddyDomainRouteRequest request, CaddyEdgeOptions options)
    {
        var host = request.NormalizedHostName;
        EdgeCertificateHostName.Validate(host);

        return $$"""
        # Managed by Shopixy DOMAIN-5.5. Do not edit by hand.
        # TenantId={{request.TenantId}}
        # DomainId={{request.DomainId}}
        # CertificateId={{request.CertificateId}}
        # OperationId={{request.OperationId}}
        # FencingToken={{request.FencingToken}}
        {{host}} {
            import {{options.SecurityHeadersSnippet}}
            encode zstd gzip
            root * {{EscapeCaddyToken(options.StorefrontRoot)}}

            @backend path /api/* /hubs/* /health/*
            handle @backend {
                reverse_proxy {{options.ApiUpstream}} {
                    header_up Host {host}
                    header_up X-Forwarded-Proto {scheme}
                    header_up X-Forwarded-For {remote_host}
                }
            }

            handle {
                try_files {path} /index.html
                file_server
            }

            log {
                output file {{EscapeCaddyToken(Path.Combine(options.LogRoot, "custom-domains-access.log"))}}
                format json
            }
        }

        """;
    }

    private static async Task<FileStream> AcquireRouteLockAsync(
        string routePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var lockPath = routePath + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var deadline = DateTime.UtcNow.Add(timeout);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private static long? ReadManagedLong(string? content, string name)
    {
        var value = ReadManagedValue(content, name);
        return long.TryParse(value, out var parsed) ? parsed : null;
    }

    private static Guid? ReadManagedGuid(string? content, string name)
    {
        var value = ReadManagedValue(content, name);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ReadManagedValue(string? content, string name)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        var prefix = $"# {name}=";
        return content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?
            [prefix.Length..]
            .Trim();
    }

    private static string EscapeCaddyToken(string value)
    {
        if (value.Contains('\n') || value.Contains('\r') || value.Contains('{') || value.Contains('}'))
        {
            throw new InvalidOperationException("Caddy path contains unsupported characters.");
        }

        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }

    internal static async Task<CaddyCommandResult> RunCaddyCommandAsync(
        string? command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return CaddyCommandResult.Failure(
                "CADDY_COMMAND_NOT_CONFIGURED",
                "The required Caddy command is not configured.",
                retryable: false);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c {command}")
            : new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"");

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return CaddyCommandResult.Failure("CADDY_COMMAND_START_FAILED", "Caddy command could not be started.", retryable: true);
        }

        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return CaddyCommandResult.Failure("CADDY_COMMAND_TIMEOUT", "Caddy command timed out.", retryable: true);
        }

        return process.ExitCode == 0
            ? CaddyCommandResult.Success()
            : CaddyCommandResult.Failure("CADDY_COMMAND_FAILED", "Caddy command failed.", retryable: true);
    }

    internal sealed record CaddyCommandResult(bool Succeeded, string FailureCode, string SafeFailureReason, bool Retryable)
    {
        public static CaddyCommandResult Success() => new(true, string.Empty, string.Empty, false);
        public static CaddyCommandResult Failure(string code, string reason, bool retryable) => new(false, code, reason, retryable);
    }
}

public sealed class ExternalSniCertificateProbe : IEdgeCertificateProbe
{
    private readonly IOptionsMonitor<CaddyEdgeOptions> _options;

    public ExternalSniCertificateProbe(IOptionsMonitor<CaddyEdgeOptions> options)
    {
        _options = options;
    }

    public async Task<EdgeCertificateProbeResult> ProbeAsync(
        EdgeCertificateProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        var host = request.NormalizedHostName;
        X509Certificate2? observedCertificate = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.CurrentValue.ProbeTimeout);

            using var tcp = new TcpClient();
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, cts.Token);
            if (addresses.Length == 0 || addresses.Any(address => !Services.Dns.PublicEndpointHttpMessageHandler.IsPublicAddress(address)))
            {
                return Failure(request, "SNI_NON_PUBLIC_ENDPOINT", "The hostname did not resolve exclusively to public edge addresses.", retryable: false);
            }

            Exception? lastConnectError = null;
            foreach (var address in addresses)
            {
                try
                {
                    await tcp.ConnectAsync(address, 443, cts.Token);
                    lastConnectError = null;
                    break;
                }
                catch (SocketException ex)
                {
                    lastConnectError = ex;
                }
            }

            if (!tcp.Connected)
            {
                throw new SocketException(lastConnectError is SocketException socketError
                    ? socketError.ErrorCode
                    : (int)SocketError.HostUnreachable);
            }
            await using var networkStream = tcp.GetStream();
            using var ssl = new SslStream(
                networkStream,
                leaveInnerStreamOpen: false,
                (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (certificate is not null)
                    {
                        observedCertificate = new X509Certificate2(certificate);
                    }

                    return sslPolicyErrors == SslPolicyErrors.None;
                });

            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.Online,
            }, cts.Token);

            if (observedCertificate is null)
            {
                return Failure(request, "SNI_CERTIFICATE_MISSING", "The edge did not return a certificate.", retryable: true);
            }

            var identity = MapIdentity(observedCertificate);
            var mismatch = ValidateIdentity(request, identity, DateTime.UtcNow);
            if (mismatch is not null)
            {
                return Failure(request, mismatch.Value.Code, mismatch.Value.Reason, retryable: false, identity);
            }

            return new EdgeCertificateProbeResult(true, false, host, null, null, identity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure(request, "SNI_PROBE_TIMEOUT", "The external HTTPS/SNI probe timed out.", retryable: true);
        }
        catch (SocketException)
        {
            return Failure(request, "SNI_DNS_OR_CONNECT_FAILURE", "The hostname was not reachable through the public HTTPS edge.", retryable: true);
        }
        catch (AuthenticationException)
        {
            return Failure(request, "SNI_TLS_VALIDATION_FAILED", "TLS validation failed for the public hostname.", retryable: true);
        }
        catch (IOException)
        {
            return Failure(request, "SNI_NETWORK_FAILURE", "The external HTTPS/SNI probe failed.", retryable: true);
        }
        finally
        {
            observedCertificate?.Dispose();
        }
    }

    private static EdgeCertificateProbeResult Failure(
        EdgeCertificateProbeRequest request,
        string code,
        string reason,
        bool retryable,
        EdgeCertificateIdentity? identity = null) =>
        new(false, retryable, request.NormalizedHostName, code, reason, identity);

    internal static EdgeCertificateIdentity MapIdentity(X509Certificate2 certificate)
    {
        var sans = ReadDnsSubjectAlternativeNames(certificate);
        return new EdgeCertificateIdentity(
            certificate.Subject,
            certificate.Issuer,
            Array.AsReadOnly(sans.ToArray()),
            NormalizeHex(certificate.Thumbprint),
            NormalizeHex(SHA256.HashData(certificate.RawData)),
            certificate.SerialNumber,
            certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value ?? string.Empty,
            certificate.NotBefore.ToUniversalTime(),
            certificate.NotAfter.ToUniversalTime());
    }

    internal static (string Code, string Reason)? ValidateIdentity(
        EdgeCertificateProbeRequest request,
        EdgeCertificateIdentity identity,
        DateTime nowUtc)
    {
        if (identity.NotBeforeUtc > nowUtc || identity.NotAfterUtc <= nowUtc)
        {
            return ("SNI_CERTIFICATE_NOT_CURRENTLY_VALID", "The edge certificate is not currently valid.");
        }

        if (!identity.SubjectAlternativeNames.Any(san => HostMatches(request.NormalizedHostName, san)))
        {
            return ("SNI_CERTIFICATE_SAN_MISMATCH", "The edge certificate SAN does not contain the requested hostname.");
        }

        if (!MatchesExpected(request.ExpectedThumbprint, identity.Thumbprint) ||
            !MatchesExpected(request.ExpectedFingerprint, identity.Fingerprint) ||
            !MatchesExpected(request.ExpectedSerialNumber, identity.SerialNumber) ||
            !MatchesExpected(request.ExpectedIssuer, identity.Issuer))
        {
            return ("SNI_CERTIFICATE_IDENTITY_MISMATCH", "The edge certificate does not match expected certificate metadata.");
        }

        if (request.ExpectedNotAfterUtc.HasValue &&
            Math.Abs((identity.NotAfterUtc - request.ExpectedNotAfterUtc.Value).TotalMinutes) > 5)
        {
            return ("SNI_CERTIFICATE_EXPIRY_MISMATCH", "The edge certificate expiry does not match expected certificate metadata.");
        }

        return null;
    }

    private static bool MatchesExpected(string? expected, string observed)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(NormalizeComparable(expected), NormalizeComparable(observed), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HostMatches(string host, string san)
    {
        if (string.Equals(host, san, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!san.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = san[1..];
        return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
               host.Count(c => c == '.') == san.Count(c => c == '.');
    }

    private static IReadOnlyCollection<string> ReadDnsSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            if (extension is X509SubjectAlternativeNameExtension sanExtension)
            {
                foreach (var dnsName in sanExtension.EnumerateDnsNames())
                {
                    names.Add(dnsName.Trim().ToLowerInvariant());
                }

                continue;
            }

            var decoded = new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical);
            foreach (var dnsName in decoded.EnumerateDnsNames())
            {
                names.Add(dnsName.Trim().ToLowerInvariant());
            }
        }

        return names;
    }

    private static string NormalizeHex(byte[] value) => Convert.ToHexString(value).ToUpperInvariant();
    private static string NormalizeHex(string? value) => NormalizeComparable(value).ToUpperInvariant();
    private static string NormalizeComparable(string? value) =>
        (value ?? string.Empty).Replace(":", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
}

public sealed class CaddyEdgeHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<CaddyEdgeOptions> _options;

    public CaddyEdgeHealthCheck(IOptionsMonitor<CaddyEdgeOptions> options)
    {
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return HealthCheckResult.Degraded("Caddy edge integration is disabled.");
        }

        var validation = new CaddyEdgeOptionsValidator().Validate(null, options);
        if (validation.Failed)
        {
            return HealthCheckResult.Unhealthy(
                "Caddy edge configuration is invalid: " + string.Join("; ", validation.Failures));
        }

        if (!Directory.Exists(options.ManagedRoutesDirectory) ||
            !Directory.Exists(options.StorefrontRoot) ||
            !Directory.Exists(options.LogRoot))
        {
            return HealthCheckResult.Unhealthy("One or more required Caddy edge directories are not reachable.");
        }

        var command = await CaddyfileEdgeClient.RunCaddyCommandAsync(
            options.ValidateCommand,
            options.CommandTimeout,
            cancellationToken);
        return command.Succeeded
            ? HealthCheckResult.Healthy("Caddy configuration validation completed successfully.")
            : HealthCheckResult.Unhealthy(command.SafeFailureReason);
    }
}

public static class CaddyEdgeServiceCollectionExtensions
{
    public static IServiceCollection AddCaddyEdgeIntegration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CaddyEdgeOptions>()
            .Bind(configuration.GetSection(CaddyEdgeOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CaddyEdgeOptions>, CaddyEdgeOptionsValidator>());
        services.TryAddScoped<ICaddyEdgeClient, CaddyfileEdgeClient>();
        services.TryAddScoped<IEdgeCertificateProbe, ExternalSniCertificateProbe>();
        services.TryAddSingleton<CaddyEdgeHealthCheck>();
        return services;
    }
}
