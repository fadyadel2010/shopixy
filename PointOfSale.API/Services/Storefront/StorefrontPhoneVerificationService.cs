using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Request for phone verification OTP (checkout flow)
/// </summary>
public class CheckoutPhoneVerificationRequest
{
    public string Phone { get; set; } = string.Empty;
}

/// <summary>
/// Request to verify the OTP code (checkout flow)
/// </summary>
public class VerifyCheckoutOtpRequest
{
    public string Phone { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
}

/// <summary>
/// Status response for checkout phone verification
/// </summary>
public class CheckoutPhoneVerificationStatus
{
    /// <summary>
    /// True if phone verification is required before checkout
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// True if the phone has already been verified
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// The phone number that needs verification (as entered by user)
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// The phone number formatted in E.164 format (for display confirmation)
    /// e.g., "01022174803" becomes "+201022174803"
    /// </summary>
    public string? FormattedPhone { get; set; }

    /// <summary>
    /// Message for the customer
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of phone verification operations
/// </summary>
public class PhoneVerificationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int? ExpiresInSeconds { get; set; }
    public int? RetryAfterSeconds { get; set; }
}

/// <summary>
/// Interface for phone verification service (checkout-focused)
/// </summary>
public interface IStorefrontPhoneVerificationService
{
    /// <summary>
    /// Checks if phone verification is required for this tenant AND this verification scope.
    /// Returns false if: tenant has no active OTP delivery channel or phone already verified.
    /// </summary>
    Task<CheckoutPhoneVerificationStatus> CheckVerificationRequiredAsync(
        Guid tenantId,
        string verificationScopeId,
        string sessionPhone,
        Guid? customerId,
        CancellationToken ct = default);

    /// <summary>
    /// Requests a phone verification OTP via WhatsApp for checkout.
    /// </summary>
    Task<PhoneVerificationResult> RequestVerificationAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string? clientIp = null,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies the OTP code for checkout.
    /// </summary>
    /// <param name="customerId">Optional customer ID - if provided, verification is also stored for customer ID</param>
    Task<PhoneVerificationResult> VerifyOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string otpCode,
        string? clientIp = null,
        Guid? customerId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a session has a verified phone number (for checkout validation).
    /// </summary>
    Task<bool> IsPhoneVerifiedForSessionAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CancellationToken ct = default);
}

/// <summary>
/// Phone verification service using WhatsApp OTP for CHECKOUT flow.
/// 
/// Reliability rules:
/// - Never return success when no deliverable OTP channel exists
/// - Reuse an active OTP deterministically within the resend window
/// - Track explicit OTP state in Redis for strict verify/expire behavior
/// - Keep verification scoped to the normalized phone and checkout scope
/// </summary>
public class StorefrontPhoneVerificationService : IStorefrontPhoneVerificationService
{
    private const int OtpLength = 6;
    private const int OtpHashIterations = 100_000;
    private const int OtpExpiryMinutes = 5;
    private const int MaxOtpRequestsPerWindow = 3;
    private const int RateLimitWindowMinutes = 5;
    private const int OtpResendCooldownSeconds = 60;
    private const int OtpLockTtlSeconds = 10;
    private const int SessionVerificationTtlHours = 24;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IWhatsAppFeatureGateService _featureGate;
    private readonly IEventPublisher _eventPublisher;
    private readonly IPhoneNumberNormalizer _phoneNormalizer;
    private readonly ICartService _cartService;
    private readonly ICheckoutPhoneOtpStore _otpStore;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<StorefrontPhoneVerificationService> _logger;

    public StorefrontPhoneVerificationService(
        ApplicationDbContext dbContext,
        IWhatsAppFeatureGateService featureGate,
        IEventPublisher eventPublisher,
        IPhoneNumberNormalizer phoneNormalizer,
        ICartService cartService,
        ICheckoutPhoneOtpStore otpStore,
        CommerceReliabilityTelemetry telemetry,
        ILogger<StorefrontPhoneVerificationService> logger)
    {
        _dbContext = dbContext;
        _featureGate = featureGate;
        _eventPublisher = eventPublisher;
        _phoneNormalizer = phoneNormalizer;
        _cartService = cartService;
        _otpStore = otpStore;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<CheckoutPhoneVerificationStatus> CheckVerificationRequiredAsync(
        Guid tenantId,
        string verificationScopeId,
        string sessionPhone,
        Guid? customerId,
        CancellationToken ct = default)
    {
        var activeAccount = await _featureGate.GetActiveAccountAsync(tenantId, WhatsAppUseCase.OtpVerification, ct);
        if (activeAccount == null)
        {
            _logger.LogDebug(
                "[CheckoutPhoneOtp] Not required - no active OTP delivery channel | TenantId: {TenantId}",
                tenantId);

            return new CheckoutPhoneVerificationStatus
            {
                IsRequired = false,
                IsVerified = true,
                Message = "Phone verification not required"
            };
        }

        // Only the tenant-aware normalizer may establish a verified identity.
        // Never invent a country prefix after it has rejected the input.
        var normalizedPhone = await TryNormalizePhoneAsync(tenantId, sessionPhone, ct);

        if (!string.IsNullOrWhiteSpace(normalizedPhone))
        {
            if (!string.IsNullOrWhiteSpace(verificationScopeId) &&
                await _otpStore.IsVerifiedAsync(tenantId, verificationScopeId, normalizedPhone, ct))
            {
                return new CheckoutPhoneVerificationStatus
                {
                    IsRequired = false,
                    IsVerified = true,
                    Phone = normalizedPhone,
                    FormattedPhone = normalizedPhone,
                    Message = "Phone already verified"
                };
            }

            if (customerId.HasValue && customerId.Value != Guid.Empty)
            {
                var customerScopeId = customerId.Value.ToString("N");
                if (await _otpStore.IsVerifiedAsync(tenantId, customerScopeId, normalizedPhone, ct))
                {
                    return new CheckoutPhoneVerificationStatus
                    {
                        IsRequired = false,
                        IsVerified = true,
                        Phone = normalizedPhone,
                        FormattedPhone = normalizedPhone,
                        Message = "Phone already verified"
                    };
                }
            }
        }

        if (customerId.HasValue)
        {
            var customer = await _dbContext.OnlineCustomers
                .FirstOrDefaultAsync(c => c.Id == customerId.Value && c.TenantId == tenantId, ct);

            if (!string.IsNullOrWhiteSpace(normalizedPhone) &&
                customer?.PhoneVerified == true &&
                !string.IsNullOrWhiteSpace(customer.Phone) &&
                string.Equals(
                    await TryNormalizePhoneAsync(tenantId, customer.Phone, ct),
                    normalizedPhone,
                    StringComparison.Ordinal))
            {
                return new CheckoutPhoneVerificationStatus
                {
                    IsRequired = false,
                    IsVerified = true,
                    Phone = normalizedPhone,
                    FormattedPhone = normalizedPhone,
                    Message = "Phone already verified"
                };
            }
        }

        string? formattedPhone = null;
        try
        {
            formattedPhone = await _phoneNormalizer.NormalizeAsync(sessionPhone, tenantId, ct);
        }
        catch (PhoneNumberNormalizationException ex)
        {
            _logger.LogDebug(
                "[CheckoutPhoneOtp] Could not format phone | TenantId: {TenantId} | Phone: {Phone} | Reason: {Reason}",
                tenantId,
                MaskPhone(sessionPhone),
                ex.Reason);
        }

        return new CheckoutPhoneVerificationStatus
        {
            IsRequired = true,
            IsVerified = false,
            Phone = normalizedPhone,
            FormattedPhone = formattedPhone,
            Message = "Please verify your phone number via WhatsApp OTP to complete checkout"
        };
    }

    public async Task<PhoneVerificationResult> RequestVerificationAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string? clientIp = null,
        CancellationToken ct = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        var nowUtc = DateTime.UtcNow;

        var normalization = await NormalizePhoneForOtpAsync(tenantId, phone, traceId, "request", ct);
        if (normalization.ErrorResult != null)
        {
            _telemetry.RecordOtpRequest(tenantId, "whatsapp", "rejected", normalization.ErrorResult.Error);
            return normalization.ErrorResult;
        }

        phone = normalization.NormalizedPhone!;
        var rateLimitKeys = BuildRateLimitKeys(sessionId, phone, clientIp);

        if (await _otpStore.IsVerifiedAsync(tenantId, sessionId, phone, ct))
        {
            _telemetry.RecordOtpRequest(tenantId, "whatsapp", "accepted", "already_verified");
            return new PhoneVerificationResult
            {
                Success = true,
                Message = "Phone already verified"
            };
        }

        var activeAccount = await _featureGate.GetActiveAccountAsync(tenantId, WhatsAppUseCase.OtpVerification, ct);
        if (activeAccount == null)
        {
            _logger.LogError(
                "[CheckoutPhoneOtp] OTP channel unavailable | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | TraceId: {TraceId}",
                tenantId,
                sessionId,
                MaskPhone(phone),
                traceId);

            _telemetry.RecordOtpRequest(tenantId, "whatsapp", "rejected", "otp_channel_unavailable");
            _telemetry.RecordOtpChannelUnavailable(tenantId, "whatsapp", "no_active_account");

            return CreateFailureResult(
                "OTP_CHANNEL_UNAVAILABLE",
                "Verification service is currently unavailable. Please try again later.");
        }

        var lockToken = Guid.NewGuid().ToString("N");
        var lockAcquired = await _otpStore.TryAcquireLockAsync(
            tenantId,
            sessionId,
            phone,
            lockToken,
            TimeSpan.FromSeconds(OtpLockTtlSeconds),
            ct);

        if (!lockAcquired)
        {
            _logger.LogWarning(
                "[CheckoutPhoneOtp] Concurrent OTP request rejected | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | TraceId: {TraceId}",
                tenantId,
                sessionId,
                MaskPhone(phone),
                traceId);

            _telemetry.RecordOtpRequest(tenantId, "whatsapp", "rejected", "otp_request_in_progress");

            return CreateFailureResult(
                "OTP_REQUEST_IN_PROGRESS",
                "A verification request is already in progress. Please wait a moment and try again.");
        }

        try
        {
            var existingOtp = await _otpStore.GetOtpAsync(tenantId, sessionId, phone, ct);
            if (existingOtp?.IsExpired(nowUtc) == true)
            {
                await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);
                existingOtp = null;
            }

            if (existingOtp?.IsVerificationBlocked == true)
            {
                await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);
                existingOtp = null;
            }

            if (existingOtp != null &&
                string.Equals(existingOtp.State, CheckoutPhoneOtpStates.Verified, StringComparison.OrdinalIgnoreCase))
            {
                _telemetry.RecordOtpRequest(tenantId, "whatsapp", "accepted", "already_verified");
                return new PhoneVerificationResult
                {
                    Success = true,
                    Message = "Phone already verified"
                };
            }

            if (existingOtp != null)
            {
                var retryAfter = GetRetryAfterSeconds(existingOtp, nowUtc);
                var expiresIn = GetExpiresInSeconds(existingOtp, nowUtc);

                if (retryAfter > 0)
                {
                    _telemetry.RecordOtpRateLimited(tenantId, "cooldown", "resend_cooldown");
                    _telemetry.RecordOtpRequest(tenantId, "whatsapp", "accepted", "existing_code_reused");

                    return new PhoneVerificationResult
                    {
                        Success = true,
                        Message = "A verification code is already active. Please use the current code or resend after the cooldown.",
                        ExpiresInSeconds = expiresIn,
                        RetryAfterSeconds = retryAfter
                    };
                }
            }

            var exceededRateLimit = await GetExceededRateLimitAsync(tenantId, rateLimitKeys, ct);
            if (exceededRateLimit.HasValue)
            {
                var scopeLabel = GetRateLimitScopeLabel(exceededRateLimit.Value.Key.Scope);
                _logger.LogWarning(
                    "[CheckoutPhoneOtp] Rate limit exceeded | TenantId: {TenantId} | SessionId: {SessionId} | Scope: {Scope} | Phone: {Phone} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    scopeLabel,
                    MaskPhone(phone),
                    traceId);

                _telemetry.RecordOtpRequest(tenantId, "whatsapp", "rejected", "rate_limit_exceeded");
                _telemetry.RecordOtpRateLimited(tenantId, scopeLabel, "otp_request_rate_limit");
                _telemetry.RecordOtpAbuseDetected(tenantId, scopeLabel, "otp_request_rate_limit");

                return CreateFailureResult(
                    "RATE_LIMIT_EXCEEDED",
                    $"Too many verification attempts. Please try again in {RateLimitWindowMinutes} minutes.",
                    retryAfterSeconds: exceededRateLimit.Value.RetryAfterSeconds);
            }

            var previousOtp = existingOtp;
            var otpCode = GenerateOtp();
            var otpRecord = new CheckoutPhoneOtpRecord
            {
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.AddMinutes(OtpExpiryMinutes),
                DeliveryChannel = "whatsapp",
                MaxVerificationAttempts = existingOtp?.MaxVerificationAttempts ?? 5,
                DispatchCount = (existingOtp?.DispatchCount ?? 0) + 1,
                State = CheckoutPhoneOtpStates.Sent,
                LastRequestedAtUtc = nowUtc,
                CodeSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            };
            otpRecord.CodeHash = HashOtp(tenantId, sessionId, phone, otpCode, otpRecord.CodeSalt);

            var otpTtl = otpRecord.ExpiresAtUtc - nowUtc;
            if (otpTtl <= TimeSpan.Zero)
            {
                _telemetry.RecordOtpRequest(tenantId, "whatsapp", "rejected", "otp_expired_before_send");
                return CreateFailureResult(
                    "OTP_EXPIRED",
                    "Verification code has expired. Please request a new one.");
            }

            var stored = await _otpStore.SaveOtpAsync(tenantId, sessionId, phone, otpRecord, otpTtl, ct);
            if (!stored)
            {
                _logger.LogError(
                    "[CheckoutPhoneOtp] Failed to persist OTP before publish | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone),
                    traceId);

                _telemetry.RecordOtpRequest(tenantId, "whatsapp", "rejected", "otp_persist_failed");

                return CreateFailureResult(
                    "OTP_REQUEST_FAILED",
                    "Verification service is currently unavailable. Please try again later.");
            }

            try
            {
                var otpEvent = new WhatsAppOtpRequestedEvent
                {
                    TenantId = tenantId,
                    CustomerId = Guid.Empty,
                    Phone = phone,
                    OtpCode = otpCode,
                    ExpiryMinutes = Math.Max(1, (int)Math.Ceiling(otpTtl.TotalMinutes)),
                    Purpose = "CHECKOUT_VERIFICATION"
                };

                _logger.LogInformation(
                    "[CheckoutPhoneOtp] Publishing WhatsAppOtpRequestedEvent | TenantId: {TenantId} | EventId: {EventId} | SessionId: {SessionId} | Phone: {Phone}",
                    tenantId,
                    otpEvent.EventId,
                    sessionId,
                    MaskPhone(phone));

                await _eventPublisher.PublishSensitiveAsync(otpEvent, ct);
            }
            catch (Exception ex)
            {
                if (previousOtp == null)
                {
                    await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);
                }
                else
                {
                    var previousTtl = previousOtp.ExpiresAtUtc - DateTime.UtcNow;
                    if (previousTtl > TimeSpan.Zero)
                    {
                        await _otpStore.SaveOtpAsync(tenantId, sessionId, phone, previousOtp, previousTtl, ct);
                    }
                    else
                    {
                        await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);
                    }
                }

                _logger.LogError(
                    ex,
                    "[CheckoutPhoneOtp] Failed to publish OTP event | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone),
                    traceId);

                _telemetry.RecordOtpRequest(tenantId, "whatsapp", "rejected", "event_publish_failed");
                _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "event_publish_failed");

                return CreateFailureResult(
                    "OTP_REQUEST_FAILED",
                    "Verification service is currently unavailable. Please try again later.");
            }

            foreach (var rateLimitKey in rateLimitKeys)
            {
                await _otpStore.IncrementRateLimitAsync(
                    tenantId,
                    rateLimitKey,
                    TimeSpan.FromMinutes(RateLimitWindowMinutes),
                    ct);
            }

            _telemetry.RecordOtpRequest(
                tenantId,
                "whatsapp",
                "accepted",
                previousOtp == null ? "new_code" : "existing_code_resent");

            _logger.LogInformation(
                "[CheckoutPhoneOtp] OTP request accepted | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | DispatchCount: {DispatchCount} | TraceId: {TraceId}",
                tenantId,
                sessionId,
                MaskPhone(phone),
                otpRecord.DispatchCount,
                traceId);

            return new PhoneVerificationResult
            {
                Success = true,
                Message = previousOtp == null
                    ? "Verification code sent to your WhatsApp."
                    : "Verification code resent to your WhatsApp.",
                ExpiresInSeconds = GetExpiresInSeconds(otpRecord, nowUtc),
                RetryAfterSeconds = OtpResendCooldownSeconds
            };
        }
        finally
        {
            try
            {
                await _otpStore.ReleaseLockAsync(tenantId, sessionId, phone, lockToken, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Checkout OTP] Failed to release lock (best-effort) | TenantId: {TenantId} | SessionId: {SessionId}",
                    tenantId, sessionId);
            }
        }
    }

    public async Task<PhoneVerificationResult> VerifyOtpAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        string otpCode,
        string? clientIp = null,
        Guid? customerId = null,
        CancellationToken ct = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        var nowUtc = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(otpCode))
        {
            _telemetry.RecordOtpVerificationFailed(tenantId, "invalid_request");
            return CreateFailureResult("INVALID_REQUEST", "Phone and OTP code are required");
        }

        var normalization = await NormalizePhoneForOtpAsync(tenantId, phone, traceId, "verify", ct);
        if (normalization.ErrorResult != null)
        {
            _telemetry.RecordOtpVerificationFailed(tenantId, normalization.ErrorResult.Error ?? "invalid_phone");
            return normalization.ErrorResult;
        }

        phone = normalization.NormalizedPhone!;
        var rateLimitKeys = BuildRateLimitKeys(sessionId, phone, clientIp);

        var customerScopeId = customerId.HasValue && customerId.Value != Guid.Empty
            ? customerId.Value.ToString("N")
            : null;

        if (await _otpStore.IsVerifiedAsync(tenantId, sessionId, phone, ct) ||
            (!string.IsNullOrWhiteSpace(customerScopeId) &&
             await _otpStore.IsVerifiedAsync(tenantId, customerScopeId, phone, ct)))
        {
            return new PhoneVerificationResult
            {
                Success = true,
                Message = "Phone already verified"
            };
        }

        var lockToken = Guid.NewGuid().ToString("N");
        var lockAcquired = await _otpStore.TryAcquireLockAsync(
            tenantId,
            sessionId,
            phone,
            lockToken,
            TimeSpan.FromSeconds(OtpLockTtlSeconds),
            ct);

        if (!lockAcquired)
        {
            _telemetry.RecordOtpVerificationFailed(tenantId, "otp_verification_in_progress");

            return CreateFailureResult(
                "OTP_VERIFICATION_IN_PROGRESS",
                "Phone verification is already in progress. Please wait a moment and try again.");
        }

        try
        {
            var otpRecord = await _otpStore.GetOtpAsync(tenantId, sessionId, phone, ct);
            if (otpRecord == null)
            {
                _logger.LogWarning(
                    "[CheckoutPhoneOtp] OTP not found | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone),
                    traceId);

                _telemetry.RecordOtpVerificationFailed(tenantId, "otp_expired");

                return CreateFailureResult(
                    "OTP_EXPIRED",
                    "Verification code has expired. Please request a new one.");
            }

            if (otpRecord.IsExpired(nowUtc))
            {
                await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);

                _logger.LogWarning(
                    "[CheckoutPhoneOtp] OTP expired | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone),
                    traceId);

                _telemetry.RecordOtpVerificationFailed(tenantId, "otp_expired");

                return CreateFailureResult(
                    "OTP_EXPIRED",
                    "Verification code has expired. Please request a new one.");
            }

            if (otpRecord.IsVerificationBlocked)
            {
                await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);

                _logger.LogWarning(
                    "[CheckoutPhoneOtp] OTP verification blocked - attempts exhausted | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | Attempts: {Attempts} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone),
                    otpRecord.VerificationAttempts,
                    traceId);

                _telemetry.RecordOtpVerificationFailed(tenantId, "otp_attempts_exhausted");
                _telemetry.RecordOtpVerificationBlocked(tenantId, "otp_attempts_exhausted");
                _telemetry.RecordOtpAbuseDetected(tenantId, "verification", "otp_attempts_exhausted");

                return CreateFailureResult(
                    "OTP_ATTEMPTS_EXCEEDED",
                    "Too many invalid attempts. Please request a new verification code.");
            }

            if (string.Equals(otpRecord.State, CheckoutPhoneOtpStates.Verified, StringComparison.OrdinalIgnoreCase))
            {
                return new PhoneVerificationResult
                {
                    Success = true,
                    Message = "Phone already verified"
                };
            }

            if (!string.Equals(otpRecord.State, CheckoutPhoneOtpStates.Sent, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[CheckoutPhoneOtp] OTP not in sent state | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | State: {State} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone),
                    otpRecord.State,
                    traceId);

                _telemetry.RecordOtpVerificationFailed(tenantId, "otp_not_sent");

                return CreateFailureResult(
                    "OTP_NOT_AVAILABLE",
                    "Verification code is not available. Please request a new one.");
            }

            if (string.IsNullOrWhiteSpace(otpRecord.CodeHash) ||
                string.IsNullOrWhiteSpace(otpRecord.CodeSalt))
            {
                await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);
                return CreateFailureResult(
                    "OTP_NOT_AVAILABLE",
                    "Verification code is not available. Please request a new one.");
            }

            bool hashMatches;
            try
            {
                var submittedHash = HashOtp(tenantId, sessionId, phone, otpCode, otpRecord.CodeSalt);
                hashMatches = CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(submittedHash),
                    Convert.FromBase64String(otpRecord.CodeHash));
            }
            catch (FormatException)
            {
                await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);
                return CreateFailureResult(
                    "OTP_NOT_AVAILABLE",
                    "Verification code is not available. Please request a new one.");
            }

            if (!hashMatches)
            {
                otpRecord.VerificationAttempts += 1;
                var otpTtl = otpRecord.ExpiresAtUtc - nowUtc;
                var attemptsRemaining = Math.Max(0, otpRecord.MaxVerificationAttempts - otpRecord.VerificationAttempts);

                if (otpRecord.IsVerificationBlocked || otpTtl <= TimeSpan.Zero)
                {
                    await _otpStore.DeleteOtpAsync(tenantId, sessionId, phone, ct);

                    _logger.LogWarning(
                        "[CheckoutPhoneOtp] OTP invalidated after repeated failures | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | Attempts: {Attempts} | TraceId: {TraceId}",
                        tenantId,
                        sessionId,
                        MaskPhone(phone),
                        otpRecord.VerificationAttempts,
                        traceId);

                    _telemetry.RecordOtpVerificationFailed(tenantId, "otp_attempts_exhausted");
                    _telemetry.RecordOtpVerificationBlocked(tenantId, "otp_attempts_exhausted");
                    _telemetry.RecordOtpAbuseDetected(tenantId, "verification", "otp_attempts_exhausted");

                    return CreateFailureResult(
                        "OTP_ATTEMPTS_EXCEEDED",
                        "Too many invalid attempts. Please request a new verification code.");
                }

                var updatedOtpSaved = await _otpStore.SaveOtpAsync(tenantId, sessionId, phone, otpRecord, otpTtl, ct);
                if (!updatedOtpSaved)
                {
                    _logger.LogWarning(
                        "[CheckoutPhoneOtp] Failed to persist OTP attempt count | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone}",
                        tenantId,
                        sessionId,
                        MaskPhone(phone));
                }

                _logger.LogWarning(
                    "[CheckoutPhoneOtp] Invalid OTP | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone} | AttemptsRemaining: {AttemptsRemaining} | TraceId: {TraceId}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone),
                    attemptsRemaining,
                    traceId);

                _telemetry.RecordOtpVerificationFailed(tenantId, "invalid_otp");

                return CreateFailureResult(
                    "INVALID_OTP",
                    attemptsRemaining == 1
                        ? "Invalid verification code. 1 attempt remaining."
                        : $"Invalid verification code. {attemptsRemaining} attempts remaining.");
            }

            var verificationTtl = TimeSpan.FromHours(SessionVerificationTtlHours);
            var verificationScopes = new List<string> { sessionId };
            if (!string.IsNullOrWhiteSpace(customerScopeId))
            {
                verificationScopes.Add(customerScopeId);
            }

            var storedScopes = new List<string>();
            foreach (var scopeId in verificationScopes)
            {
                var stored = await _otpStore.SetVerifiedAsync(tenantId, scopeId, phone, verificationTtl, ct);
                if (!stored)
                {
                    foreach (var storedScopeId in storedScopes)
                    {
                        await _otpStore.DeleteVerifiedAsync(tenantId, storedScopeId, phone, ct);
                    }

                    _logger.LogError(
                        "[CheckoutPhoneOtp] Failed to persist verification scope | TenantId: {TenantId} | ScopeId: {ScopeId} | Phone: {Phone} | TraceId: {TraceId}",
                        tenantId,
                        scopeId,
                        MaskPhone(phone),
                        traceId);

                    _telemetry.RecordOtpVerificationFailed(tenantId, "verification_state_persist_failed");

                    return CreateFailureResult(
                        "VERIFY_FAILED",
                        "Verification service is currently unavailable. Please try again later.");
                }

                storedScopes.Add(scopeId);
            }

            otpRecord.State = CheckoutPhoneOtpStates.Verified;
            otpRecord.CodeHash = string.Empty;
            otpRecord.CodeSalt = string.Empty;
            otpRecord.VerifiedAtUtc = nowUtc;
            otpRecord.VerificationAttempts = 0;

            var otpRecordSaved = await _otpStore.SaveOtpAsync(tenantId, sessionId, phone, otpRecord, verificationTtl, ct);
            if (!otpRecordSaved)
            {
                _logger.LogWarning(
                    "[CheckoutPhoneOtp] Verified scope persisted but OTP record update failed | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone}",
                    tenantId,
                    sessionId,
                    MaskPhone(phone));
            }

            await ClearRateLimitKeysAsync(tenantId, rateLimitKeys, ct);

            _logger.LogInformation(
                "[CheckoutPhoneOtp] Phone verified successfully | TenantId: {TenantId} | SessionId: {SessionId} | CustomerId: {CustomerId} | Phone: {Phone} | TraceId: {TraceId}",
                tenantId,
                sessionId,
                customerId,
                MaskPhone(phone),
                traceId);

            await EmitCartActivityEventAsync(tenantId, customerId, phone, sessionId, ct);

            return new PhoneVerificationResult
            {
                Success = true,
                Message = "Phone verified successfully"
            };
        }
        finally
        {
            try
            {
                await _otpStore.ReleaseLockAsync(tenantId, sessionId, phone, lockToken, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Checkout OTP] Failed to release lock (best-effort) | TenantId: {TenantId} | SessionId: {SessionId}",
                    tenantId, sessionId);
            }
        }
    }

    public async Task<bool> IsPhoneVerifiedForSessionAsync(
        Guid tenantId,
        string sessionId,
        string phone,
        CancellationToken ct = default)
    {
        var originalPhone = phone;
        var normalizedPhone = await TryNormalizePhoneAsync(tenantId, phone, ct);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            _logger.LogWarning(
                "[CheckoutPhoneOtp] Verification lookup failed to normalize phone | TenantId: {TenantId} | SessionId: {SessionId} | Phone: {Phone}",
                tenantId,
                sessionId,
                MaskPhone(originalPhone));

            return false;
        }

        var isOtpRequired = await _featureGate.IsOtpVerificationEnabledAsync(tenantId, ct);
        if (!isOtpRequired)
        {
            return true;
        }

        var isVerified = await _otpStore.IsVerifiedAsync(tenantId, sessionId, normalizedPhone, ct);

        _logger.LogInformation(
            "[CheckoutPhoneOtp] Verification lookup | TenantId: {TenantId} | SessionId: {SessionId} | OriginalPhone: {OriginalPhone} | NormalizedPhone: {NormalizedPhone} | Found: {Found}",
            tenantId,
            sessionId,
            MaskPhone(originalPhone),
            MaskPhone(normalizedPhone),
            isVerified);

        return isVerified;
    }

    private async Task<(string? NormalizedPhone, PhoneVerificationResult? ErrorResult)> NormalizePhoneForOtpAsync(
        Guid tenantId,
        string phone,
        string traceId,
        string operation,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return (null, CreateFailureResult("INVALID_PHONE", "Valid phone number is required"));
        }

        try
        {
            var normalizedPhone = await _phoneNormalizer.NormalizeAsync(phone, tenantId, ct);
            return (normalizedPhone, null);
        }
        catch (PhoneNumberNormalizationException ex)
        {
            _logger.LogWarning(
                "[CheckoutPhoneOtp] Phone normalization failed during {Operation} | TenantId: {TenantId} | Phone: {Phone} | Reason: {Reason} | TraceId: {TraceId}",
                operation,
                tenantId,
                MaskPhone(phone),
                ex.Reason,
                traceId);

            return (null, CreateFailureResult("INVALID_PHONE", "Valid phone number is required"));
        }
    }

    private async Task<string?> TryNormalizePhoneAsync(Guid tenantId, string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        try
        {
            return await _phoneNormalizer.NormalizeAsync(phone, tenantId, ct);
        }
        catch (PhoneNumberNormalizationException)
        {
            return null;
        }
    }

    private static PhoneVerificationResult CreateFailureResult(
        string errorCode,
        string message,
        int? retryAfterSeconds = null,
        int? expiresInSeconds = null)
    {
        return new PhoneVerificationResult
        {
            Success = false,
            Error = errorCode,
            Message = message,
            RetryAfterSeconds = retryAfterSeconds,
            ExpiresInSeconds = expiresInSeconds
        };
    }

    private static int GetExpiresInSeconds(CheckoutPhoneOtpRecord record, DateTime utcNow)
    {
        return Math.Max(0, (int)Math.Ceiling((record.ExpiresAtUtc - utcNow).TotalSeconds));
    }

    private static int GetRetryAfterSeconds(CheckoutPhoneOtpRecord record, DateTime utcNow)
    {
        var retryAtUtc = record.LastRequestedAtUtc.AddSeconds(OtpResendCooldownSeconds);
        return Math.Max(0, (int)Math.Ceiling((retryAtUtc - utcNow).TotalSeconds));
    }

    private static string GenerateOtp()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        var number = BitConverter.ToUInt32(bytes) % 1_000_000;
        return number.ToString($"D{OtpLength}");
    }

    private static string HashOtp(
        Guid tenantId,
        string sessionId,
        string phone,
        string code,
        string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var value = System.Text.Encoding.UTF8.GetBytes($"{tenantId:N}\n{sessionId}\n{phone}\n{code}");
        return Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
            value,
            saltBytes,
            OtpHashIterations,
            HashAlgorithmName.SHA256,
            32));
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
        {
            return "***";
        }

        return phone[..4] + "****" + phone[^3..];
    }

    private static string? NormalizeClientIp(string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return null;
        }

        return clientIp.Trim();
    }

    private static IReadOnlyList<CheckoutOtpRateLimitKey> BuildRateLimitKeys(
        string sessionId,
        string phone,
        string? clientIp)
    {
        var keys = new List<CheckoutOtpRateLimitKey>
        {
            new(CheckoutOtpRateLimitScope.PhoneNumber, phone),
            new(CheckoutOtpRateLimitScope.SessionId, sessionId)
        };

        var normalizedClientIp = NormalizeClientIp(clientIp);
        if (!string.IsNullOrWhiteSpace(normalizedClientIp))
        {
            keys.Add(new CheckoutOtpRateLimitKey(CheckoutOtpRateLimitScope.IpAddress, normalizedClientIp));
        }

        return keys;
    }

    private static string GetRateLimitScopeLabel(CheckoutOtpRateLimitScope scope) => scope switch
    {
        CheckoutOtpRateLimitScope.PhoneNumber => "phone_number",
        CheckoutOtpRateLimitScope.SessionId => "session_id",
        CheckoutOtpRateLimitScope.IpAddress => "ip_address",
        _ => "unknown"
    };

    private async Task<(CheckoutOtpRateLimitKey Key, int RetryAfterSeconds)?> GetExceededRateLimitAsync(
        Guid tenantId,
        IReadOnlyList<CheckoutOtpRateLimitKey> keys,
        CancellationToken ct)
    {
        foreach (var key in keys)
        {
            var count = await _otpStore.GetRateLimitCountAsync(tenantId, key, ct);
            if (count < MaxOtpRequestsPerWindow)
            {
                continue;
            }

            var ttl = await _otpStore.GetRateLimitTimeToLiveAsync(tenantId, key, ct);
            var retryAfterSeconds = ttl.HasValue && ttl.Value > TimeSpan.Zero
                ? Math.Max(1, (int)Math.Ceiling(ttl.Value.TotalSeconds))
                : RateLimitWindowMinutes * 60;

            return (key, retryAfterSeconds);
        }

        return null;
    }

    private async Task ClearRateLimitKeysAsync(
        Guid tenantId,
        IReadOnlyList<CheckoutOtpRateLimitKey> keys,
        CancellationToken ct)
    {
        foreach (var key in keys)
        {
            await _otpStore.ClearRateLimitAsync(tenantId, key, ct);
        }
    }

    private static string? DetermineContactChannel(string? phone, string? email)
    {
        var hasPhone = !string.IsNullOrWhiteSpace(phone);
        var hasEmail = !string.IsNullOrWhiteSpace(email);

        return (hasPhone, hasEmail) switch
        {
            (true, true) => "phone,email",
            (true, false) => "phone",
            (false, true) => "email",
            _ => null
        };
    }

    /// <summary>
    /// Emits CartActivityEvent for abandoned cart tracking after phone verification.
    /// This is the critical integration point for Smart Abandoned Cart Recovery.
    /// </summary>
    private async Task EmitCartActivityEventAsync(
        Guid tenantId,
        Guid? customerId,
        string verifiedPhone,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var hasCustomerId = customerId.HasValue && customerId.Value != Guid.Empty;
            if (!hasCustomerId && string.IsNullOrWhiteSpace(sessionId))
            {
                _logger.LogWarning(
                    "[Abandoned Cart] Skipping activity tracking - owner context missing | TenantId: {TenantId} | SessionId: {SessionId}",
                    tenantId,
                    sessionId);
                _telemetry.RecordAbandonedCartSkipped(tenantId, "phone_verification", "missing_owner_context");
                return;
            }

            var cartOwner = new CartOwner
            {
                OwnerId = hasCustomerId
                    ? customerId!.Value.ToString("N")
                    : sessionId,
                OwnerType = hasCustomerId ? "customer" : "session"
            };

            var correlationId = Activity.Current?.Id ?? sessionId;
            var cart = await _cartService.GetCartAsync(tenantId, cartOwner, correlationId, ct);
            if (cart == null || cart.Items == null || !cart.Items.Any())
            {
                _logger.LogInformation(
                    "[Abandoned Cart] Skipping activity tracking - cart empty | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId}",
                    tenantId,
                    customerId,
                    sessionId);
                _telemetry.RecordAbandonedCartSkipped(tenantId, "phone_verification", "empty_cart");
                return;
            }

            var snapshotJson = JsonSerializer.Serialize(
                RecoveredCartSnapshotFactory.FromCartDto(cart),
                _jsonOptions);

            string? customerEmail = null;
            var emailVerified = false;
            if (hasCustomerId)
            {
                var customerContact = await _dbContext.OnlineCustomers
                    .Where(c => c.Id == customerId!.Value && c.TenantId == tenantId)
                    .Select(c => new
                    {
                        c.Email,
                        c.EmailVerified
                    })
                    .FirstOrDefaultAsync(ct);

                customerEmail = customerContact?.Email;
                emailVerified = customerContact?.EmailVerified == true;
            }

            var evt = new CartActivityEvent
            {
                TenantId = tenantId,
                CartId = cart.CartId,
                SessionId = sessionId,
                CustomerId = customerId,
                PhoneNumber = verifiedPhone,
                Email = customerEmail,
                ContactChannel = DetermineContactChannel(verifiedPhone, emailVerified ? customerEmail : null),
                PhoneVerified = true,
                EmailVerified = emailVerified,
                CartSnapshotJson = snapshotJson,
                OccurredAtUtc = DateTime.UtcNow
            };

            await _eventPublisher.PublishAsync(evt, ct);

            _logger.LogInformation(
                "[Abandoned Cart] Cart activity tracked after phone verification | TenantId: {TenantId} | CartId: {CartId} | CustomerId: {CustomerId} | SessionId: {SessionId} | Phone: {Phone}",
                tenantId,
                cart.CartId,
                customerId,
                sessionId,
                MaskPhone(verifiedPhone));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[Abandoned Cart] Failed to emit CartActivityEvent | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId}",
                tenantId,
                customerId,
                sessionId);
            _telemetry.RecordAbandonedCartSkipped(tenantId, "phone_verification", "publish_failed");
        }
    }
}
