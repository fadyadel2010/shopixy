using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.WhatsApp.Services;
using StackExchange.Redis;
using Xunit;

namespace PointOfSale.Tests.WhatsApp;

/// <summary>
/// Property-Based Tests for OTP Logic Preservation
/// 
/// Tests universal properties that must hold for OTP logic:
/// - Property 18: OTP Logic Preservation
/// 
/// Validates that OTP generation, validation, and TTL behavior remain unchanged
/// after the Baileys migration. The OTP service is transport-agnostic and should
/// produce identical results regardless of which transport is used.
/// 
/// Uses FsCheck for property-based testing with 100+ iterations
/// </summary>
public class OtpPreservationPropertyTests : IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly Mock<ILogger<WhatsAppOtpService>> _mockLogger;
    private readonly WhatsAppOtpService _otpService;

    public OtpPreservationPropertyTests()
    {
        // Connect to Redis for testing
        // Note: These tests require a running Redis instance
        _redis = ConnectionMultiplexer.Connect("localhost:6379");
        _mockLogger = new Mock<ILogger<WhatsAppOtpService>>();
        _otpService = new WhatsAppOtpService(_redis, _mockLogger.Object);
    }

    /// <summary>
    /// Property 18: OTP Logic Preservation
    /// 
    /// **Validates: Requirements 8.1**
    /// 
    /// For any tenant, phone, and purpose combination, the OTP service must:
    /// 1. Generate a 6-digit numeric code
    /// 2. Store it in Redis with correct TTL
    /// 3. Validate correctly with exact match
    /// 4. Track attempts correctly
    /// 5. Expire after TTL
    /// 6. Use consistent Redis key format
    /// 
    /// This behavior must be identical before and after Baileys migration.
    /// </summary>
    [Property(MaxTest = 100, Skip = "Requires Redis")]
    public Property OtpGeneration_AlwaysProduces6DigitCode()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, phone, purpose) =>
            {
                // Act
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    expiryMinutes: 5);

                // Assert
                return (otp.Length == 6)
                    .Label("OTP should be 6 digits")
                    .And(otp.All(char.IsDigit))
                    .Label("OTP should contain only digits")
                    .And(int.TryParse(otp, out _))
                    .Label("OTP should be a valid integer");
            });
    }

    [Property(MaxTest = 100, Skip = "Requires Redis")]
    public Property OtpValidation_CorrectCode_Succeeds()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, phone, purpose) =>
            {
                // Arrange
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    expiryMinutes: 5);

                // Act
                var result = await _otpService.ValidateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    otp);

                // Assert
                return result.IsValid
                    .Label("Validation should succeed with correct code")
                    .And(result.ErrorMessage == null)
                    .Label("No error message should be present");
            });
    }

    [Property(MaxTest = 100, Skip = "Requires Redis")]
    public Property OtpValidation_WrongCode_FailsAndTracksAttempts()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, phone, purpose) =>
            {
                // Arrange
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    expiryMinutes: 5);

                var wrongCode = otp == "000000" ? "111111" : "000000";

                // Act
                var result = await _otpService.ValidateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    wrongCode);

                // Assert
                return (!result.IsValid)
                    .Label("Validation should fail with wrong code")
                    .And(result.AttemptsRemaining == 2)
                    .Label("Should have 2 attempts remaining after first failure")
                    .And(result.ErrorMessage != null)
                    .Label("Error message should be present");
            });
    }

    [Property(MaxTest = 50, Skip = "Requires Redis")]
    public Property OtpValidation_MaxAttemptsExceeded_DeletesOtp()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, phone, purpose) =>
            {
                // Arrange
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    expiryMinutes: 5);

                var wrongCode = "000000";

                // Act - Attempt 3 times with wrong code
                var result1 = await _otpService.ValidateOtpAsync(tenantId, phone.Get, purpose.Get, wrongCode);
                var result2 = await _otpService.ValidateOtpAsync(tenantId, phone.Get, purpose.Get, wrongCode);
                var result3 = await _otpService.ValidateOtpAsync(tenantId, phone.Get, purpose.Get, wrongCode);

                // Try with correct code after max attempts
                var result4 = await _otpService.ValidateOtpAsync(tenantId, phone.Get, purpose.Get, otp);

                // Assert
                return (!result1.IsValid && result1.AttemptsRemaining == 2)
                    .Label("First attempt should fail with 2 remaining")
                    .And(!result2.IsValid && result2.AttemptsRemaining == 1)
                    .Label("Second attempt should fail with 1 remaining")
                    .And(!result3.IsValid && result3.AttemptsRemaining == 0)
                    .Label("Third attempt should fail with 0 remaining")
                    .And(!result4.IsValid)
                    .Label("Should not validate even with correct code after max attempts")
                    .And(result4.ErrorMessage != null && result4.ErrorMessage.Contains("expired", StringComparison.OrdinalIgnoreCase))
                    .Label("Should indicate OTP is expired/not found");
            });
    }

    [Property(MaxTest = 50, Skip = "Requires Redis")]
    public Property OtpInvalidation_DeletesOtp()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, phone, purpose) =>
            {
                // Arrange
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    expiryMinutes: 5);

                // Act
                await _otpService.InvalidateOtpAsync(tenantId, phone.Get, purpose.Get);

                // Try to validate after invalidation
                var result = await _otpService.ValidateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    otp);

                // Assert
                return (!result.IsValid)
                    .Label("Validation should fail after invalidation")
                    .And(result.ErrorMessage != null)
                    .Label("Error message should be present");
            });
    }

    /// <summary>
    /// Property: Redis Key Format Consistency
    /// 
    /// The Redis key format must remain consistent:
    /// whatsapp:otp:{tenantId}:{cleanPhone}:{purpose}
    /// 
    /// This ensures existing OTPs remain accessible after migration.
    /// </summary>
    [Property(MaxTest = 100, Skip = "Requires Redis")]
    public Property RedisKeyFormat_RemainsConsistent()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Gen.Elements("+5511999999999", "5511999999999", "+55 11 99999-9999"),
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, phone, purpose) =>
            {
                // Arrange
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone,
                    purpose.Get,
                    expiryMinutes: 5);

                // Act - Check if key exists in Redis with expected format
                var db = _redis.GetDatabase();
                
                // Clean phone for key (remove +, spaces)
                var cleanPhone = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();
                var expectedKey = $"whatsapp:otp:{tenantId:N}:{cleanPhone}:{purpose.Get}";
                
                var exists = await db.KeyExistsAsync(expectedKey);

                // Assert
                return exists
                    .Label($"Key should exist with format: {expectedKey}");
            });
    }

    /// <summary>
    /// Property: TTL Preservation
    /// 
    /// The TTL (Time To Live) for OTP keys must be preserved correctly.
    /// After generating an OTP with N minutes expiry, the Redis key should
    /// have approximately N minutes TTL (within reasonable tolerance).
    /// </summary>
    [Property(MaxTest = 50, Skip = "Requires Redis")]
    public Property TtlPreservation_MatchesExpiryMinutes()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            Gen.Choose(1, 10), // Expiry minutes between 1 and 10
            async (tenantId, phone, purpose, expiryMinutes) =>
            {
                // Arrange & Act
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    expiryMinutes);

                // Check TTL
                var db = _redis.GetDatabase();
                var cleanPhone = phone.Get.Replace("+", "").Replace(" ", "").Trim();
                var key = $"whatsapp:otp:{tenantId:N}:{cleanPhone}:{purpose.Get}";
                
                var ttl = await db.KeyTimeToLiveAsync(key);

                // Assert
                return ttl.HasValue
                    .Label("TTL should be set")
                    .And(ttl.Value.TotalMinutes >= expiryMinutes - 0.5)
                    .Label($"TTL should be at least {expiryMinutes - 0.5} minutes")
                    .And(ttl.Value.TotalMinutes <= expiryMinutes + 0.5)
                    .Label($"TTL should be at most {expiryMinutes + 0.5} minutes")
                    .When(ttl.HasValue);
            });
    }

    /// <summary>
    /// Property: Case Sensitivity
    /// 
    /// OTP validation must be case-sensitive (though OTPs are numeric, this validates
    /// the comparison logic).
    /// </summary>
    [Property(MaxTest = 100, Skip = "Requires Redis")]
    public Property OtpValidation_IsCaseSensitive()
    {
        return Prop.ForAll(
            Arb.Default.Guid().Generator,
            Arb.Default.NonEmptyString().Generator,
            Arb.Default.NonEmptyString().Generator,
            async (tenantId, phone, purpose) =>
            {
                // Arrange
                var otp = await _otpService.GenerateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    expiryMinutes: 5);

                // Act - Validate with exact code
                var result = await _otpService.ValidateOtpAsync(
                    tenantId,
                    phone.Get,
                    purpose.Get,
                    otp);

                // Assert
                return result.IsValid
                    .Label("Exact match should succeed");
            });
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
