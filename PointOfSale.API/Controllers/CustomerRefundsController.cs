using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.API.Controllers;

[AllowAnonymousTenant]
[ApiController]
[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
[Route("api/customers/refunds")]
public class CustomerRefundsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const long MaxRefundEvidenceUploadBytes = 50 * 1024 * 1024;
    private readonly ICustomerRefundService _refundService;
    private readonly ILogger<CustomerRefundsController> _logger;

    public CustomerRefundsController(
        ICustomerRefundService refundService,
        ILogger<CustomerRefundsController> logger)
    {
        _refundService = refundService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerRefundSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRefunds(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var refunds = await _refundService.GetRefundRequestsAsync(customerId.Value, cancellationToken);
        return Ok(refunds);
    }

    [HttpPost]
    [EnableRateLimiting("CustomerRefundRateLimit")]
    [ProducesResponseType(typeof(CustomerRefundSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRefund(
        [FromBody] CustomerCreateRefundRequestDto request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required.", traceId));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new ApiErrorResponse("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.", traceId));
        }

        try
        {
            var refundRequest = await _refundService.CreateRefundRequestAsync(
                customerId.Value,
                request,
                traceId,
                idempotencyKey,
                cancellationToken);

            return Ok(refundRequest);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "Customer refund request blocked | CustomerId: {CustomerId} | Code: {Code} | TraceId: {TraceId}",
                customerId.Value,
                ex.Code,
                traceId);

            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Customer refund request failed unexpectedly | CustomerId: {CustomerId} | TraceId: {TraceId}",
                customerId.Value,
                traceId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("INTERNAL_ERROR", "Failed to create refund request.", traceId));
        }
    }

    [HttpPost("upload-url")]
    [EnableRateLimiting("CustomerRefundUploadRateLimit")]
    [ProducesResponseType(typeof(RefundEvidenceUploadUrlResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateUploadUrl(
        [FromBody] RefundEvidenceUploadUrlRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required.", traceId));
        }

        try
        {
            var uploadLease = await _refundService.CreateUploadLeaseAsync(
                customerId.Value,
                request,
                cancellationToken);

            var fileUrl = Url.ActionLink(
                nameof(GetUploadedMedia),
                values: new { mediaId = uploadLease.MediaId });

            return Ok(new RefundEvidenceUploadUrlResponseDto
            {
                UploadUrl = uploadLease.UploadUrl,
                FileUrl = fileUrl ?? $"/api/customers/refunds/media/{uploadLease.MediaId}",
                UploadMethod = uploadLease.UploadMethod,
                ContentType = uploadLease.ContentType,
                UploadHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = uploadLease.ContentType
                }
            });
        }
        catch (CommerceHubCheckoutException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    [HttpPost("upload")]
    [EnableRateLimiting("CustomerRefundUploadRateLimit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRefundEvidenceUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRefundEvidenceUploadBytes)]
    [ProducesResponseType(typeof(RefundEvidenceUploadResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadEvidence(
        [FromForm] RefundEvidenceUploadFormRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        if (request?.File == null || request.File.Length <= 0)
        {
            return BadRequest(new ApiErrorResponse("REFUND_MEDIA_FILE_REQUIRED", "Refund evidence file is required.", traceId));
        }

        try
        {
            var uploadedMedia = await _refundService.UploadEvidenceAsync(
                customerId.Value,
                request.File,
                request.Sha256,
                cancellationToken);

            var fileUrl = Url.ActionLink(
                nameof(GetUploadedMedia),
                values: new { mediaId = uploadedMedia.MediaId });

            return Ok(new RefundEvidenceUploadResponseDto
            {
                FileUrl = fileUrl ?? $"/api/customers/refunds/media/{uploadedMedia.MediaId}"
            });
        }
        catch (CommerceHubCheckoutException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    [HttpGet("media/{mediaId:guid}")]
    public async Task<IActionResult> GetUploadedMedia(
        [FromRoute] Guid mediaId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        try
        {
            var accessUrl = await _refundService.GetCustomerMediaAccessUrlAsync(
                customerId.Value,
                mediaId,
                cancellationToken);

            return Redirect(accessUrl);
        }
        catch (CommerceHubCheckoutException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    private Guid? ResolveCustomerId()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
