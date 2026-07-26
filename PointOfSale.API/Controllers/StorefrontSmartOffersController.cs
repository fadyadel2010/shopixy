using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.SmartOffers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/storefront/smart-offers")]
[AllowAnonymous]
public class StorefrontSmartOffersController : ControllerBase
{
    private const string StorefrontAuthenticationScheme = "StorefrontJwt";
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;

    private readonly ApplicationDbContext _dbContext;
    private readonly ISmartOfferRedeemTokenService _smartOfferRedeemTokenService;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<StorefrontSmartOffersController> _logger;

    public StorefrontSmartOffersController(
        ApplicationDbContext dbContext,
        ISmartOfferRedeemTokenService smartOfferRedeemTokenService,
        CommerceReliabilityTelemetry telemetry,
        ILogger<StorefrontSmartOffersController> logger)
    {
        _dbContext = dbContext;
        _smartOfferRedeemTokenService = smartOfferRedeemTokenService;
        _telemetry = telemetry;
        _logger = logger;
    }

    [HttpGet]
    [EnableRateLimiting("PublicReadRateLimit")]
    [ProducesResponseType(typeof(List<StorefrontSmartOfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetSmartOffers(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }

        try
        {
            var offers = await _dbContext.SmartOffers
                .AsNoTracking()
                .Where(x => 
                    x.TenantId == tenantValidation.TenantId!.Value && 
                    x.Status == SmartOfferStatus.Active &&
                    x.GeneratedCouponId != null &&
                    _dbContext.Coupons
                        .IgnoreQueryFilters()
                        .Any(c => 
                            c.Id == x.GeneratedCouponId && 
                            c.TenantId == tenantValidation.TenantId.Value &&
                            !c.IsDeleted && 
                            c.IsActive))
                .OrderByDescending(x => x.Priority)
                .Select(x => new StorefrontSmartOfferDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Priority = x.Priority,
                    TriggerType = x.TriggerType,
                    PagesScope = x.PagesScope,
                    DelaySeconds = x.DelaySeconds,
                    ScrollPercent = x.ScrollPercent,
                    ShowOncePerSession = x.ShowOncePerSession,
                    MaxDisplaysPerUser = x.MaxDisplaysPerUser,
                    PopupTitle = x.PopupTitle,
                    PopupDescription = x.PopupDescription,
                    ButtonText = x.ButtonText,
                    ImageUrl = x.ImageUrl,
                    BackgroundColor = x.BackgroundColor
                })
                .ToListAsync(cancellationToken);

            return Ok(offers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load storefront smart offers");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpPost("{id:guid}/redeem")]
    [EnableRateLimiting("CouponRateLimit")]
    [ProducesResponseType(typeof(StorefrontSmartOfferRedeemResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RedeemSmartOffer([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }

        try
        {
            var authenticatedCustomer = await ResolveAuthenticatedCustomerAsync();
            if (!authenticatedCustomer.CustomerId.HasValue)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new ApiErrorResponse(
                        CouponErrorCodes.AuthenticationRequired,
                        "You must sign in to redeem smart offers.",
                        traceId));
            }

            var tenantClaimError = ValidateAuthenticatedTenantClaim(
                authenticatedCustomer.Principal,
                tenantValidation.TenantId!.Value,
                traceId);
            if (tenantClaimError != null)
            {
                return tenantClaimError;
            }

            var offer = await _dbContext.SmartOffers
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == tenantValidation.TenantId.Value &&
                    x.Id == id &&
                    x.Status == SmartOfferStatus.Active &&
                    x.GeneratedCouponId.HasValue)
                .Select(x => new
                {
                    x.Id,
                    CouponId = x.GeneratedCouponId!.Value
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (offer == null)
            {
                return NotFound(new ApiErrorResponse(
                    CouponErrorCodes.Invalid,
                    "Smart offer is not available.",
                    traceId));
            }

            var couponIsActive = await _dbContext.Coupons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x =>
                    x.TenantId == tenantValidation.TenantId.Value &&
                    x.Id == offer.CouponId &&
                    !x.IsDeleted &&
                    x.IsActive,
                    cancellationToken);

            if (!couponIsActive)
            {
                return NotFound(new ApiErrorResponse(
                    CouponErrorCodes.Invalid,
                    "Smart offer is not available.",
                    traceId));
            }

            var redeemToken = _smartOfferRedeemTokenService.CreateToken(
                tenantValidation.TenantId.Value,
                offer.Id,
                offer.CouponId,
                authenticatedCustomer.CustomerId.Value);

            return Ok(new StorefrontSmartOfferRedeemResponseDto
            {
                RedeemToken = redeemToken,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to redeem storefront smart offer | SmartOfferId: {SmartOfferId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private async Task<(ClaimsPrincipal? Principal, Guid? CustomerId)> ResolveAuthenticatedCustomerAsync()
    {
        var customerAuthenticationResult = await HttpContext.AuthenticateAsync(CustomerAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(customerAuthenticationResult.Principal, out var customerId))
        {
            return (customerAuthenticationResult.Principal, customerId);
        }

        var storefrontAuthenticationResult = await HttpContext.AuthenticateAsync(StorefrontAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(storefrontAuthenticationResult.Principal, out customerId))
        {
            return (storefrontAuthenticationResult.Principal, customerId);
        }

        return (null, null);
    }

    private IActionResult? ValidateAuthenticatedTenantClaim(
        ClaimsPrincipal? principal,
        Guid tenantId,
        string traceId)
    {
        var tenantClaimValue = principal?.FindFirst("TenantId")?.Value
            ?? principal?.FindFirst("tenant_id")?.Value
            ?? principal?.FindFirst("tenantId")?.Value;

        if (string.IsNullOrWhiteSpace(tenantClaimValue) ||
            !Guid.TryParse(tenantClaimValue, out var claimTenantId))
        {
            _telemetry.RecordTenantAccessViolation(
                "smart_offer_redeem_tenant_claim_mismatch",
                HttpContext.Request.Path.Value ?? string.Empty);

            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }

        if (claimTenantId == tenantId)
        {
            return null;
        }

        _telemetry.RecordTenantAccessViolation(
            "smart_offer_redeem_tenant_claim_mismatch",
            HttpContext.Request.Path.Value ?? string.Empty);

        return Unauthorized(new ApiErrorResponse(
            TenantErrorCodes.TenantMismatch,
            "Authenticated customer does not belong to the resolved tenant.",
            traceId));
    }

    private static bool TryGetAuthenticatedCustomerId(ClaimsPrincipal? principal, out Guid customerId)
    {
        customerId = Guid.Empty;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return Guid.TryParse(
            principal.FindFirst("CustomerId")?.Value
                ?? principal.FindFirst("customer_id")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out customerId) &&
            customerId != Guid.Empty;
    }
}
