using PointOfSale.Core.Entities.Affiliate;
using Xunit;

namespace PointOfSale.Tests.Core.Entities.Affiliate;

/// <summary>
/// AF-16B: Unit tests for PayoutWallet value object.
/// Tests normalization, validation, and masking logic.
/// </summary>
public class PayoutWalletTests
{
    // ═══════════════════════════════════════════════════════════════════
    // MOBILE WALLET NORMALIZATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("01012345678", "01012345678")] // Already normalized
    [InlineData("+201012345678", "01012345678")] // E.164 format
    [InlineData("201012345678", "01012345678")] // E.164 without +
    [InlineData("0 10 123 456 78", "01012345678")] // With spaces
    [InlineData("010-1234-5678", "01012345678")] // With dashes
    [InlineData("(010) 1234-5678", "01012345678")] // With parentheses
    public void Create_VodafoneCash_NormalizesCorrectly(string input, string expected)
    {
        // Act
        var wallet = PayoutWallet.Create(input, PayoutWalletTypes.VodafoneCash);

        // Assert
        Assert.Equal(expected, wallet.Number);
        Assert.Equal(PayoutWalletTypes.VodafoneCash, wallet.Type);
    }

    [Theory]
    [InlineData("+201012345678")]
    [InlineData("201012345678")]
    [InlineData("010-1234-5678")]
    public void Create_OrangeMoney_NormalizesCorrectly(string input)
    {
        // Act
        var wallet = PayoutWallet.Create(input, PayoutWalletTypes.OrangeMoney);

        // Assert
        Assert.Equal("01012345678", wallet.Number);
    }

    [Theory]
    [InlineData("+201012345678")]
    [InlineData("201012345678")]
    public void Create_EtisalatCash_NormalizesCorrectly(string input)
    {
        // Act
        var wallet = PayoutWallet.Create(input, PayoutWalletTypes.EtisalatCash);

        // Assert
        Assert.Equal("01012345678", wallet.Number);
    }

    [Theory]
    [InlineData("+201012345678")]
    [InlineData("201012345678")]
    public void Create_InstaPay_NormalizesCorrectly(string input)
    {
        // Act
        var wallet = PayoutWallet.Create(input, PayoutWalletTypes.InstaPay);

        // Assert
        Assert.Equal("01012345678", wallet.Number);
    }

    // ═══════════════════════════════════════════════════════════════════
    // MOBILE WALLET VALIDATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("0101234567")] // 10 digits - too short
    [InlineData("010123456789")] // 12 digits - too long
    [InlineData("1012345678")] // Missing leading 0
    [InlineData("0101234ABC8")] // Contains letters (after normalization would fail)
    public void Create_VodafoneCash_InvalidFormat_ThrowsArgumentException(string input)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            PayoutWallet.Create(input, PayoutWalletTypes.VodafoneCash));
        
        Assert.Contains("11 digits", ex.Message);
    }

    [Fact]
    public void Create_MobileWallet_EmptyNumber_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            PayoutWallet.Create("", PayoutWalletTypes.VodafoneCash));
        
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void Create_MobileWallet_WhitespaceOnly_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            PayoutWallet.Create("   ", PayoutWalletTypes.VodafoneCash));
        
        Assert.Contains("required", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BANK ACCOUNT VALIDATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ABC123")]
    [InlineData("1234567890")]
    [InlineData("IBAN1234567890123456")]
    [InlineData("12345678901234567890123456789012")] // 32 chars - max
    public void Create_Bank_ValidFormats_Succeeds(string input)
    {
        // Act
        var wallet = PayoutWallet.Create(input, PayoutWalletTypes.Bank);

        // Assert
        Assert.Equal(input, wallet.Number);
        Assert.Equal(PayoutWalletTypes.Bank, wallet.Type);
    }

    [Theory]
    [InlineData("12345")] // 5 chars - too short
    [InlineData("123456789012345678901234567890123")] // 33 chars - too long
    public void Create_Bank_InvalidLength_ThrowsArgumentException(string input)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            PayoutWallet.Create(input, PayoutWalletTypes.Bank));
        
        Assert.Contains("6-32 characters", ex.Message);
    }

    [Theory]
    [InlineData("ABC-123")] // Contains dash
    [InlineData("ABC 123")] // Contains space
    [InlineData("ABC@123")] // Contains special char
    public void Create_Bank_InvalidCharacters_ThrowsArgumentException(string input)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            PayoutWallet.Create(input, PayoutWalletTypes.Bank));
        
        Assert.Contains("letters and digits", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════════
    // OTHER WALLET TYPE VALIDATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("WALLET123")]
    [InlineData("123456")]
    [InlineData("ABC123XYZ789")]
    public void Create_Other_ValidFormats_Succeeds(string input)
    {
        // Act
        var wallet = PayoutWallet.Create(input, PayoutWalletTypes.Other);

        // Assert
        Assert.Equal(input, wallet.Number);
        Assert.Equal(PayoutWalletTypes.Other, wallet.Type);
    }

    // ═══════════════════════════════════════════════════════════════════
    // MASKING TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("01012345678", "010*****678")]
    [InlineData("ABC123XYZ789", "ABC*****789")]
    [InlineData("1234567890", "123*****890")]
    public void GetMasked_ReturnsCorrectlyMaskedNumber(string input, string expected)
    {
        // Arrange
        var wallet = PayoutWallet.Create(input, PayoutWalletTypes.Bank);

        // Act
        var masked = wallet.GetMasked();

        // Assert
        Assert.Equal(expected, masked);
    }

    [Theory]
    [InlineData("123", "***")] // Too short
    [InlineData("12345", "***")] // 5 chars
    [InlineData("123456", "***")] // 6 chars - edge case
    public void MaskWalletNumber_ShortNumbers_ReturnsAsterisks(string input, string expected)
    {
        // Act
        var masked = PayoutWallet.MaskWalletNumber(input);

        // Assert
        Assert.Equal(expected, masked);
    }

    [Fact]
    public void MaskWalletNumber_EmptyString_ReturnsAsterisks()
    {
        // Act
        var masked = PayoutWallet.MaskWalletNumber("");

        // Assert
        Assert.Equal("***", masked);
    }

    [Fact]
    public void MaskWalletNumber_Null_ReturnsAsterisks()
    {
        // Act
        var masked = PayoutWallet.MaskWalletNumber(null!);

        // Assert
        Assert.Equal("***", masked);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WALLET TYPE VALIDATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Create_EmptyWalletType_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            PayoutWallet.Create("01012345678", ""));
        
        Assert.Contains("Wallet type is required", ex.Message);
    }

    [Fact]
    public void Create_NullWalletType_ThrowsArgumentException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            PayoutWallet.Create("01012345678", null!));
        
        Assert.Contains("Wallet type is required", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════════
    // VALUE OBJECT EQUALITY TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PayoutWallet_SameValues_AreEqual()
    {
        // Arrange
        var wallet1 = PayoutWallet.Create("01012345678", PayoutWalletTypes.VodafoneCash);
        var wallet2 = PayoutWallet.Create("01012345678", PayoutWalletTypes.VodafoneCash);

        // Assert
        Assert.Equal(wallet1, wallet2);
    }

    [Fact]
    public void PayoutWallet_DifferentNumbers_AreNotEqual()
    {
        // Arrange
        var wallet1 = PayoutWallet.Create("01012345678", PayoutWalletTypes.VodafoneCash);
        var wallet2 = PayoutWallet.Create("01098765432", PayoutWalletTypes.VodafoneCash);

        // Assert
        Assert.NotEqual(wallet1, wallet2);
    }

    [Fact]
    public void PayoutWallet_DifferentTypes_AreNotEqual()
    {
        // Arrange
        var wallet1 = PayoutWallet.Create("01012345678", PayoutWalletTypes.VodafoneCash);
        var wallet2 = PayoutWallet.Create("01012345678", PayoutWalletTypes.OrangeMoney);

        // Assert
        Assert.NotEqual(wallet1, wallet2);
    }
}
