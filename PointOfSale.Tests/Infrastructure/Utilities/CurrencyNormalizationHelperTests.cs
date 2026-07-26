using System.Reflection;
using PointOfSale.Infrastructure.Utilities;
using Xunit;
using PointOfSale.Core.Financial;

namespace PointOfSale.Tests.Infrastructure.Utilities;

public class CurrencyNormalizationHelperTests
{
    [Theory]
    [InlineData("usd", "USD")]
    [InlineData("USD", "USD")]
    [InlineData("  egp  ", "EGP")]
    [InlineData("eur", "EUR")]
    public void NormalizeCurrencyOrThrow_WithValidCurrency_ReturnsNormalizedCurrency(string input, string expected)
    {
        // Act
        var result = CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(input, "Test context");

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCurrencyOrThrow_WithNullOrWhitespace_ThrowsInvalidOperationException(string? input)
    {
        // Act & Assert
        var exception = Assert.Throws<FinancialCurrencyMissingException>(() =>
            CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(input, "TenantId: 123"));

        Assert.Contains("Currency is required", exception.Message);
        Assert.Contains("TenantId: 123", exception.Message);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData(null)]
    [InlineData("")]
    public void NormalizeCurrencyOrDefault_ThrowsBecauseSilentFallbackIsForbidden(string? input)
    {
        // Use reflection to bypass compile-time error from [Obsolete(..., error: true)]
        var method = typeof(CurrencyNormalizationHelper).GetMethod(
            nameof(CurrencyNormalizationHelper.NormalizeCurrencyOrDefault),
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(string), typeof(bool), typeof(string)],
            null);

        Assert.NotNull(method);

        // Invoke the obsolete method via reflection
        var exception = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, [input, false, "payment-tests"]));

        // Assert that InvalidOperationException is thrown with expected message
        var innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Silent currency fallback is forbidden", innerException.Message);
        Assert.Contains("payment-tests", innerException.Message);
    }
}
