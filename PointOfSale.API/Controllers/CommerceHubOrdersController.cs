using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PointOfSale.API.Attributes;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

[ApiController]
public class CommerceHubOrdersController : ControllerBase
{
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubOrderReadRepository _orderReadRepository;
    private readonly ICommerceHubOrderProjectionRepairService _orderProjectionRepairService;
    private readonly ICommerceHubOrderManagementService _orderManagementService;
    private readonly ICommerceHubEarningsService _earningsService;
    private readonly ICommerceHubOrderAccessService _orderAccessService;
    private readonly IAdminOrderService _adminOrderService;
    private readonly ICommerceHubPaymentRecoveryAuthorityService _paymentRecoveryAuthorityService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CommerceHubOrdersController> _logger;
    private readonly CheckoutSessionSecurityOptions _checkoutSessionSecurityOptions;
    private readonly IWebHostEnvironment _environment;

    public CommerceHubOrdersController(
        ApplicationDbContext dbContext,
        ICommerceHubOrderReadRepository orderReadRepository,
        ICommerceHubOrderProjectionRepairService orderProjectionRepairService,
        ICommerceHubOrderManagementService orderManagementService,
        ICommerceHubEarningsService earningsService,
        ICommerceHubOrderAccessService orderAccessService,
        IAdminOrderService adminOrderService,
        ICommerceHubPaymentRecoveryAuthorityService paymentRecoveryAuthorityService,
        ITenantContext tenantContext,
        ILogger<CommerceHubOrdersController> logger,
        IOptions<CheckoutSessionSecurityOptions> checkoutSessionSecurityOptions,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _orderReadRepository = orderReadRepository;
        _orderProjectionRepairService = orderProjectionRepairService;
        _orderManagementService = orderManagementService;
        _earningsService = earningsService;
        _orderAccessService = orderAccessService;
        _adminOrderService = adminOrderService;
        _paymentRecoveryAuthorityService = paymentRecoveryAuthorityService;
        _tenantContext = tenantContext;
        _logger = logger;
        _checkoutSessionSecurityOptions = checkoutSessionSecurityOptions.Value;
        _environment = environment;
    }

    [Authorize(Roles = "Owner,Admin,Manager")]
    [HttpGet("api/commercehub/vendor-orders")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminPagedResult<AdminOrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListVendorOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] PaymentStatus? paymentStatus = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = AdminOrderListQuery.DefaultSortBy,
        [FromQuery] string sortDirection = AdminOrderListQuery.DefaultSortDirection,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var storeId = _tenantContext.TenantId;
        if (!storeId.HasValue)
        {
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", traceId));
        }

        var query = new AdminOrderListQuery
        {
            Page = page,
            PageSize = pageSize,
            Status = status,
            PaymentStatus = paymentStatus,
            Source = OrderSource.Marketplace,
            FromDate = fromDate,
            ToDate = toDate,
            Search = search,
            SortBy = sortBy,
            SortDirection = sortDirection
        };
        var queryError = query.Validate();
        if (queryError != null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_QUERY", queryError, traceId));
        }

        return Ok(await _adminOrderService.ListOrdersAsync(
            storeId.Value,
            query,
            traceId,
            cancellationToken));
    }

    [Authorize(Roles = "Owner,Admin,Manager")]
    [HttpGet("api/commercehub/vendor-orders/{subOrderId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminOrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVendorOrder(
        Guid subOrderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var storeId = _tenantContext.TenantId;
        if (!storeId.HasValue)
        {
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", traceId));
        }

        var order = await _adminOrderService.GetCommerceHubVendorOrderDetailsAsync(
            storeId.Value,
            subOrderId,
            traceId,
            cancellationToken);

        return order == null
            ? NotFound(new ApiErrorResponse("ORDER_NOT_FOUND", "CommerceHub vendor order not found.", traceId))
            : Ok(order);
    }

    [AllowAnonymous]
    [AllowAnonymousTenant]
    [HttpGet("api/commercehub/orders/{marketplaceOrderId:guid}")]
    [ProducesResponseType(typeof(CommerceHubOrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetOrder(
        Guid marketplaceOrderId,
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var authenticatedCustomerId = await ResolveAuthenticatedCustomerIdAsync(cancellationToken);
        var orderAccessToken = token?.Trim();
        if (string.IsNullOrWhiteSpace(orderAccessToken))
        {
            orderAccessToken = CommerceHubOrderAccessCookie.ReadOrderAccessToken(Request, marketplaceOrderId.ToString("D"));
        }

        // SECURITY FIX: Validate credentials BEFORE expensive operations
        // Reject completely anonymous requests immediately
        var hasToken = !string.IsNullOrWhiteSpace(orderAccessToken);

        if (!authenticatedCustomerId.HasValue && !hasToken)
        {
            _logger.LogWarning(
                "[SECURITY] Anonymous order access attempt without credentials | OrderId: {OrderId} | TraceId: {TraceId}",
                marketplaceOrderId,
                traceId);

            return Unauthorized(new ApiErrorResponse(
                "ORDER_ACCESS_DENIED",
                "You do not have permission to view this order. Please use a valid access token or sign in.",
                traceId));
        }

        var authority = await LoadOrderAuthoritySnapshotAsync(marketplaceOrderId, cancellationToken);
        if (authority == null)
        {
            return NotFound(new ApiErrorResponse("ORDER_NOT_FOUND", "Marketplace order not found.", traceId));
        }

        var accessGranted = false;

        // Option A: Authenticated customer ownership check from SQL authority
        if (authenticatedCustomerId.HasValue && authenticatedCustomerId.Value == authority.CustomerId)
        {
            accessGranted = true;
        }

        // Option B: Signed access token validation against SQL order metadata
        if (!accessGranted &&
            hasToken &&
            _orderAccessService.ValidateAccessToken(marketplaceOrderId, authority.OrderNumber, orderAccessToken))
        {
            accessGranted = true;
        }

        if (!accessGranted)
        {
            _logger.LogWarning(
                "[SECURITY] Unauthorized order access attempt | OrderId: {OrderId} | AuthenticatedCustomerId: {CustomerId} | HasToken: {HasToken} | TraceId: {TraceId}",
                marketplaceOrderId,
                authenticatedCustomerId,
                hasToken,
                traceId);

            return Unauthorized(new ApiErrorResponse(
                "ORDER_ACCESS_DENIED",
                "You do not have permission to view this order. Please use a valid access token or sign in.",
                traceId));
        }

        if (hasToken)
        {
            RefreshOrderAccessCookie(marketplaceOrderId, authority.OrderNumber, orderAccessToken!);
        }

        try
        {
            await _orderProjectionRepairService.EnsureOrderProjectedAsync(marketplaceOrderId, cancellationToken);
        }
        catch (CommerceHubProjectionRepairException ex)
        {
            _logger.LogError(
                ex,
                "[CommerceHubOrders] Projection repair failed for customer-critical order detail | OrderId: {OrderId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                marketplaceOrderId,
                authority.CustomerId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var document = await _orderReadRepository.GetByMarketplaceOrderIdAsync(marketplaceOrderId, cancellationToken);
        if (document == null)
        {
            _logger.LogError(
                "[CommerceHubOrders] SQL order authority exists but projection remained unavailable after repair | OrderId: {OrderId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                marketplaceOrderId,
                authority.CustomerId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        if (!IsEgpOrderProjection(document))
        {
            _logger.LogError(
                "[CommerceHubOrders] Currency-invalid order projection withheld | OrderId: {OrderId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                marketplaceOrderId,
                authority.CustomerId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var paymentRecoveryAuthority = await _paymentRecoveryAuthorityService.EvaluateAsync(
            marketplaceOrderId,
            authority.PaymentMethodCode,
            authority.PaymentStatus,
            authority.Status,
            cancellationToken);

        return Ok(MapToDto(document, paymentRecoveryAuthority));
    }

    [Authorize(Roles = "Owner,Admin,Manager")]
    [HttpGet("api/commercehub/suborders/{subOrderId:guid}/actions")]
    [ProducesResponseType(typeof(CommerceHubVendorOrderActionAuthorityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVendorOrderActions(
        Guid subOrderId,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var storeId = _tenantContext.TenantId;
        if (!storeId.HasValue)
        {
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", traceId));
        }

        try
        {
            return Ok(await _orderManagementService.GetVendorOrderActionAuthorityAsync(
                storeId.Value,
                subOrderId,
                cancellationToken));
        }
        catch (CommerceHubCheckoutException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
    }

    [Authorize(Roles = "Owner,Admin,Manager")]
    [HttpPatch("api/commercehub/suborders/{subOrderId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSubOrderStatus(
        Guid subOrderId,
        [FromBody] CommerceHubUpdateSubOrderStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var storeId = _tenantContext.TenantId;

        if (!storeId.HasValue)
        {
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", traceId));
        }

        try
        {
            var subOrder = await _orderManagementService.UpdateSubOrderStatusAsync(
                storeId.Value,
                subOrderId,
                request.Status,
                request.ExpectedVersion,
                request.Reason,
                traceId,
                cancellationToken);

            return Ok(new
            {
                subOrderId = subOrder.Id,
                status = subOrder.Status,
                paymentStatus = subOrder.PaymentStatus,
                lifecycleVersion = subOrder.ProjectionMutationVersion
            });
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning("CommerceHub sub-order update blocked | Code: {Code} | TraceId: {TraceId}", ex.Code, traceId);
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
    }

    [Authorize(Roles = "Owner,Admin")]
    [HttpGet("api/commercehub/earnings")]
    [ProducesResponseType(typeof(CommerceHubVendorEarningsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetVendorEarnings(CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var storeId = _tenantContext.TenantId;

        if (!storeId.HasValue)
        {
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", traceId));
        }

        var response = await _earningsService.GetVendorEarningsAsync(storeId.Value, cancellationToken);
        return Ok(response);
    }

    private async Task<Guid?> ResolveAuthenticatedCustomerIdAsync(CancellationToken cancellationToken)
    {
        var customerAuthenticationResult = await HttpContext.AuthenticateAsync(CustomerAuthenticationScheme);
        if (customerAuthenticationResult.Succeeded && customerAuthenticationResult.Principal != null)
        {
            var customerIdClaim = customerAuthenticationResult.Principal.FindFirst("CustomerId")?.Value;
            if (Guid.TryParse(customerIdClaim, out var customerId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return customerId;
            }
        }

        return null;
    }

    private async Task<OrderAuthoritySnapshot?> LoadOrderAuthoritySnapshotAsync(
        Guid marketplaceOrderId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Where(order =>
                order.Id == marketplaceOrderId &&
                !order.IsDeleted &&
                order.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode &&
                !order.SubOrders.Any(subOrder => subOrder.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode))
            .Select(order => new OrderAuthoritySnapshot(
                order.CustomerId,
                order.OrderNumber,
                order.Status,
                order.PaymentStatus,
                order.PaymentMethodCode))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record OrderAuthoritySnapshot(
        Guid CustomerId,
        string OrderNumber,
        PointOfSale.Core.Enums.OrderStatus Status,
        PointOfSale.Core.Enums.PaymentStatus PaymentStatus,
        string PaymentMethodCode);

    private static CommerceHubOrderDetailsDto MapToDto(
        CommerceHubOrderDocument document,
        CommerceHubPaymentRecoveryAuthorityDecision paymentRecoveryAuthority)
    {
        return new CommerceHubOrderDetailsDto
        {
            MarketplaceOrderId = document.MarketplaceOrderId,
            OrderNumber = document.OrderNumber,
            FullName = document.FullName,
            PhoneNumber = MaskPhoneNumber(document.PhoneNumber),
            AddressLine = document.AddressLine,
            City = document.City,
            Area = document.Area,
            Notes = document.Notes,
            Currency = document.Currency,
            PaymentMethodCode = document.PaymentMethodCode,
            PaymentMethodName = document.PaymentMethodName,
            PaymentReference = document.PaymentReference,
            PaymentReferenceId = document.PaymentReferenceId,
            PaymentGateway = document.PaymentGateway,
            Subtotal = document.Subtotal,
            Shipping = document.ShippingTotal,
            Total = document.TotalAmount,
            Status = document.Status,
            PaymentStatus = document.PaymentStatus,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            PaidAt = document.PaidAt,
            CanRetryPayment = paymentRecoveryAuthority.CanRetryPayment,
            PaymentRecoveryMessage = paymentRecoveryAuthority.PaymentRecoveryMessage,
            SubOrders = document.SubOrders.Select(subOrder => new CommerceHubOrderSubOrderDto
            {
                SubOrderId = subOrder.SubOrderId,
                OrderNumber = subOrder.OrderNumber,
                StoreId = subOrder.StoreId,
                StoreName = subOrder.StoreName,
                Currency = subOrder.Currency,
                Subtotal = subOrder.Subtotal,
                Shipping = subOrder.Shipping,
                ShippingMethodId = subOrder.ShippingMethodId,
                ShippingMethodName = subOrder.ShippingMethodName,
                ShippingSnapshotJson = subOrder.ShippingSnapshotJson,
                PaymentMethod = subOrder.PaymentMethod,
                PaymentMethodCode = subOrder.PaymentMethodCode,
                PaymentMethodName = subOrder.PaymentMethodName,
                PaymentSnapshotJson = subOrder.PaymentSnapshotJson,
                Total = subOrder.Total,
                Status = subOrder.Status,
                PaymentStatus = subOrder.PaymentStatus,
                PaymentReferenceId = subOrder.PaymentReferenceId,
                PaidAt = subOrder.PaidAt,
                Items = subOrder.Items.Select(item => new CommerceHubOrderItemDto
                {
                    ListingId = item.ListingId,
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    ProductName = item.ProductName,
                    VariantName = item.VariantName,
                    VariantAttributesJson = item.VariantAttributesJson,
                    ImageUrl = item.ImageUrl,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Total = item.Total
                }).ToList()
            }).ToList()
        };
    }

    private static bool IsEgpOrderProjection(CommerceHubOrderDocument document) =>
        string.Equals(document.Currency, FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.Ordinal) &&
        document.SubOrders.All(subOrder =>
            string.Equals(subOrder.Currency, FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.Ordinal));

    private void RefreshOrderAccessCookie(Guid marketplaceOrderId, string orderNumber, string orderAccessToken)
    {
        var allowInsecureDevelopmentCookie = _environment.IsDevelopment();

        CommerceHubOrderAccessCookie.StoreOrderAccessToken(
            HttpContext,
            marketplaceOrderId,
            orderNumber,
            orderAccessToken,
            TimeSpan.FromMinutes(_checkoutSessionSecurityOptions.RecoveryTokenTtlMinutes),
            allowInsecureDevelopmentCookie);
    }

    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Length <= 4)
        {
            return "****";
        }

        return new string('*', phoneNumber.Length - 4) + phoneNumber[^4..];
    }
}
