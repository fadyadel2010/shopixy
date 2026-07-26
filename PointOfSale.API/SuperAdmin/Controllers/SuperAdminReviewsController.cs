using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.DTOs.MarketplaceReviews;
using PointOfSale.Core.Exceptions;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/reviews")]
public sealed class SuperAdminReviewsController : SuperAdminBaseController
{
    private readonly ISuperAdminProductReviewService _reviewService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminReviewsController> _logger;

    public SuperAdminReviewsController(
        ISuperAdminProductReviewService reviewService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminReviewsController> logger)
    {
        _reviewService = reviewService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReviews(
        [FromQuery] SuperAdminProductReviewQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var result = await _reviewService.GetReviewsAsync(query, cancellationToken);
        var meta = new Dictionary<string, object?>
        {
            ["page"] = result.Page,
            ["pageSize"] = result.PageSize,
            ["totalCount"] = result.TotalCount,
            ["totalPages"] = result.TotalPages,
            ["status"] = query.Status?.ToString(),
            ["rating"] = query.Rating,
            ["productId"] = query.ProductId,
            ["userId"] = query.UserId
        };

        await _auditService.LogActionAsync(
            action: "REVIEWS_LIST_VIEWED",
            entityType: "ProductReview",
            entityId: "product-reviews",
            description: "Super Admin viewed the marketplace product reviews queue.",
            success: true,
            details: meta,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(result, meta);
    }

    [HttpGet("{reviewId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewDetails(
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        var result = await _reviewService.GetReviewDetailsAsync(reviewId, cancellationToken);
        if (result == null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "REVIEW_NOT_FOUND", "Review was not found.");
        }

        return OkResponse(result);
    }

    [HttpPost("{reviewId:guid}/approve")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveReview(
        Guid reviewId,
        [FromBody] SuperAdminReviewModerationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _reviewService.ApproveAsync(reviewId, HttpContext.TraceIdentifier, cancellationToken);

            await _auditService.LogActionAsync(
                action: "REVIEW_APPROVED",
                entityType: "ProductReview",
                entityId: reviewId.ToString("N"),
                description: "Super Admin approved a marketplace product review.",
                success: true,
                details: new { request?.Reason },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation("SuperAdmin approved review | ReviewId: {ReviewId}", reviewId);
            return OkResponse(result);
        }
        catch (MarketplaceProductReviewException ex)
        {
            return ErrorResponse(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    [HttpPost("{reviewId:guid}/reject")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectReview(
        Guid reviewId,
        [FromBody] SuperAdminReviewModerationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _reviewService.RejectAsync(reviewId, HttpContext.TraceIdentifier, cancellationToken);

            await _auditService.LogActionAsync(
                action: "REVIEW_REJECTED",
                entityType: "ProductReview",
                entityId: reviewId.ToString("N"),
                description: "Super Admin rejected a marketplace product review.",
                success: true,
                details: new { request?.Reason },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation("SuperAdmin rejected review | ReviewId: {ReviewId}", reviewId);
            return OkResponse(result);
        }
        catch (MarketplaceProductReviewException ex)
        {
            return ErrorResponse(ex.StatusCode, ex.Code, ex.Message);
        }
    }
}
