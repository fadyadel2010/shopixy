using System.Diagnostics;
using System.Net;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Configuration;

namespace PointOfSale.Infrastructure.Services.Dns;

/// <summary>
/// DNS resolver implementation using DnsClient.NET library.
///
/// DOMAIN-4.1 Section 5.1 — Layered Abstraction (Constitutional Freeze).
/// DOMAIN-4.1 Section 6.1 — Provider Replaceability.
/// DOMAIN-4.2A — DNS Resolver Foundation Implementation.
///
/// This implementation wraps DnsClient.NET and provides:
/// - TXT record lookups for DNS verification
/// - CNAME record lookups for DNS verification
/// - HTTP file verification abstraction
/// - Error normalization to canonical error codes
/// - Optional in-memory caching (positive and negative)
/// - Structured logging with latency tracking
/// - Cancellation token support
///
/// Constitutional Law #9: No provider-specific code may exist in application
/// layer. This implementation lives in Infrastructure and is never referenced
/// directly by business logic.
///
/// Constitutional Law #26: Caching is transparent to verification engine.
/// IDnsResolver handles cache reads/writes internally.
/// </summary>
public class DnsClientResolver : IDnsResolver
{
    private readonly ILookupClient _lookupClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DnsClientResolver> _logger;
    private readonly DnsResolverOptions _options;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public DnsClientResolver(
        ILookupClient lookupClient,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<DnsClientResolver> logger,
        IOptions<DnsResolverOptions> options)
    {
        _lookupClient = lookupClient ?? throw new ArgumentNullException(nameof(lookupClient));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // DOMAIN-4.1 Section 15.5: Max concurrent queries per resolver instance
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentQueries, _options.MaxConcurrentQueries);
    }

    /// <inheritdoc />
    public async Task<DnsLookupResult> ResolveTxtAsync(string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new ArgumentException("Hostname cannot be null or empty.", nameof(hostname));
        }

        var cacheKey = $"TXT:{hostname}";
        
        // Check cache if enabled
        if (_options.EnableCaching && _cache.TryGetValue<DnsLookupResult>(cacheKey, out var cachedResult))
        {
            if (_options.EnableDetailedLogging)
            {
                _logger.LogInformation(
                    "DNS TXT lookup cache hit for hostname: {Hostname}",
                    hostname);
            }
            return cachedResult!;
        }

        // Enforce concurrency limit
        await _concurrencyLimiter.WaitAsync(ct);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var resolverEndpoint = GetResolverEndpoint();

            try
            {
                // DOMAIN-4.1 Section 15.3: DNS query timeout 30 seconds
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.Timeout));

                if (_options.EnableDetailedLogging)
                {
                    _logger.LogInformation(
                        "Resolving DNS TXT record for hostname: {Hostname} using resolver: {Resolver}",
                        hostname,
                        resolverEndpoint);
                }

                var response = await _lookupClient.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, timeoutCts.Token);
                stopwatch.Stop();

                // Log query latency
                _logger.LogInformation(
                    "DNS TXT query completed for {Hostname} in {Latency}ms with status: {Status}",
                    hostname,
                    stopwatch.ElapsedMilliseconds,
                    response.Header.ResponseCode);

                var responseCode = MapDnsResponseCode(response.Header.ResponseCode.ToString());

                // Handle DNS response codes
                if (responseCode == "NOERROR")
                {
                    var txtRecords = response.Answers.TxtRecords().ToArray();
                    if (txtRecords.Length > 0)
                    {
                        var values = txtRecords
                            .Select(record => string.Join("", record.Text))
                            .ToArray();
                        var ttl = txtRecords.Min(record => record.TimeToLive);

                        var successResult = DnsLookupResult.CreateSuccess(
                            values[0],
                            resolverEndpoint,
                            stopwatch.Elapsed,
                            ttl,
                            values);

                        CacheResult(cacheKey, successResult, ttl);
                        return successResult;
                    }

                    // No TXT record found in answer section (NOERROR but empty)
                    var missingResult = DnsLookupResult.CreateFailure(
                        "NXDOMAIN",
                        resolverEndpoint,
                        stopwatch.Elapsed);

                    CacheResult(cacheKey, missingResult, _options.NegativeCacheTtl, isNegative: true);
                    return missingResult;
                }
                else if (responseCode == "NXDOMAIN")
                {
                    _logger.LogWarning(
                        "DNS TXT record not found (NXDOMAIN) for hostname: {Hostname}",
                        hostname);

                    var nxdomainResult = DnsLookupResult.CreateFailure(
                        "NXDOMAIN",
                        resolverEndpoint,
                        stopwatch.Elapsed);

                    CacheResult(cacheKey, nxdomainResult, _options.NegativeCacheTtl, isNegative: true);
                    return nxdomainResult;
                }
                else if (responseCode == "SERVFAIL")
                {
                    _logger.LogError(
                        "DNS server failure (SERVFAIL) for hostname: {Hostname}",
                        hostname);

                    return DnsLookupResult.CreateFailure(
                        "SERVFAIL",
                        resolverEndpoint,
                        stopwatch.Elapsed);
                }
                else if (responseCode == "REFUSED")
                {
                    _logger.LogError(
                        "DNS query refused for hostname: {Hostname}",
                        hostname);

                    return DnsLookupResult.CreateFailure(
                        "REFUSED",
                        resolverEndpoint,
                        stopwatch.Elapsed);
                }
                else
                {
                    _logger.LogError(
                        "DNS query returned unexpected response code: {ResponseCode} for hostname: {Hostname}",
                        response.Header.ResponseCode,
                        hostname);

                    return DnsLookupResult.CreateFailure(
                        "UNKNOWN",
                        resolverEndpoint,
                        stopwatch.Elapsed);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Graceful cancellation requested by caller
                stopwatch.Stop();
                _logger.LogWarning(
                    "DNS TXT query cancelled for hostname: {Hostname}",
                    hostname);
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout
                stopwatch.Stop();
                _logger.LogError(
                    "DNS TXT query timeout ({Timeout}s) for hostname: {Hostname}",
                    _options.Timeout,
                    hostname);

                return DnsLookupResult.CreateFailure(
                    "TIMEOUT",
                    resolverEndpoint,
                    stopwatch.Elapsed);
            }
            catch (DnsResponseException ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "DNS query exception for hostname: {Hostname}",
                    hostname);

                return DnsLookupResult.CreateFailure(
                    MapDnsException(ex),
                    resolverEndpoint,
                    stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "Unexpected error during DNS TXT query for hostname: {Hostname}",
                    hostname);

                return DnsLookupResult.CreateFailure(
                    "NETWORK",
                    resolverEndpoint,
                    stopwatch.Elapsed);
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DnsLookupResult> ResolveCnameAsync(string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new ArgumentException("Hostname cannot be null or empty.", nameof(hostname));
        }

        var cacheKey = $"CNAME:{hostname}";
        
        // Check cache if enabled
        if (_options.EnableCaching && _cache.TryGetValue<DnsLookupResult>(cacheKey, out var cachedResult))
        {
            if (_options.EnableDetailedLogging)
            {
                _logger.LogInformation(
                    "DNS CNAME lookup cache hit for hostname: {Hostname}",
                    hostname);
            }
            return cachedResult!;
        }

        // Enforce concurrency limit
        await _concurrencyLimiter.WaitAsync(ct);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var resolverEndpoint = GetResolverEndpoint();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.Timeout));

                if (_options.EnableDetailedLogging)
                {
                    _logger.LogInformation(
                        "Resolving DNS CNAME record for hostname: {Hostname} using resolver: {Resolver}",
                        hostname,
                        resolverEndpoint);
                }

                var response = await _lookupClient.QueryAsync(hostname, QueryType.CNAME, QueryClass.IN, timeoutCts.Token);
                stopwatch.Stop();

                _logger.LogInformation(
                    "DNS CNAME query completed for {Hostname} in {Latency}ms with status: {Status}",
                    hostname,
                    stopwatch.ElapsedMilliseconds,
                    response.Header.ResponseCode);

                var responseCode = MapDnsResponseCode(response.Header.ResponseCode.ToString());

                if (responseCode == "NOERROR")
                {
                    var cnameRecord = response.Answers.CnameRecords().FirstOrDefault();
                    if (cnameRecord != null)
                    {
                        var value = cnameRecord.CanonicalName.Value.TrimEnd('.');
                        var ttl = cnameRecord.TimeToLive;

                        var successResult = DnsLookupResult.CreateSuccess(
                            value,
                            resolverEndpoint,
                            stopwatch.Elapsed,
                            ttl);

                        CacheResult(cacheKey, successResult, ttl);
                        return successResult;
                    }

                    var missingResult = DnsLookupResult.CreateFailure(
                        "NXDOMAIN",
                        resolverEndpoint,
                        stopwatch.Elapsed);

                    CacheResult(cacheKey, missingResult, _options.NegativeCacheTtl, isNegative: true);
                    return missingResult;
                }
                else if (responseCode == "NXDOMAIN")
                {
                    _logger.LogWarning(
                        "DNS CNAME record not found (NXDOMAIN) for hostname: {Hostname}",
                        hostname);

                    var nxdomainResult = DnsLookupResult.CreateFailure(
                        "NXDOMAIN",
                        resolverEndpoint,
                        stopwatch.Elapsed);

                    CacheResult(cacheKey, nxdomainResult, _options.NegativeCacheTtl, isNegative: true);
                    return nxdomainResult;
                }
                else if (responseCode == "SERVFAIL")
                {
                    return DnsLookupResult.CreateFailure(
                        "SERVFAIL",
                        resolverEndpoint,
                        stopwatch.Elapsed);
                }
                else if (responseCode == "REFUSED")
                {
                    return DnsLookupResult.CreateFailure(
                        "REFUSED",
                        resolverEndpoint,
                        stopwatch.Elapsed);
                }
                else
                {
                    return DnsLookupResult.CreateFailure(
                        "UNKNOWN",
                        resolverEndpoint,
                        stopwatch.Elapsed);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "DNS CNAME query cancelled for hostname: {Hostname}",
                    hostname);
                throw;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogError(
                    "DNS CNAME query timeout ({Timeout}s) for hostname: {Hostname}",
                    _options.Timeout,
                    hostname);

                return DnsLookupResult.CreateFailure(
                    "TIMEOUT",
                    resolverEndpoint,
                    stopwatch.Elapsed);
            }
            catch (DnsResponseException ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "DNS CNAME query exception for hostname: {Hostname}",
                    hostname);

                return DnsLookupResult.CreateFailure(
                    MapDnsException(ex),
                    resolverEndpoint,
                    stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "Unexpected error during DNS CNAME query for hostname: {Hostname}",
                    hostname);

                return DnsLookupResult.CreateFailure(
                    "NETWORK",
                    resolverEndpoint,
                    stopwatch.Elapsed);
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <inheritdoc />
    public async Task<HttpLookupResult> ResolveHttpFileAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.HostNameType == UriHostNameType.Unknown)
        {
            throw new ArgumentException("URL must be a valid HTTPS URL on the default port without user credentials.", nameof(url));
        }

        var cacheKey = $"HTTP:{url}";
        
        // Check cache if enabled
        if (_options.EnableCaching && _cache.TryGetValue<HttpLookupResult>(cacheKey, out var cachedResult))
        {
            if (_options.EnableDetailedLogging)
            {
                _logger.LogInformation(
                    "HTTP file lookup cache hit for URL: {Url}",
                    url);
            }
            return cachedResult!;
        }

        // Enforce concurrency limit
        await _concurrencyLimiter.WaitAsync(ct);
        try
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeout));

                if (_options.EnableDetailedLogging)
                {
                    _logger.LogInformation(
                        "Fetching HTTP verification file from URL: {Url}",
                        url);
                }

                var httpClient = _httpClientFactory.CreateClient("DnsVerification");
                using var response = await httpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);
                stopwatch.Stop();

                _logger.LogInformation(
                    "HTTP request completed for {Url} in {Latency}ms with status: {StatusCode}",
                    url,
                    stopwatch.ElapsedMilliseconds,
                    (int)response.StatusCode);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    const int maxVerificationBodyBytes = 4096;
                    if (response.Content.Headers.ContentLength > maxVerificationBodyBytes)
                    {
                        return HttpLookupResult.CreateFailure(
                            "RESPONSE_TOO_LARGE",
                            (int)response.StatusCode,
                            stopwatch.Elapsed);
                    }

                    await using var bodyStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                    using var reader = new StreamReader(bodyStream);
                    var buffer = new char[maxVerificationBodyBytes + 1];
                    var read = await reader.ReadBlockAsync(buffer.AsMemory(), timeoutCts.Token);
                    if (read > maxVerificationBodyBytes)
                    {
                        return HttpLookupResult.CreateFailure(
                            "RESPONSE_TOO_LARGE",
                            (int)response.StatusCode,
                            stopwatch.Elapsed);
                    }

                    var body = new string(buffer, 0, read);
                    
                    var result = HttpLookupResult.CreateSuccess(
                        body,
                        stopwatch.Elapsed,
                        (int)response.StatusCode);

                    CacheResult(cacheKey, result, _options.CacheTtl);
                    return result;
                }
                else
                {
                    var errorCode = MapHttpStatusCode(response.StatusCode);
                    
                    _logger.LogWarning(
                        "HTTP verification file request failed with status {StatusCode} for URL: {Url}",
                        (int)response.StatusCode,
                        url);

                    var result = HttpLookupResult.CreateFailure(
                        errorCode,
                        (int)response.StatusCode,
                        stopwatch.Elapsed);

                    // Cache 404 responses (file not uploaded yet)
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        CacheResult(cacheKey, result, _options.NegativeCacheTtl, isNegative: true);
                    }

                    return result;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "HTTP verification request cancelled for URL: {Url}",
                    url);
                throw;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogError(
                    "HTTP verification request timeout ({Timeout}s) for URL: {Url}",
                    _options.HttpTimeout,
                    url);

                return HttpLookupResult.CreateFailure(
                    "TIMEOUT",
                    0,
                    stopwatch.Elapsed);
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "SSL certificate validation failed for URL: {Url}",
                    url);

                return HttpLookupResult.CreateFailure(
                    "SSL_ERROR",
                    0,
                    stopwatch.Elapsed);
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "HTTP request exception for URL: {Url}",
                    url);

                return HttpLookupResult.CreateFailure(
                    "NETWORK",
                    0,
                    stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "Unexpected error during HTTP verification for URL: {Url}",
                    url);

                return HttpLookupResult.CreateFailure(
                    "UNKNOWN",
                    0,
                    stopwatch.Elapsed);
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Gets the resolver endpoint identifier for logging.
    /// </summary>
    private string GetResolverEndpoint()
    {
        if (_lookupClient is LookupClient lookupClient)
        {
            var nameServer = lookupClient.NameServers.FirstOrDefault();
            return nameServer?.ToString() ?? "unknown";
        }
        return "unknown";
    }

    /// <summary>
    /// Caches a lookup result if caching is enabled.
    ///
    /// DOMAIN-4.1 Section 14.3 Constitutional Law #28: DNS TTL clamped to max 300 seconds.
    /// </summary>
    private void CacheResult<T>(string key, T result, int ttlSeconds, bool isNegative = false)
    {
        if (!_options.EnableCaching)
        {
            return;
        }

        // Clamp TTL to max configured value (default 300s per Constitutional Law #28)
        var effectiveTtl = Math.Min(ttlSeconds, Math.Min(_options.MaxCacheTtl, 300));
        
        // Ensure minimum TTL for stability
        effectiveTtl = Math.Max(effectiveTtl, 1);

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(effectiveTtl)
        };

        _cache.Set(key, result, cacheOptions);

        if (_options.EnableDetailedLogging)
        {
            _logger.LogDebug(
                "Cached {CacheType} result for key: {Key} with TTL: {Ttl}s",
                isNegative ? "negative" : "positive",
                key,
                effectiveTtl);
        }
    }

    /// <summary>
    /// Maps DnsClient.NET exceptions to canonical error codes.
    /// </summary>
    private static string MapDnsException(DnsResponseException ex)
    {
        // Map DNS response codes to canonical error codes
        return MapDnsResponseCode(ex.Code.ToString()) switch
        {
            "SERVFAIL" => "SERVFAIL",
            "NXDOMAIN" => "NXDOMAIN",
            "REFUSED" => "REFUSED",
            _ => "UNKNOWN"
        };
    }

    private static string MapDnsResponseCode(string? responseCode)
    {
        return responseCode switch
        {
            null => "UNKNOWN",
            var value when value.Equals("NoError", StringComparison.OrdinalIgnoreCase) => "NOERROR",
            var value when value.Equals("NxDomain", StringComparison.OrdinalIgnoreCase) => "NXDOMAIN",
            var value when value.Equals("NXDomain", StringComparison.OrdinalIgnoreCase) => "NXDOMAIN",
            var value when value.Equals("NotExistentDomain", StringComparison.OrdinalIgnoreCase) => "NXDOMAIN",
            var value when value.Equals("ServerFailure", StringComparison.OrdinalIgnoreCase) => "SERVFAIL",
            var value when value.Equals("ServFail", StringComparison.OrdinalIgnoreCase) => "SERVFAIL",
            var value when value.Equals("Refused", StringComparison.OrdinalIgnoreCase) => "REFUSED",
            _ => "UNKNOWN",
        };
    }

    /// <summary>
    /// Maps HTTP status codes to canonical error codes.
    /// </summary>
    private static string MapHttpStatusCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.NotFound => "HTTP_404",
            HttpStatusCode.Forbidden => "HTTP_403",
            HttpStatusCode.InternalServerError => "HTTP_500",
            HttpStatusCode.ServiceUnavailable => "HTTP_503",
            HttpStatusCode.BadGateway => "HTTP_502",
            _ => "HTTP_ERROR"
        };
    }
}
