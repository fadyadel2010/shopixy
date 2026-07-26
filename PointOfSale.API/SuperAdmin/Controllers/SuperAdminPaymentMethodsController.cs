using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.API.Services.CommerceHub.Finance;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/payment-methods")]
public sealed class SuperAdminPaymentMethodsController : SuperAdminBaseController
{
    private readonly ICommerceHubVendorPaymentsService _vendorPaymentsService;
    private readonly ISuperAdminAuditService _auditService;

    public SuperAdminPaymentMethodsController(
        ICommerceHubVendorPaymentsService vendorPaymentsService,
        ISuperAdminAuditService auditService)
    {
        _vendorPaymentsService = vendorPaymentsService;
        _auditService = auditService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SuperAdminVendorPaymentMethodResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken cancellationToken = default)
    {
        var methods = await _vendorPaymentsService.GetPaymentMethodsAsync(cancellationToken);

        await _auditService.LogActionAsync(
            action: "PAYMENT_METHODS_VIEWED",
            entityType: "CommerceHubVendorPaymentMethod",
            entityId: "payment-methods",
            description: "Super Admin viewed vendor payment methods.",
            success: true,
            details: new { Count = methods.Count },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(new SuperAdminVendorPaymentMethodResponseDto
        {
            Methods = methods
        });
    }

    [HttpPost]
    [ProducesResponseType(typeof(SuperAdminVendorPaymentMethodDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePaymentMethod(
        [FromBody] SuperAdminCreateVendorPaymentMethodRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var method = await _vendorPaymentsService.CreatePaymentMethodAsync(
                request,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "PAYMENT_METHOD_CREATED",
                entityType: "CommerceHubVendorPaymentMethod",
                entityId: method.Id.ToString(),
                description: "Super Admin created a vendor payment method.",
                success: true,
                details: method,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(method);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
