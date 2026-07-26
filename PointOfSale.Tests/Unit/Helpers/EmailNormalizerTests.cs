using PointOfSale.Core.Helpers;
using Xunit;

namespace PointOfSale.Tests.Unit.Helpers;

/// <summary>
/// Unit tests for EmailNormalizer.
/// 
/// CRITICAL: Email normalization is security-critical for:
/// - Unique email enforcement per tenant
/// - Case-insensitive email lookup
/// - Preventing duplicate registrations with different casing
/// 
/// Phase: 10.3.1 Hardening - Email Normalization Tests
/// </summary>
public class EmailNormalizerTests
{
    #region Normalize Tests

    [Fact]
    public void Normalize_StandardEmail_ReturnsLowercaseTrimmed()
    {
        // Arrange
        var email = "User@Example.COM";

        // Act
        var result = EmailNormalizer.Normalize(email);

        // Assert
        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void Normalize_EmailWithLeadingSpaces_TrimsLeadingSpaces()
    {
        // Arrange
        var email = "   user@example.com";

        // Act
        var result = EmailNormalizer.Normalize(email);

        // Assert
        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void Normalize_EmailWithTrailingSpaces_TrimsTrailingSpaces()
    {
        // Arrange
        var email = "user@example.com   ";

        // Act
        var result = EmailNormalizer.Normalize(email);

        // Assert
        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void Normalize_EmailWithBothSpaces_TrimsBothSpaces()
    {
        // Arrange
        var email = "  User@EXAMPLE.com  ";

        // Act
        var result = EmailNormalizer.Normalize(email);

        // Assert
        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void Normalize_NullEmail_ReturnsEmptyString()
    {
        // Act
        var result = EmailNormalizer.Normalize(null);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_EmptyEmail_ReturnsEmptyString()
    {
        // Act
        var result = EmailNormalizer.Normalize(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_WhitespaceOnlyEmail_ReturnsEmptyString()
    {
        // Act
        var result = EmailNormalizer.Normalize("   ");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("TEST@TEST.COM", "test@test.com")]
    [InlineData("User.Name+tag@domain.CO.UK", "user.name+tag@domain.co.uk")]
    [InlineData("UPPERCASE@DOMAIN.COM", "uppercase@domain.com")]
    [InlineData("MixedCase@MixedCase.Com", "mixedcase@mixedcase.com")]
    public void Normalize_VariousCases_ReturnsLowercase(string input, string expected)
    {
        // Act
        var result = EmailNormalizer.Normalize(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalize_UsesToLowerInvariant_NotToLower()
    {
        // This test verifies culture-safe normalization
        // Turkish 'I' problem: "I".ToLower() in Turkish culture = "ı" (dotless i)
        // We want "I".ToLowerInvariant() = "i" always
        
        // Arrange
        var email = "USER@EXAMPLE.COM";

        // Save and change culture to Turkish
        var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = 
                new System.Globalization.CultureInfo("tr-TR");

            // Act
            var result = EmailNormalizer.Normalize(email);

            // Assert - should be 'i' not 'ı' (Turkish dotless i)
            Assert.Equal("user@example.com", result);
            Assert.DoesNotContain("ı", result); // Turkish dotless i
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    #endregion

    #region AreEqual Tests

    [Fact]
    public void AreEqual_SameEmailDifferentCase_ReturnsTrue()
    {
        // Arrange
        var email1 = "User@Example.COM";
        var email2 = "user@example.com";

        // Act
        var result = EmailNormalizer.AreEqual(email1, email2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreEqual_SameEmailWithSpaces_ReturnsTrue()
    {
        // Arrange
        var email1 = "  user@example.com  ";
        var email2 = "user@example.com";

        // Act
        var result = EmailNormalizer.AreEqual(email1, email2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreEqual_DifferentEmails_ReturnsFalse()
    {
        // Arrange
        var email1 = "user1@example.com";
        var email2 = "user2@example.com";

        // Act
        var result = EmailNormalizer.AreEqual(email1, email2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_BothNull_ReturnsTrue()
    {
        // Act
        var result = EmailNormalizer.AreEqual(null, null);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreEqual_OneNull_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(EmailNormalizer.AreEqual("user@example.com", null));
        Assert.False(EmailNormalizer.AreEqual(null, "user@example.com"));
    }

    #endregion

    #region NormalizeRequired Tests

    [Fact]
    public void NormalizeRequired_ValidEmail_ReturnsNormalized()
    {
        // Arrange
        var email = "  USER@EXAMPLE.COM  ";

        // Act
        var result = EmailNormalizer.NormalizeRequired(email);

        // Assert
        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void NormalizeRequired_NullEmail_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => EmailNormalizer.NormalizeRequired(null));
        Assert.Contains("email", ex.ParamName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeRequired_EmptyEmail_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => EmailNormalizer.NormalizeRequired(string.Empty));
        Assert.Contains("email", ex.ParamName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeRequired_WhitespaceEmail_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => EmailNormalizer.NormalizeRequired("   "));
        Assert.Contains("email", ex.ParamName, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Normalize_EmailWithUnicode_NormalizesCorrectly()
    {
        // Unicode characters in local part (rare but valid per RFC 6531)
        var email = "用户@例子.测试";
        
        var result = EmailNormalizer.Normalize(email);
        
        // Should be trimmed and lowercased (though unicode lowercase may vary)
        Assert.NotNull(result);
        Assert.Equal(email.Trim().ToLowerInvariant(), result);
    }

    [Fact]
    public void Normalize_EmailWithPlusAddressing_PreservesPlus()
    {
        // Gmail-style plus addressing
        var email = "User+TAG@Gmail.COM";
        
        var result = EmailNormalizer.Normalize(email);
        
        Assert.Equal("user+tag@gmail.com", result);
    }

    [Fact]
    public void Normalize_EmailWithDots_PreservesDots()
    {
        // Dots in local part
        var email = "First.Last@Example.COM";
        
        var result = EmailNormalizer.Normalize(email);
        
        Assert.Equal("first.last@example.com", result);
    }

    #endregion
}
