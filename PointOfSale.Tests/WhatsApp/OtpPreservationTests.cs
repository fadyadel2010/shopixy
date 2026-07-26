using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Infrastructure.WhatsApp.Services;
using StackExchange.Redis;
using Xunit;

namespace PointOfSale.Tests.WhatsApp;

/// <summary>
/// Unit Tests for OTP Logic Preservation
/// 
/// Tests specific OTP scenarios to ensure behavior remains unchanged after Baileys migration.
/// The OTP service is transport-agnostic and should work identically with both transports.
/// 
/// These tests validate:
/// - OTP generation format
/// - TTL values
/// - Validation logic
/// - Attempt tracking
/// - Redis key format
/// </summary>
[Trait("Category", "Unit")]
[Trait("Category", "OTP")]
public class OtpPreservationTests : IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly Mock<ILogger<WhatsAppOtpService>> _mockLogger;
    private readonly WhatsAppOtpService _otpService;

    public OtpPreservationTests()
    {
        // Note: These tests require a running Redis instance
        _redis = ConnectionMultiplexer.Connect("localhost:6379");
        _mockLogger = new Mock<ILogger<WhatsAppOtpService>>();
        _otpService = new WhatsAppOtpService(_redis, _mockLogger.Object);
    }

    #region OTP Generation Tests

    [Fact(Skip = "Requires Redis")]
    public async Task GenerateOtp_Always_Returns6DigitCode()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";

        // Act
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Assert
        Assert.Equal(6, otp.Length);
        Assert.True(otp.All(char.IsDigit));
        Assert.True(int.TryParse(otp, out _));
    }

    [Fact(Skip = "Requires Redis")]
    public async Task GenerateOtp_WithCustomExpiry_SetsCorrectTTL()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "password-reset";
        var expiryMinutes = 10;

        // Act
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose, expiryMinutes);

        // Verify TTL in Redis
        var db = _redis.GetDatabase();
        var cleanPhone = phone.Replace("+", "");
        var key = $"whatsapp:otp:{tenantId:N}:{cleanPhone}:{purpose}";
        var ttl = await db.KeyTimeToLiveAsync(key);

        // Assert
        Assert.True(ttl.HasValue);
        Assert.InRange(ttl.Value.TotalMinutes, expiryMinutes - 0.5, expiryMinutes + 0.5);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task GenerateOtp_DefaultExpiry_Is5Minutes()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "verification";

        // Act
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Verify TTL in Redis
        var db = _redis.GetDatabase();
        var cleanPhone = phone.Replace("+", "");
        var key = $"whatsapp:otp:{tenantId:N}:{cleanPhone}:{purpose}";
        var ttl = await db.KeyTimeToLiveAsync(key);

        // Assert
        Assert.True(ttl.HasValue);
        Assert.InRange(ttl.Value.TotalMinutes, 4.5, 5.5); // 5 minutes ± 30 seconds
    }

    #endregion

    #region OTP Validation Tests

    [Fact(Skip = "Requires Redis")]
    public async Task ValidateOtp_CorrectCode_ReturnsSuccess()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Act
        var result = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, otp);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(0, result.AttemptsRemaining);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task ValidateOtp_WrongCode_ReturnsFailureWithAttemptsRemaining()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";
        await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Act
        var result = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, "000000");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(2, result.AttemptsRemaining); // 3 max - 1 attempt = 2 remaining
    }

    [Fact(Skip = "Requires Redis")]
    public async Task ValidateOtp_ThreeWrongAttempts_ExceedsMaxAttempts()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";
        var correctOtp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Act - Make 3 wrong attempts
        var result1 = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, "000000");
        var result2 = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, "111111");
        var result3 = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, "222222");

        // Try with correct code after max attempts
        var result4 = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, correctOtp);

        // Assert
        Assert.False(result1.IsValid);
        Assert.Equal(2, result1.AttemptsRemaining);

        Assert.False(result2.IsValid);
        Assert.Equal(1, result2.AttemptsRemaining);

        Assert.False(result3.IsValid);
        Assert.Equal(0, result3.AttemptsRemaining);

        Assert.False(result4.IsValid);
        Assert.Contains("expired", result4.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task ValidateOtp_NonExistentOtp_ReturnsExpiredError()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";

        // Act - Validate without generating
        var result = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, "123456");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task ValidateOtp_SuccessfulValidation_DeletesOtp()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Act
        var result1 = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, otp);
        var result2 = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, otp);

        // Assert
        Assert.True(result1.IsValid);
        Assert.False(result2.IsValid); // Should fail because OTP was deleted
    }

    #endregion

    #region OTP Invalidation Tests

    [Fact(Skip = "Requires Redis")]
    public async Task InvalidateOtp_DeletesOtpFromRedis()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Act
        await _otpService.InvalidateOtpAsync(tenantId, phone, purpose);

        // Try to validate after invalidation
        var result = await _otpService.ValidateOtpAsync(tenantId, phone, purpose, otp);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Redis Key Format Tests

    [Theory(Skip = "Requires Redis")]
    [InlineData("+5511999999999", "5511999999999")]
    [InlineData("5511999999999", "5511999999999")]
    [InlineData("+55 11 99999-9999", "5511999999999")]
    [InlineData("+55 (11) 99999-9999", "5511999999999")]
    public async Task RedisKeyFormat_NormalizesPhoneNumber(string inputPhone, string expectedCleanPhone)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var purpose = "login";

        // Act
        var otp = await _otpService.GenerateOtpAsync(tenantId, inputPhone, purpose);

        // Verify key exists with normalized phone
        var db = _redis.GetDatabase();
        var expectedKey = $"whatsapp:otp:{tenantId:N}:{expectedCleanPhone}:{purpose}";
        var exists = await db.KeyExistsAsync(expectedKey);

        // Assert
        Assert.True(exists, $"Expected key: {expectedKey}");
    }

    [Fact(Skip = "Requires Redis")]
    public async Task RedisKeyFormat_UsesCorrectPrefix()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";

        // Act
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose);

        // Verify key format
        var db = _redis.GetDatabase();
        var cleanPhone = phone.Replace("+", "");
        var expectedKey = $"whatsapp:otp:{tenantId:N}:{cleanPhone}:{purpose}";
        var exists = await db.KeyExistsAsync(expectedKey);

        // Assert
        Assert.True(exists);
        Assert.StartsWith("whatsapp:otp:", expectedKey);
    }

    #endregion

    #region TTL Preservation Tests

    [Theory(Skip = "Requires Redis")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    public async Task TtlPreservation_MatchesSpecifiedExpiry(int expiryMinutes)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "test";

        // Act
        var otp = await _otpService.GenerateOtpAsync(tenantId, phone, purpose, expiryMinutes);

        // Verify TTL
        var db = _redis.GetDatabase();
        var cleanPhone = phone.Replace("+", "");
        var key = $"whatsapp:otp:{tenantId:N}:{cleanPhone}:{purpose}";
        var ttl = await db.KeyTimeToLiveAsync(key);

        // Assert
        Assert.True(ttl.HasValue);
        Assert.InRange(ttl.Value.TotalMinutes, expiryMinutes - 0.5, expiryMinutes + 0.5);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task TtlPreservation_WrongAttempt_PreservesTTL()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var phone = "+5511999999999";
        var purpose = "login";
        var expiryMinutes = 5;
        await _otpService.GenerateOtpAsync(tenantId, phone, purpose, expiryMinutes);

        // Get initial TTL
        var db = _redis.GetDatabase();
        var cleanPhone = phone.Replace("+", "");
        var key = $"whatsapp:otp:{tenantId:N}:{cleanPhone}:{purpose}";
        var initialTtl = await db.KeyTimeToLiveAsync(key);

        // Wait a bit
        await Task.Delay(1000);

        // Act - Make wrong attempt
        await _otpService.ValidateOtpAsync(tenantId, phone, purpose, "000000");

        // Get TTL after wrong attempt
        var afterTtl = await db.KeyTimeToLiveAsync(key);

        // Assert
        Assert.True(initialTtl.HasValue);
        Assert.True(afterTtl.HasValue);
        
        // TTL should be approximately the same (within 2 seconds tolerance)
        var ttlDifference = Math.Abs((initialTtl.Value - afterTtl.Value).TotalSeconds);
        Assert.InRange(ttlDifference, 0, 2);
    }

    #endregion

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
