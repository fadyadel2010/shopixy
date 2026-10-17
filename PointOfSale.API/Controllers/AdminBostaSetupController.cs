using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/shipping/carrier-accounts/{carrierAccountId:guid}/bosta")]
[Authorize(Roles = "Owner,Admin,Manager")]
[Authorize(Policy = PosPermissions.ShippingManage)]
public sealed class AdminBostaSetupController(ApplicationDbContext db, ITenantContext tenantContext,
    ICarrierIntegrationAuthority authority, IShippingCarrierRegistry registry, BostaPickupVerificationCache? pickupVerification = null) : ControllerBase
{
    [HttpGet("pickup-locations")]
    public async Task<IActionResult> PickupLocations(Guid carrierAccountId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var tenant = tenantContext.RequiredTenantId;
        var account = await db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            account => account.Id == carrierAccountId && account.TenantId == tenant && !account.IsDeleted && account.CarrierCode == CarrierCodes.Bosta, ct);
        if (account == null) return NotFound(new { code = "UNKNOWN_CARRIER_ACCOUNT" });
        var resolution = await authority.ResolveConnectionCredentialsAsync(tenant, CarrierCodes.Bosta, ct);
        if (!resolution.Succeeded || resolution.Credentials == null ||
            resolution.CarrierCode != CarrierCodes.Bosta || resolution.Configuration?.CarrierCode != CarrierCodes.Bosta ||
            account.Environment.ToString() != (resolution.Credentials.UseSandbox ? "Sandbox" : "Production"))
            return Conflict(new { code = "BOSTA_PICKUP_SAVED_CONFIGURATION_REQUIRED" });
        if (registry.GetAdapter(CarrierCodes.Bosta) is not IBostaPickupLocationDiscovery discovery)
            return StatusCode(503, new { code = "BOSTA_PICKUP_DISCOVERY_UNAVAILABLE" });
        var result = await discovery.GetPickupLocationsAsync(resolution.Credentials, ct);
        var current = await db.CarrierAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == carrierAccountId && item.TenantId == tenant && !item.IsDeleted)
            .Select(item => new { item.ActiveCredentialReferenceId, item.Environment }).SingleOrDefaultAsync(ct);
        if (current == null || current.ActiveCredentialReferenceId != account.ActiveCredentialReferenceId || current.Environment != account.Environment)
            return Conflict(new { code = "BOSTA_PICKUP_SAVED_CONFIGURATION_CHANGED" });
        var currentResolution = await authority.ResolveConnectionCredentialsAsync(tenant, CarrierCodes.Bosta, ct);
        if (!BostaPickupVerificationCache.SameConfiguration(resolution, currentResolution))
            return Conflict(new { code = "BOSTA_PICKUP_SAVED_CONFIGURATION_CHANGED" });
        var selection = BostaPickupSelections.Resolve(resolution.Credentials, result);
        var proof = pickupVerification?.Observe(tenant, resolution.Configuration!.Id, account.Id, account.ActiveCredentialReferenceId, resolution.Credentials, result);
        return result.Succeeded ? Ok(new { accountId = account.Id, environment = account.Environment.ToString(), locations = result.Locations,
            pickupSelection = new { mode = selection.UsesProviderDefault ? "ProviderDefault" : "Explicit", resolvedLocationId = selection.Location?.Id,
                errorCode = selection.ErrorCode, verifiedAtUtc = proof?.VerifiedAtUtc, validUntilUtc = proof?.ValidUntilUtc, requiresRevalidationBeforeBooking = true } })
            : StatusCode(502, new { code = result.ErrorCode ?? "BOSTA_PICKUP_DISCOVERY_FAILED" });
    }
}
