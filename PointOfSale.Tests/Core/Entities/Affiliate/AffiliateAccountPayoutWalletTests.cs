using PointOfSale.Core.Entities.Affiliate;
using Xunit;

namespace PointOfSale.Tests.Core.Entities.Affiliate;

/// <summary>
/// AF-16B: Unit tests for AffiliateAccount payout wallet functionality.
/// Tests domain-level validation, normalization, and timestamp updates.
/// </summary>
public class AffiliateAccountPayoutWalletTests
{
    private AffiliateAccount CreateTestAffiliate()
    {
        return AffiliateAccount.Create(
            tenantId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            referralCode: "TEST123",
            displayName: "Test Affiliate"
        );
    }

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE PAYOUT WALLET TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdatePayoutWallet_ValidMobileWallet_Succeeds()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();
        var beforeUpdate = DateTime.UtcNow;

        // Act
        affiliate.UpdatePayoutWallet("01012345678", PayoutWalletTypes.VodafoneCash);

        // Assert
        Assert.Equal("01012345678", affiliate.PayoutWalletNumber);
        Assert.Equal(PayoutWalletTypes.VodafoneCash, affiliate.PayoutWalletType);
        Assert.NotNull(affiliate.PayoutWalletUpdatedAtUtc);
        Assert.True(affiliate.PayoutWalletUpdatedAtUtc >= beforeUpdate);
        Assert.True(affiliate.UpdatedAt >= beforeUpdate);
    }

    [Fact]
    public void UpdatePayoutWallet_ValidBankAccount_Succeeds()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act
        affiliate.UpdatePayoutWallet("IBAN1234567890", PayoutWalletTypes.Bank);

        // Assert
        Assert.Equal("IBAN1234567890", affiliate.PayoutWalletNumber);
        Assert.Equal(PayoutWalletTypes.Bank, affiliate.PayoutWalletType);
        Assert.NotNull(affiliate.PayoutWalletUpdatedAtUtc);
    }

    [Theory]
    [InlineData("+201012345678", "01012345678")] // E.164 format normalized
    [InlineData("201012345678", "01012345678")] // E.164 without + normalized
    [InlineData("010-1234-5678", "01012345678")] // Dashes removed
    public void UpdatePayoutWallet_MobileWallet_NormalizesInput(string input, string expected)
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act
        affiliate.UpdatePayoutWallet(input, PayoutWalletTypes.VodafoneCash);

        // Assert
        Assert.Equal(expected, affiliate.PayoutWalletNumber);
    }

    [Fact]
    public void UpdatePayoutWallet_UpdatesTimestamp()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();
        affiliate.UpdatePayoutWallet("01012345678", PayoutWalletTypes.VodafoneCash);
        var firstUpdate = affiliate.PayoutWalletUpdatedAtUtc;

        // Wait a tiny bit to ensure timestamp difference
        Thread.Sleep(10);

        // Act
        affiliate.UpdatePayoutWallet("01098765432", PayoutWalletTypes.VodafoneCash);

        // Assert
        Assert.NotNull(affiliate.PayoutWalletUpdatedAtUtc);
        Assert.True(affiliate.PayoutWalletUpdatedAtUtc > firstUpdate);
    }

    [Fact]
    public void UpdatePayoutWallet_CanChangeWalletType()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();
        affiliate.UpdatePayoutWallet("01012345678", PayoutWalletTypes.VodafoneCash);

        // Act
        affiliate.UpdatePayoutWallet("IBAN123456", PayoutWalletTypes.Bank);

        // Assert
        Assert.Equal("IBAN123456", affiliate.PayoutWalletNumber);
        Assert.Equal(PayoutWalletTypes.Bank, affiliate.PayoutWalletType);
    }

    // ═══════════════════════════════════════════════════════════════════
    // VALIDATION ERROR TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdatePayoutWallet_EmptyNumber_ThrowsArgumentException()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            affiliate.UpdatePayoutWallet("", PayoutWalletTypes.VodafoneCash));
        
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void UpdatePayoutWallet_EmptyType_ThrowsArgumentException()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            affiliate.UpdatePayoutWallet("01012345678", ""));
        
        Assert.Contains("required", ex.Message);
    }

    [Theory]
    [InlineData("0101234567")] // 10 digits - too short
    [InlineData("010123456789")] // 12 digits - too long
    public void UpdatePayoutWallet_InvalidMobileFormat_ThrowsArgumentException(string invalidNumber)
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            affiliate.UpdatePayoutWallet(invalidNumber, PayoutWalletTypes.VodafoneCash));
        
        Assert.Contains("11 digits", ex.Message);
    }

    [Theory]
    [InlineData("12345")] // Too short
    [InlineData("123456789012345678901234567890123")] // Too long (33 chars)
    public void UpdatePayoutWallet_InvalidBankLength_ThrowsArgumentException(string invalidNumber)
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            affiliate.UpdatePayoutWallet(invalidNumber, PayoutWalletTypes.Bank));
        
        Assert.Contains("6-32 characters", ex.Message);
    }

    [Theory]
    [InlineData("ABC-123")] // Contains dash
    [InlineData("ABC@123")] // Contains special char
    public void UpdatePayoutWallet_BankWithSpecialChars_ThrowsArgumentException(string invalidNumber)
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => 
            affiliate.UpdatePayoutWallet(invalidNumber, PayoutWalletTypes.Bank));
        
        Assert.Contains("letters and digits", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════════
    // MASKING TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetMaskedWalletNumber_WithWallet_ReturnsMasked()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();
        affiliate.UpdatePayoutWallet("01012345678", PayoutWalletTypes.VodafoneCash);

        // Act
        var masked = affiliate.GetMaskedWalletNumber();

        // Assert
        Assert.Equal("010*****678", masked);
    }

    [Fact]
    public void GetMaskedWalletNumber_NoWallet_ReturnsNull()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act
        var masked = affiliate.GetMaskedWalletNumber();

        // Assert
        Assert.Null(masked);
    }

    // ═══════════════════════════════════════════════════════════════════
    // INITIAL STATE TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void NewAffiliate_HasNoPayoutWallet()
    {
        // Act
        var affiliate = CreateTestAffiliate();

        // Assert
        Assert.Null(affiliate.PayoutWalletNumber);
        Assert.Null(affiliate.PayoutWalletType);
        Assert.Null(affiliate.PayoutWalletUpdatedAtUtc);
    }

    // ═══════════════════════════════════════════════════════════════════
    // EDGE CASE TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdatePayoutWallet_MinimumBankLength_Succeeds()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();

        // Act
        affiliate.UpdatePayoutWallet("ABC123", PayoutWalletTypes.Bank);

        // Assert
        Assert.Equal("ABC123", affiliate.PayoutWalletNumber);
    }

    [Fact]
    public void UpdatePayoutWallet_MaximumBankLength_Succeeds()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();
        var maxLength = new string('A', 32); // 32 characters

        // Act
        affiliate.UpdatePayoutWallet(maxLength, PayoutWalletTypes.Bank);

        // Assert
        Assert.Equal(maxLength, affiliate.PayoutWalletNumber);
    }

    [Fact]
    public void UpdatePayoutWallet_AllMobileWalletTypes_Succeed()
    {
        // Arrange
        var affiliate = CreateTestAffiliate();
        var mobileTypes = new[]
        {
            PayoutWalletTypes.VodafoneCash,
            PayoutWalletTypes.OrangeMoney,
            PayoutWalletTypes.EtisalatCash,
            PayoutWalletTypes.InstaPay
        };

        // Act & Assert
        foreach (var type in mobileTypes)
        {
            affiliate.UpdatePayoutWallet("01012345678", type);
            Assert.Equal("01012345678", affiliate.PayoutWalletNumber);
            Assert.Equal(type, affiliate.PayoutWalletType);
        }
    }
}
