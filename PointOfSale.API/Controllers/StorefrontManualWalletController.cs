using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/storefront/payments/manual-wallet")]
public class StorefrontManualWalletController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly IR2StorageService _storageService;
    private readonly IPaymentMethodReadRepository _paymentMethodReadRepository;
    private readonly IManualPaymentCapabilityAdmissionAuthority _manualPaymentCapabilityAdmissionAuthority;
    private readonly IFileValidationService _fileValidationService;
    private readonly ICheckoutSessionService _checkoutSessionService;
    private readonly R2Options _r2Options;
    private readonly ManualPaymentOptions _manualPaymentOptions;
    private readonly ILogger<StorefrontManualWalletController> _logger;

    public StorefrontManualWalletController(
        ITenantContext tenantContext,
        IR2StorageService storageService,
        IPaymentMethodReadRepository paymentMethodReadRepository,
        IManualPaymentCapabilityAdmissionAuthority manualPaymentCapabilityAdmissionAuthority,
        IFileValidationService fileValidationService,
        ICheckoutSessionService checkoutSessionService,
        IOptions<R2Options> r2Options,
        IOptions<ManualPaymentOptions> manualPaymentOptions,
        ILogger<StorefrontManualWalletController> logger)
    {
        _tenantContext = tenantContext;
        _storageService = storageService;
        _paymentMethodReadRepository = paymentMethodReadRepository;
        _manualPaymentCapabilityAdmissionAuthority = manualPaymentCapabilityAdmissionAuthority;
        _fileValidationService = fileValidationService;
        _checkoutSessionService = checkoutSessionService;
        _r2Options = r2Options.Value;
        _manualPaymentOptions = manualPaymentOptions.Value;
        _logger = logger;
    }

    [HttpGet("config")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicReadRateLimit")]
    [ProducesResponseType(typeof(ManualWalletConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        ManualPaymentTenantConfiguration tenantConfiguration;
        try
        {
            tenantConfiguration = await _manualPaymentCapabilityAdmissionAuthority.AdmitAsync(
                tenantId,
                ManualPaymentOperationalCapability.ReferencePaymentInitialization,
                requestedChannel: null,
                cancellationToken);
        }
        catch (ManualPaymentApplicationException ex)
        {
            return ManualPaymentAdmissionRejected(ex, traceId);
        }
        
        var walletMethod = await _paymentMethodReadRepository.GetByCodeAsync(
            tenantId,
            "MANUALWALLET",
            cancellationToken);

        var walletNumber = walletMethod?.PublicConfiguration.GetValueOrDefault("walletNumber");

        if (walletMethod == null ||
            walletMethod.IsConfigurationValid != true ||
            string.IsNullOrWhiteSpace(walletNumber))
        {
             return NotFound(new ApiErrorResponse(
                 "MANUAL_WALLET_NOT_CONFIGURED",
                 "Payment method unavailable",
                 traceId));
        }

        var configuredProofTypes = ResolveConfiguredProofFileTypes();
        var walletTypes = tenantConfiguration.SupportedChannels
            .Where(channel => channel.Enabled)
            .Select(MapStorefrontWalletType)
            .Where(walletType => walletType != null)
            .Cast<ManualWalletTypeResponse>()
            .ToArray();

        if (walletTypes.Length == 0)
        {
            return NotFound(new ApiErrorResponse(
                "MANUAL_WALLET_CHANNEL_UNAVAILABLE",
                "Payment method unavailable",
                traceId));
        }

        return Ok(new ManualWalletConfigResponse
        {
            WalletNumber = walletNumber,
            WalletTypes = walletTypes,
            AllowedMimeTypes = FileValidationService.GetAllowedMimeTypes(configuredProofTypes),
            AllowedExtensions = FileValidationService.GetAllowedExtensions(configuredProofTypes),
            MaxFileBytes = _manualPaymentOptions.Proof.MaximumUploadBytes
        });
    }

    [HttpPost("upload-proof")]
    [Authorize(AuthenticationSchemes = ManualWalletUploadTokenAuthenticationDefaults.SchemeName)]
    [EnableRateLimiting("UploadProofPolicy")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ManualWalletUploadProofResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadProof(
        [FromForm] UploadManualWalletProofRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var tenantId = _tenantContext.RequiredTenantId;
        var tokenCheckoutSessionId = User.FindFirstValue(ManualWalletUploadTokenAuthenticationDefaults.CheckoutSessionIdClaimType);
        var tokenRecoveryNonce = User.FindFirstValue(ManualWalletUploadTokenAuthenticationDefaults.RecoveryNonceClaimType);

        if (!Guid.TryParse(tokenCheckoutSessionId, out var authenticatedCheckoutSessionId) ||
            authenticatedCheckoutSessionId != request.CheckoutSessionId)
        {
            return BadRequest(new ApiErrorResponse(
                "INVALID_CHECKOUT_CONTEXT",
                "Proof upload requires a valid checkout session context.",
                traceId));
        }

        var session = await _checkoutSessionService.ResolveActiveSessionAsync(
            request.CheckoutSessionId,
            tenantId,
            cancellationToken);

        if (session == null ||
            session.Status != Core.Enums.CheckoutSessionStatus.Active ||
            !SecurityUtilities.FixedTimeEquals(session.RecoveryToken, tokenRecoveryNonce))
        {
            return NotFound(new ApiErrorResponse(
                "CHECKOUT_SESSION_NOT_FOUND",
                "Checkout session not found or no longer active.",
                traceId));
        }

        try
        {
            await _manualPaymentCapabilityAdmissionAuthority.AdmitAsync(
                tenantId,
                ManualPaymentOperationalCapability.ProofUpload,
                requestedChannel: null,
                cancellationToken);
        }
        catch (ManualPaymentApplicationException ex)
        {
            return ManualPaymentAdmissionRejected(ex, traceId);
        }

        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(new ApiErrorResponse(
                "MANUAL_WALLET_PROOF_EMPTY",
                "The uploaded proof file is empty.",
                traceId));
        }

        if (request.File.Length > _manualPaymentOptions.Proof.MaximumUploadBytes)
        {
            return BadRequest(new ApiErrorResponse(
                "MANUAL_WALLET_PROOF_TOO_LARGE",
                $"Proof files must be {_manualPaymentOptions.Proof.MaximumUploadBytes / (1024 * 1024)} MB or smaller.",
                traceId));
        }

        FileValidationResult validationResult;
        using (var stream = request.File.OpenReadStream())
        {
            validationResult = await _fileValidationService.ValidateFileAsync(
                stream, 
                request.File.FileName, 
                request.File.ContentType,
                ResolveConfiguredProofFileTypes(),
                cancellationToken);
        }

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "File validation failed | TenantId: {TenantId} | FileName: {FileName} | Error: {Error} | TraceId: {TraceId}",
                tenantId, request.File.FileName, validationResult.ErrorMessage, traceId);
            return BadRequest(new ApiErrorResponse(
                "INVALID_MANUAL_WALLET_PROOF",
                validationResult.ErrorMessage ?? "Unsupported or invalid file format.",
                traceId));
        }

        string? uploadedObjectKey = null;
        try
        {
            if (string.IsNullOrWhiteSpace(validationResult.CanonicalContentType))
            {
                return BadRequest(new ApiErrorResponse(
                    "INVALID_MANUAL_WALLET_PROOF",
                    "Unsupported or invalid file format.",
                    traceId));
            }

            var uploadPath = $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{session.Id:D}";

            await using var uploadStream = request.File.OpenReadStream();
            var uploadResult = await _storageService.UploadFileAsync(
                uploadStream,
                validationResult.CanonicalContentType,
                uploadPath,
                cancellationToken);
            uploadedObjectKey = uploadResult.ObjectKey;

            // Validate and normalize the newly uploaded key before persisting
            if (!ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                uploadResult.ObjectKey,
                tenantId,
                _r2Options,
                out var normalizedUploadedKey))
            {
                // Storage misconfiguration - delete the uploaded object and return error
                await _storageService.DeleteObjectAsync(uploadResult.ObjectKey, cancellationToken);
                _logger.LogError(
                    "Uploaded manual wallet proof key is not tenant-scoped | TenantId: {TenantId} | CheckoutSessionId: {CheckoutSessionId} | ObjectKey: {ObjectKey} | TraceId: {TraceId}",
                    tenantId,
                    session.Id,
                    uploadResult.ObjectKey,
                    traceId);
                return StatusCode(500, new ApiErrorResponse(
                    "STORAGE_CONFIGURATION_ERROR",
                    "Storage configuration error. Please contact support.",
                    traceId));
            }

            var previousProofObjectKey = ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
                session.ProofImageUrl,
                tenantId,
                _r2Options,
                out var existingObjectKey)
                ? existingObjectKey
                : null;

            var updatedSession = await _checkoutSessionService.SetManualWalletProofAsync(
                session.Id,
                tenantId,
                normalizedUploadedKey,
                cancellationToken);

            if (updatedSession == null)
            {
                await _storageService.DeleteObjectAsync(normalizedUploadedKey, cancellationToken);
                return NotFound(new ApiErrorResponse(
                    "CHECKOUT_SESSION_NOT_FOUND",
                    "Checkout session not found or no longer active.",
                    traceId));
            }

            if (!string.IsNullOrWhiteSpace(previousProofObjectKey) &&
                !string.Equals(previousProofObjectKey, normalizedUploadedKey, StringComparison.Ordinal))
            {
                try
                {
                    await _storageService.DeleteObjectAsync(previousProofObjectKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to delete replaced manual wallet proof | TenantId: {TenantId} | CheckoutSessionId: {CheckoutSessionId} | ObjectKey: {ObjectKey} | TraceId: {TraceId}",
                        tenantId,
                        session.Id,
                        previousProofObjectKey,
                        traceId);
                }
            }

            _logger.LogInformation(
                "Manual wallet proof uploaded to private storage | TenantId: {TenantId} | CheckoutSessionId: {CheckoutSessionId} | ObjectKey: {ObjectKey} | TraceId: {TraceId}",
                tenantId,
                session.Id,
                normalizedUploadedKey,
                traceId);

            return Ok(new ManualWalletUploadProofResponse
            {
                FileKey = normalizedUploadedKey,
                CanonicalContentType = validationResult.CanonicalContentType
            });
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(uploadedObjectKey))
            {
                try
                {
                    await _storageService.DeleteObjectAsync(uploadedObjectKey, cancellationToken);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(
                        cleanupEx,
                        "Failed to delete manual wallet proof after upload error | TenantId: {TenantId} | CheckoutSessionId: {CheckoutSessionId} | ObjectKey: {ObjectKey} | TraceId: {TraceId}",
                        tenantId,
                        session.Id,
                        uploadedObjectKey,
                        traceId);
                }
            }

            _logger.LogError(ex, "Failed to upload manual wallet proof | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);
            return StatusCode(500, new ApiErrorResponse(
                "MANUAL_WALLET_UPLOAD_FAILED",
                "The proof upload could not be completed. Please retry.",
                traceId));
        }
    }

    [HttpDelete("clear-proof")]
    [Authorize(AuthenticationSchemes = ManualWalletUploadTokenAuthenticationDefaults.SchemeName)]
    [EnableRateLimiting("UploadProofPolicy")]
    [ProducesResponseType(typeof(ManualWalletClearProofResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearProof(
        [FromQuery] Guid checkoutSessionId,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var tenantId = _tenantContext.RequiredTenantId;
        var tokenCheckoutSessionId = User.FindFirstValue(ManualWalletUploadTokenAuthenticationDefaults.CheckoutSessionIdClaimType);
        var tokenRecoveryNonce = User.FindFirstValue(ManualWalletUploadTokenAuthenticationDefaults.RecoveryNonceClaimType);

        if (!Guid.TryParse(tokenCheckoutSessionId, out var authenticatedCheckoutSessionId) ||
            authenticatedCheckoutSessionId != checkoutSessionId)
        {
            return BadRequest(new ApiErrorResponse(
                "INVALID_CHECKOUT_CONTEXT",
                "Proof clear requires a valid checkout session context.",
                traceId));
        }

        var session = await _checkoutSessionService.ResolveActiveSessionAsync(
            checkoutSessionId,
            tenantId,
            cancellationToken);

        if (session == null ||
            session.Status != Core.Enums.CheckoutSessionStatus.Active ||
            !SecurityUtilities.FixedTimeEquals(session.RecoveryToken, tokenRecoveryNonce))
        {
            return NotFound(new ApiErrorResponse(
                "CHECKOUT_SESSION_NOT_FOUND",
                "Checkout session not found or no longer active.",
                traceId));
        }

        try
        {
            await _manualPaymentCapabilityAdmissionAuthority.AdmitAsync(
                tenantId,
                ManualPaymentOperationalCapability.ProofClear,
                requestedChannel: null,
                cancellationToken);
        }
        catch (ManualPaymentApplicationException ex)
        {
            return ManualPaymentAdmissionRejected(ex, traceId);
        }

        var previousProofObjectKey = ManualWalletProofStorage.TryNormalizeTenantScopedObjectKey(
            session.ProofImageUrl,
            tenantId,
            _r2Options,
            out var existingObjectKey)
            ? existingObjectKey
            : null;

        var updatedSession = await _checkoutSessionService.SetManualWalletProofAsync(
            session.Id,
            tenantId,
            string.Empty,
            cancellationToken);

        if (updatedSession == null)
        {
            return NotFound(new ApiErrorResponse(
                "CHECKOUT_SESSION_NOT_FOUND",
                "Checkout session not found or no longer active.",
                traceId));
        }

        if (!string.IsNullOrWhiteSpace(previousProofObjectKey))
        {
            try
            {
                await _storageService.DeleteObjectAsync(previousProofObjectKey, cancellationToken);
                _logger.LogInformation(
                    "Manual wallet proof cleared and deleted | TenantId: {TenantId} | CheckoutSessionId: {CheckoutSessionId} | ObjectKey: {ObjectKey} | TraceId: {TraceId}",
                    tenantId,
                    session.Id,
                    previousProofObjectKey,
                    traceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete manual wallet proof during clear | TenantId: {TenantId} | CheckoutSessionId: {CheckoutSessionId} | ObjectKey: {ObjectKey} | TraceId: {TraceId}",
                    tenantId,
                    session.Id,
                    previousProofObjectKey,
                    traceId);
                return Ok(new ManualWalletClearProofResponse
                {
                    Cleared = true,
                    WarningCode = "PROOF_STORAGE_DELETE_FAILED",
                    WarningMessage = "The proof was removed from checkout, but storage cleanup will be retried by the system."
                });
            }
        }

        return Ok(new ManualWalletClearProofResponse
        {
            Cleared = true
        });
    }

    public sealed class UploadManualWalletProofRequest
    {
        public Guid CheckoutSessionId { get; set; }

        public IFormFile? File { get; set; }
    }

    public sealed class ManualWalletConfigResponse
    {
        public string WalletNumber { get; set; } = string.Empty;
        public IReadOnlyCollection<ManualWalletTypeResponse> WalletTypes { get; set; } = Array.Empty<ManualWalletTypeResponse>();
        public IReadOnlyCollection<string> AllowedMimeTypes { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> AllowedExtensions { get; set; } = Array.Empty<string>();
        public long MaxFileBytes { get; set; }
    }

    public sealed class ManualWalletTypeResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string? Icon { get; set; }
    }

    public sealed class ManualWalletUploadProofResponse
    {
        public string FileKey { get; set; } = string.Empty;
        public string? CanonicalContentType { get; set; }
    }

    public sealed class ManualWalletClearProofResponse
    {
        public bool Cleared { get; set; }
        public string? WarningCode { get; set; }
        public string? WarningMessage { get; set; }
    }

    private static IActionResult ManualPaymentAdmissionRejected(
        ManualPaymentApplicationException exception,
        string traceId)
    {
        var statusCode = exception.FailureType == ManualPaymentApplicationFailureType.Configuration
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status409Conflict;

        return new ObjectResult(new ApiErrorResponse(exception.Code, exception.Message, traceId))
        {
            StatusCode = statusCode
        };
    }

    private IReadOnlyCollection<AllowedFileType> ResolveConfiguredProofFileTypes()
    {
        var configuredTypes = FileValidationService.GetAllowedFileTypesForMimeTypes(
            _manualPaymentOptions.Proof.AcceptedContentTypes);

        return configuredTypes.Count > 0
            ? configuredTypes
            : FileValidationProfiles.ManualWallet;
    }

    private static ManualWalletTypeResponse? MapStorefrontWalletType(
        ManualPaymentChannelConfiguration channel) => channel.Channel switch
        {
            ManualPaymentChannel.VodafoneCash => new ManualWalletTypeResponse
            {
                Code = "VODAFONECASH",
                Name = channel.DisplayName,
                NameAr = channel.DisplayName,
                Icon = "VF"
            },
            ManualPaymentChannel.Instapay => new ManualWalletTypeResponse
            {
                Code = "INSTAPAY",
                Name = channel.DisplayName,
                NameAr = channel.DisplayName,
                Icon = "IP"
            },
            _ => null
        };
}
