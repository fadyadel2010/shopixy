using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Interfaces;
using Xunit;

namespace PointOfSale.Tests.Middleware;

/// <summary>
/// Unit tests for HostNormalization (DOMAIN-2A).
/// Verifies lowercase, trailing dot removal, port removal, whitespace trimming,
/// and IPv6 bracket handling — pure syntactic normalization with no DNS,
/// database, or tenant lookups.
/// </summary>
public class HostResolutionHostNormalizationTests
{
    private readonly HostNormalizer _normalizer = new();

    [Theory]
    [InlineData("WWW.MyBrand.com", "www.mybrand.com")]
    [InlineData("MYBRAND.COM", "mybrand.com")]
    [InlineData("mybrand.com", "mybrand.com")]
    public void Normalize_LowercasesHostname(string input, string expected)
    {
        var result = _normalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("mybrand.com.", "mybrand.com")]
    [InlineData("WWW.MyBrand.com.", "www.mybrand.com")]
    [InlineData("example.com.", "example.com")]
    public void Normalize_RemovesTrailingDot(string input, string expected)
    {
        var result = _normalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("mybrand.com:443", "mybrand.com")]
    [InlineData("mybrand.com:80", "mybrand.com")]
    [InlineData("mybrand.com:5000", "mybrand.com")]
    [InlineData("WWW.MyBrand.com:443", "www.mybrand.com")]
    public void Normalize_StripsPort(string input, string expected)
    {
        var result = _normalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("  mybrand.com  ", "mybrand.com")]
    [InlineData("\tmybrand.com\n", "mybrand.com")]
    [InlineData(" mybrand.com ", "mybrand.com")]
    public void Normalize_TrimsWhitespace(string input, string expected)
    {
        var result = _normalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[::1]:5000", "::1")]
    [InlineData("[::1]", "::1")]
    [InlineData("[2001:db8::1]:443", "2001:db8::1")]
    [InlineData("[2001:db8::1]", "2001:db8::1")]
    public void Normalize_HandlesIPv6Brackets(string input, string expected)
    {
        var result = _normalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Normalize_ReturnsNullForEmptyOrWhitespace(string? input)
    {
        var result = _normalizer.Normalize(input);
        Assert.Null(result);
    }

    [Fact]
    public void Normalize_DoesNotPerformDnsResolution()
    {
        // The normalizer is purely syntactic — it must not throw or attempt
        // any DNS resolution for any input. This test confirms that a valid
        // hostname string is processed without network calls.
        var result = _normalizer.Normalize("nonexistent.invalid.domain.example.com");
        Assert.Equal("nonexistent.invalid.domain.example.com", result);
    }

    [Fact]
    public void Normalize_DoesNotLookupDatabase()
    {
        // The normalizer must not accept any database dependency.
        // This test confirms the normalizer works without any context.
        var result = _normalizer.Normalize("shop.example.com");
        Assert.NotNull(result);
        Assert.Equal("shop.example.com", result);
    }

    [Theory]
    [InlineData("a.b.c.d.example.com.", "a.b.c.d.example.com")]
    [InlineData("sub.domain.com:8080", "sub.domain.com")]
    [InlineData("SUB.DOMAIN.COM", "sub.domain.com")]
    public void Normalize_CompositeInputs(string input, string expected)
    {
        var result = _normalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalize_UsesIdnaAsciiForCanonicalUniqueness()
    {
        Assert.Equal("xn--bcher-kva.example", _normalizer.Normalize("B\u00dcCHER.example."));
    }

    [Theory]
    [InlineData("under_score.example.com")]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("*.example.com")]
    [InlineData("example.com:invalid")]
    [InlineData("example.com..")]
    public void Normalize_RejectsHostConfusionInputs(string input)
    {
        Assert.Null(_normalizer.Normalize(input));
    }
}
