using Microsoft.Extensions.Logging;
using Moq;
using PhoneNumbers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Services;
using StackExchange.Redis;
using Xunit;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;

namespace PointOfSale.Tests.Unit;

/// <summary>
/// Unit tests for PhoneNumberNormalizer.
/// 
/// Tests cover:
/// 1. Egypt phone numbers (local and international)
/// 2. Saudi Arabia phone numbers
/// 3. UAE phone numbers
/// 4. US phone numbers
/// 5. UK phone numbers
/// 6. Invalid numbers
/// 7. Edge cases (spaces, dashes, leading zeros)
/// 8. Missing country code scenarios
/// </summary>
public class PhoneNumberNormalizerTests
{
    private readonly Mock<ILogger<PhoneNumberNormalizer>> _loggerMock;

    public PhoneNumberNormalizerTests()
    {
        _loggerMock = new Mock<ILogger<PhoneNumberNormalizer>>();
    }

    private PhoneNumberNormalizer CreateNormalizerWithoutDb()
    {
        // Create a minimal in-memory context for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext = new ApplicationDbContext(options);
        
        return new PhoneNumberNormalizer(dbContext, null, _loggerMock.Object);
    }

    // ==========================================================================
    // EGYPT PHONE NUMBERS (Country Code: +20)
    // ==========================================================================

    [Theory]
    [InlineData("01022174803", "+201022174803")]      // Local mobile
    [InlineData("1022174803", "+201022174803")]       // Without leading 0
    [InlineData("0100 123 4567", "+201001234567")]    // With spaces
    [InlineData("0111-234-5678", "+201112345678")]    // With dashes
    [InlineData("+201022174803", "+201022174803")]    // Already E.164
    [InlineData("00201022174803", "+201022174803")]   // International dial prefix
    public void Normalize_Egypt_ValidNumbers_ReturnsE164(string input, string expected)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.Normalize(input, "EG");

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("12345")]           // Too short
    [InlineData("010221748031234")] // Too long
    [InlineData("abc12345")]        // Contains letters
    [InlineData("")]                // Empty
    public void Normalize_Egypt_InvalidNumbers_ThrowsException(string input)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act & Assert
        Assert.Throws<PhoneNumberNormalizationException>(() => 
            normalizer.Normalize(input, "EG"));
    }

    // ==========================================================================
    // SAUDI ARABIA PHONE NUMBERS (Country Code: +966)
    // ==========================================================================

    [Theory]
    [InlineData("0551234567", "+966551234567")]       // Local mobile
    [InlineData("551234567", "+966551234567")]        // Without leading 0
    [InlineData("055 123 4567", "+966551234567")]     // With spaces
    [InlineData("+966551234567", "+966551234567")]    // Already E.164
    public void Normalize_SaudiArabia_ValidNumbers_ReturnsE164(string input, string expected)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.Normalize(input, "SA");

        // Assert
        Assert.Equal(expected, result);
    }

    // ==========================================================================
    // UAE PHONE NUMBERS (Country Code: +971)
    // ==========================================================================

    [Theory]
    [InlineData("0501234567", "+971501234567")]       // Local mobile
    [InlineData("501234567", "+971501234567")]        // Without leading 0
    [InlineData("+971501234567", "+971501234567")]    // Already E.164
    public void Normalize_UAE_ValidNumbers_ReturnsE164(string input, string expected)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.Normalize(input, "AE");

        // Assert
        Assert.Equal(expected, result);
    }

    // ==========================================================================
    // US PHONE NUMBERS (Country Code: +1)
    // ==========================================================================

    [Theory]
    [InlineData("2025551234", "+12025551234")]        // 10-digit
    [InlineData("202-555-1234", "+12025551234")]      // With dashes
    [InlineData("(202) 555-1234", "+12025551234")]    // With parentheses
    [InlineData("+12025551234", "+12025551234")]      // Already E.164
    public void Normalize_US_ValidNumbers_ReturnsE164(string input, string expected)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.Normalize(input, "US");

        // Assert
        Assert.Equal(expected, result);
    }

    // ==========================================================================
    // UK PHONE NUMBERS (Country Code: +44)
    // ==========================================================================

    [Theory]
    [InlineData("07911123456", "+447911123456")]      // Local mobile
    [InlineData("7911123456", "+447911123456")]       // Without leading 0
    [InlineData("+447911123456", "+447911123456")]    // Already E.164
    public void Normalize_UK_ValidNumbers_ReturnsE164(string input, string expected)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.Normalize(input, "GB");

        // Assert
        Assert.Equal(expected, result);
    }

    // ==========================================================================
    // VALIDATION TESTS
    // ==========================================================================

    [Theory]
    [InlineData("01022174803", "EG", true)]
    [InlineData("0551234567", "SA", true)]
    [InlineData("12345", "EG", false)]
    [InlineData("", "EG", false)]
    public void IsValidNumber_ReturnsCorrectResult(string input, string country, bool expected)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.IsValidNumber(input, country);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("01022174803", "EG", true)]
    [InlineData("12345", "EG", false)]
    public void TryNormalize_ReturnsCorrectResult(string input, string country, bool expectedSuccess)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.TryNormalize(input, country, out var normalized);

        // Assert
        Assert.Equal(expectedSuccess, result);
        if (expectedSuccess)
        {
            Assert.StartsWith("+", normalized);
        }
    }

    // ==========================================================================
    // ERROR HANDLING TESTS
    // ==========================================================================

    [Fact]
    public void Normalize_NullCountryCode_ThrowsException()
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act & Assert
        var ex = Assert.Throws<PhoneNumberNormalizationException>(() => 
            normalizer.Normalize("01022174803", null!));
        
        Assert.Equal(PhoneNormalizationFailureReason.MissingCountryCode, ex.Reason);
    }

    [Fact]
    public void Normalize_InvalidCountryCode_ThrowsException()
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act & Assert
        var ex = Assert.Throws<PhoneNumberNormalizationException>(() => 
            normalizer.Normalize("01022174803", "INVALID"));
        
        Assert.Equal(PhoneNormalizationFailureReason.InvalidCountryCode, ex.Reason);
    }

    [Fact]
    public void Normalize_EmptyPhoneNumber_ThrowsException()
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act & Assert
        var ex = Assert.Throws<PhoneNumberNormalizationException>(() => 
            normalizer.Normalize("", "EG"));
        
        Assert.Equal(PhoneNormalizationFailureReason.NotANumber, ex.Reason);
    }

    // ==========================================================================
    // EDGE CASES
    // ==========================================================================

    [Theory]
    [InlineData("  01022174803  ", "+201022174803")]  // Leading/trailing spaces
    [InlineData("010-221-74803", "+201022174803")]   // Dashes
    [InlineData("010.221.74803", "+201022174803")]   // Dots
    [InlineData("(010) 221-74803", "+201022174803")] // Parentheses
    public void Normalize_EdgeCases_HandlesCorrectly(string input, string expected)
    {
        // Arrange
        var normalizer = CreateNormalizerWithoutDb();

        // Act
        var result = normalizer.Normalize(input, "EG");

        // Assert
        Assert.Equal(expected, result);
    }
}
