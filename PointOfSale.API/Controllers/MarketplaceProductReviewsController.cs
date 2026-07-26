using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.MarketplaceReviews;
using PointOfSale.API.Services.Platform;
using PointOfSale.Core.DTOs.MarketplaceReviews;
using PointOfSale.Core.Exceptions;

namespace PointOfSale.API.Controllers;

[AllowAnonymousTenant]
[ApiController]
[Route("api/marketplace")]
public sealed class MarketplaceProductReviewsController : ControllerBase
{
    private const int ReviewCreateLimit = 5;
    private const int ReviewCreateWindowSeconds = 3600;

    private readonly IMarketplaceProductReviewService _reviewService;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<MarketplaceProductReviewsController> _logger;

    public MarketplaceProductReviewsController(
        IMarketplaceProductReviewService reviewService,
        IRateLimitService rateLimitService,
        ILogger<MarketplaceProductReviewsController> logger)
    {
        _reviewService = reviewService;
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    [HttpPost("reviews")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(typeof(MarketplaceProductReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateReview(
        [FromBody] MarketplaceCreateProductReviewRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var rateLimitResponse = await EnforceCreateRateLimitAsync(customerId.Value, traceId);
        if (rateLimitResponse != null)
        {
            return rateLimitResponse;
        }

        try
        {
            var review = await _reviewService.CreateAsync(
                customerId.Value,
                request,
                traceId,
                cancellationToken);

            return StatusCode(StatusCodes.Status201Created, review);
        }
        catch (MarketplaceProductReviewException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    [HttpPut("reviews/{reviewId:guid}")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(typeof(MarketplaceProductReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateReview(
        Guid reviewId,
        [FromBody] MarketplaceUpdateProductReviewRequest request,
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
            var review = await _reviewService.UpdateAsync(
                customerId.Value,
                reviewId,
                request,
                traceId,
                cancellationToken);

            return Ok(review);
        }
        catch (MarketplaceProductReviewException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    [HttpDelete("reviews/{reviewId:guid}")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteReview(Guid reviewId, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        try
        {
            await _reviewService.DeleteAsync(customerId.Value, reviewId, traceId, cancellationToken);
            return NoContent();
        }
        catch (MarketplaceProductReviewException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    [HttpGet("products/{id:guid}/reviews")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MarketplaceProductReviewPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProductReviews(
        Guid id,
        [FromQuery] MarketplaceProductReviewsQuery query,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validationError = query.Validate();
        if (validationError != null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_QUERY", validationError, traceId));
        }

        var reviews = await _reviewService.GetApprovedReviewsAsync(id, query, cancellationToken);
        return Ok(reviews);
    }

    [HttpGet("products/{id:guid}/rating-summary")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MarketplaceProductRatingSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProductRatingSummary(Guid id, CancellationToken cancellationToken)
    {
        var summary = await _reviewService.GetRatingSummaryAsync(id, cancellationToken);
        return Ok(summary);
    }

    [HttpGet("products/{id:guid}/review-context")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MarketplaceCustomerProductReviewContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewContext(Guid id, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        try
        {
            var context = await _reviewService.GetCustomerReviewContextAsync(id, await ResolveOptionalCustomerIdAsync(), cancellationToken);
            return Ok(context);
        }
        catch (MarketplaceProductReviewException ex)
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

    private async Task<Guid?> ResolveOptionalCustomerIdAsync()
    {
        var customerId = ResolveCustomerId();
        if (customerId.HasValue)
        {
            return customerId;
        }

        var authenticateResult = await HttpContext.AuthenticateAsync(CustomerSecurityConstants.AuthenticationScheme);
        var claimValue = authenticateResult.Principal?.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(claimValue, out var parsedCustomerId) ? parsedCustomerId : null;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private async Task<IActionResult?> EnforceCreateRateLimitAsync(Guid customerId, string traceId)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var endpointKey = $"marketplace-review-create:{customerId:N}";
        var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(
            clientIp,
            endpointKey,
            ReviewCreateLimit,
            ReviewCreateWindowSeconds);

        if (rateLimitResult.Allowed)
        {
            return null;
        }

        await _rateLimitService.RecordRateLimitHitAsync(clientIp, endpointKey);

        _logger.LogWarning(
            "Marketplace review create rate limited | CustomerId: {CustomerId} | IP: {IP} | TraceId: {TraceId}",
            customerId,
            clientIp,
            traceId);

        return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(
            "RATE_LIMITED",
            "Too many review submissions. Please try again later.",
            traceId)
        {
            Data = new
            {
                retryAfterSeconds = ReviewCreateWindowSeconds
            }
        });
    }
}
