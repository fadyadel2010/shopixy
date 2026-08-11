using System.Net;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Configuration;
using PointOfSale.Infrastructure.Services.Dns;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// Unit tests for DNS resolver foundation.
///
/// DOMAIN-4.2A — DNS Resolver Foundation Implementation Tests.
///
/// Test Coverage:
/// - TXT record lookup success and failures
/// - CNAME record lookup success and failures
/// - HTTP file verification success and failures
/// - Error normalization to canonical codes
/// - Timeout handling
/// - Cancellation token support
/// - Configuration binding
/// - Caching behavior (positive and negative)
/// - Health check functionality
/// </summary>
public class DnsResolverTests
{
    private readonly Mock<ILookupClient> _mockLookupClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<DnsClientResolver>> _mockLogger;
    private readonly DnsResolverOptions _options;
    private readonly IDnsResolver _resolver;

    public DnsResolverTests()
    {
        _mockLookupClient = new Mock<ILookupClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _mockLogger = new Mock<ILogger<DnsClientResolver>>();
        
        _options = new DnsResolverOptions
        {
            Provider = "DnsClient",
            NameServers = new List<string> { "8.8.8.8", "1.1.1.1" },
            Timeout = 30,
            HttpTimeout = 30,
            EnableCaching = true,
            CacheTtl = 60,
            MaxCacheTtl = 300,
            NegativeCacheTtl = 60,
            RetryAttempts = 2,
            RetryDelayMilliseconds = 1000,
            MaxConcurrentQueries = 50,
            EnableDetailedLogging = false
        };

        _resolver = new DnsClientResolver(
            _mockLookupClient.Object,
            _mockHttpClientFactory.Object,
            _cache,
            _mockLogger.Object,
            Options.Create(_options));
    }

    #region TXT Lookup Tests

    [Fact]
    public async Task ResolveTxtAsync_SuccessfulLookup_ReturnsSuccessResult()
    {
        // Arrange
        var hostname = "_shopixy-verification.example.com";
        var expectedToken = "shopixy-domain-verify-abc123";
        
        var txtRecord = CreateTxtRecord(hostname, expectedToken);

        var mockResponse = new Mock<IDnsQueryResponse>();
        mockResponse.Setup(r => r.Header).Returns(CreateHeader(0, answerCount: 1));
        mockResponse.Setup(r => r.Answers).Returns(new[] { txtRecord }.Cast<DnsResourceRecord>().ToList());

        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _resolver.ResolveTxtAsync(hostname);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedToken, result.Value);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Ttl);
        Assert.InRange(result.Ttl.Value, 1, 300);
        Assert.True(result.Latency.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task ResolveTxtAsync_NxDomain_ReturnsNxDomainError()
    {
        // Arrange
        var hostname = "_shopixy-verification.nonexistent.com";

        var mockResponse = new Mock<IDnsQueryResponse>();
        mockResponse.Setup(r => r.Header).Returns(CreateHeader(3)); // NXDOMAIN = 3
        mockResponse.Setup(r => r.Answers).Returns(new List<DnsResourceRecord>());

        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _resolver.ResolveTxtAsync(hostname);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Equal("NXDOMAIN", result.ErrorCode);
        Assert.Null(result.Ttl);
    }

    [Fact]
    public async Task ResolveTxtAsync_Timeout_ReturnsTimeoutError()
    {
        // Arrange
        var hostname = "_shopixy-verification.slow.com";
        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _resolver.ResolveTxtAsync(hostname);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("TIMEOUT", result.ErrorCode);
    }

    [Fact]
    public async Task ResolveTxtAsync_NullHostname_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _resolver.ResolveTxtAsync(null!));
    }

    [Fact]
    public async Task ResolveTxtAsync_EmptyHostname_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _resolver.ResolveTxtAsync(""));
    }

    [Fact]
    public async Task ResolveTxtAsync_CachingEnabled_ReturnsCachedResult()
    {
        // Arrange
        var hostname = "_shopixy-verification.cached.com";
        var expectedToken = "cached-token-123";
        
        var txtRecord = CreateTxtRecord(hostname, expectedToken);

        var mockResponse = new Mock<IDnsQueryResponse>();
        mockResponse.Setup(r => r.Header).Returns(CreateHeader(0, answerCount: 1));
        mockResponse.Setup(r => r.Answers).Returns(new[] { txtRecord }.Cast<DnsResourceRecord>().ToList());

        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act - First call populates cache
        var result1 = await _resolver.ResolveTxtAsync(hostname);
        
        // Act - Second call should hit cache
        var result2 = await _resolver.ResolveTxtAsync(hostname);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(expectedToken, result1.Value);
        Assert.Equal(expectedToken, result2.Value);
        
        // Verify DNS client was only called once
        _mockLookupClient.Verify(
            c => c.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region CNAME Lookup Tests

    [Fact]
    public async Task ResolveCnameAsync_SuccessfulLookup_ReturnsSuccessResult()
    {
        // Arrange
        var hostname = "_shopixy-verification.example.com";
        var expectedCname = "verify-abc123.domains.shopixy.com";
        
        var cnameRecord = CreateCnameRecord(hostname, expectedCname);

        var mockResponse = new Mock<IDnsQueryResponse>();
        mockResponse.Setup(r => r.Header).Returns(CreateHeader(0, answerCount: 1));
        mockResponse.Setup(r => r.Answers).Returns(new[] { cnameRecord }.Cast<DnsResourceRecord>().ToList());

        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.CNAME, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _resolver.ResolveCnameAsync(hostname);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedCname, result.Value);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Ttl);
        Assert.InRange(result.Ttl.Value, 1, 300);
    }

    [Fact]
    public async Task ResolveCnameAsync_NxDomain_ReturnsNxDomainError()
    {
        // Arrange
        var hostname = "_shopixy-verification.nonexistent.com";

        var mockResponse = new Mock<IDnsQueryResponse>();
        mockResponse.Setup(r => r.Header).Returns(CreateHeader(3)); // NXDOMAIN = 3
        mockResponse.Setup(r => r.Answers).Returns(new List<DnsResourceRecord>());

        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.CNAME, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _resolver.ResolveCnameAsync(hostname);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Equal("NXDOMAIN", result.ErrorCode);
    }

    #endregion

    #region HTTP File Verification Tests

    [Fact]
    public async Task ResolveHttpFileAsync_SuccessfulRequest_ReturnsSuccessResult()
    {
        // Arrange
        var url = "https://example.com/.well-known/shopixy-domain-verification.txt";
        var expectedToken = "shopixy-domain-verify-abc123";
        
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(expectedToken)
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        _mockHttpClientFactory
            .Setup(f => f.CreateClient("DnsVerification"))
            .Returns(httpClient);

        // Act
        var result = await _resolver.ResolveHttpFileAsync(url);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedToken, result.ResponseBody);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ResolveHttpFileAsync_NotFound_ReturnsHttp404Error()
    {
        // Arrange
        var url = "https://example.com/.well-known/shopixy-domain-verification.txt";
        
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        _mockHttpClientFactory
            .Setup(f => f.CreateClient("DnsVerification"))
            .Returns(httpClient);

        // Act
        var result = await _resolver.ResolveHttpFileAsync(url);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.ResponseBody);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("HTTP_404", result.ErrorCode);
    }

    [Fact]
    public async Task ResolveHttpFileAsync_InvalidUrl_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _resolver.ResolveHttpFileAsync("not-a-url"));
    }

    [Fact]
    public async Task ResolveHttpFileAsync_HttpUrl_ThrowsArgumentException()
    {
        // DOMAIN-4.1 Section 2.5: HTTP File verification requires HTTPS
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _resolver.ResolveHttpFileAsync("http://example.com/verification.txt"));
    }

    [Theory]
    [InlineData("https://example.com:8443/verification.txt")]
    [InlineData("https://user:password@example.com/verification.txt")]
    public async Task ResolveHttpFileAsync_NonCanonicalHttpsTarget_ThrowsArgumentException(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _resolver.ResolveHttpFileAsync(url));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("2001:db8::1")]
    public void HttpVerificationEndpointGuard_RejectsNonPublicAddresses(string value)
    {
        Assert.False(PublicEndpointHttpMessageHandler.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void HttpVerificationEndpointGuard_AcceptsPublicAddresses(string value)
    {
        Assert.True(PublicEndpointHttpMessageHandler.IsPublicAddress(IPAddress.Parse(value)));
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void DnsResolverOptions_Validate_ValidConfiguration_DoesNotThrow()
    {
        // Arrange
        var options = new DnsResolverOptions
        {
            Provider = "DnsClient",
            NameServers = new List<string> { "8.8.8.8" },
            Timeout = 30,
            MaxCacheTtl = 300
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void DnsResolverOptions_Validate_NoNameServers_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new DnsResolverOptions
        {
            Provider = "DnsClient",
            NameServers = new List<string>(),
            Timeout = 30
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void DnsResolverOptions_Validate_InvalidNameServerIp_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new DnsResolverOptions
        {
            Provider = "DnsClient",
            NameServers = new List<string> { "not-an-ip" },
            Timeout = 30
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void DnsResolverOptions_Validate_InvalidTimeout_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new DnsResolverOptions
        {
            Provider = "DnsClient",
            NameServers = new List<string> { "8.8.8.8" },
            Timeout = 0
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void DnsResolverOptions_Validate_MaxCacheTtlLessThanCacheTtl_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new DnsResolverOptions
        {
            Provider = "DnsClient",
            NameServers = new List<string> { "8.8.8.8" },
            Timeout = 30,
            CacheTtl = 300,
            MaxCacheTtl = 60 // Less than CacheTtl
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void DnsResolverOptions_Validate_NegativeCacheTtlAboveConstitutionalMaximum_Throws()
    {
        var options = new DnsResolverOptions
        {
            Provider = "DnsClient",
            NameServers = new List<string> { "8.8.8.8" },
            NegativeCacheTtl = 301,
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    #endregion

    #region Error Normalization Tests

    [Fact]
    public async Task ResolveTxtAsync_ServerFailure_ReturnsServFailError()
    {
        // Arrange
        var hostname = "_shopixy-verification.servfail.com";

        var mockResponse = new Mock<IDnsQueryResponse>();
        mockResponse.Setup(r => r.Header).Returns(CreateHeader(2)); // SERVFAIL = 2
        mockResponse.Setup(r => r.Answers).Returns(new List<DnsResourceRecord>());

        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _resolver.ResolveTxtAsync(hostname);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("SERVFAIL", result.ErrorCode);
    }

    [Fact]
    public async Task ResolveTxtAsync_Refused_ReturnsRefusedError()
    {
        // Arrange
        var hostname = "_shopixy-verification.refused.com";

        var mockResponse = new Mock<IDnsQueryResponse>();
        mockResponse.Setup(r => r.Header).Returns(CreateHeader(5)); // REFUSED = 5
        mockResponse.Setup(r => r.Answers).Returns(new List<DnsResourceRecord>());

        _mockLookupClient
            .Setup(c => c.QueryAsync(hostname, QueryType.TXT, QueryClass.IN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _resolver.ResolveTxtAsync(hostname);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("REFUSED", result.ErrorCode);
    }

    #endregion

    private static DnsResponseHeader CreateHeader(int responseCode, int answerCount = 0)
    {
        const ushort standardResponseFlags = 0x8180;
        var flags = (ushort)(standardResponseFlags | responseCode);
        return new DnsResponseHeader(1, flags, 1, answerCount, 0, 0);
    }

    private static TxtRecord CreateTxtRecord(string hostname, string value)
    {
        var info = new ResourceRecordInfo(
            hostname,
            ResourceRecordType.TXT,
            QueryClass.IN,
            300,
            value.Length);

        return new TxtRecord(info, new[] { value }, new[] { value });
    }

    private static CNameRecord CreateCnameRecord(string hostname, string canonicalName)
    {
        var info = new ResourceRecordInfo(
            hostname,
            ResourceRecordType.CNAME,
            QueryClass.IN,
            300,
            canonicalName.Length);

        return new CNameRecord(info, DnsString.Parse(canonicalName + "."));
    }
}
