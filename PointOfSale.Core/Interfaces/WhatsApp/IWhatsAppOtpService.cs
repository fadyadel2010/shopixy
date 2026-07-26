namespace PointOfSale.Core.Interfaces.WhatsApp;

/// <summary>
/// Service for managing OTP codes sent via WhatsApp.
/// Handles generation, storage (Redis), and validation.
/// </summary>
public interface IWhatsAppOtpService
{
    /// <summary>
    /// Generates a new OTP code and stores it in Redis.
    /// Returns the generated code.
    /// </summary>
    Task<string> GenerateOtpAsync(
        Guid tenantId, 
        string phone, 
        string purpose, 
        int expiryMinutes = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Validates an OTP code.
    /// Returns true if valid, false if invalid/expired/max attempts exceeded.
    /// </summary>
    Task<OtpValidationResult> ValidateOtpAsync(
        Guid tenantId, 
        string phone, 
        string purpose, 
        string code,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates/deletes an OTP (after successful validation or manual cancel).
    /// </summary>
    Task InvalidateOtpAsync(
        Guid tenantId, 
        string phone, 
        string purpose,
        CancellationToken ct = default);
}

public record OtpValidationResult(
    bool IsValid, 
    string? ErrorReason, 
    int AttemptsRemaining);
