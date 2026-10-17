using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Controllers;

public sealed record BostaDestinationSelection(string CityId, string DistrictId);

[ApiController]
[Authorize(Roles = "Owner,Admin,Manager")]
[Authorize(Policy = PosPermissions.ShippingManage)]
public sealed class AdminBostaDestinationsController(ApplicationDbContext db, ITenantContext tenantContext,
    ICarrierIntegrationAuthority authority, IShippingCarrierRegistry registry) : ControllerBase
{
    private BostaNativeDestinationService Service => new(db, authority, registry);
    [HttpGet("api/admin/shipping/carrier-accounts/{accountId:guid}/bosta/native-geography")]
    public Task<IActionResult> References(Guid accountId, [FromQuery] string? cityId, CancellationToken ct) => Safe(async () =>
    {
        var result = await Service.DiscoverAsync(tenantContext.RequiredTenantId, accountId, cityId, ct);
        var saved = await Service.SavedAsync(tenantContext.RequiredTenantId, accountId, ct);
        return Ok(new { accountId, environment = saved.Account.Environment.ToString(), result.ObservedAtUtc, result.Places });
    });

    [HttpPost("api/admin/shipping/carrier-accounts/{accountId:guid}/bosta/destinations/validate")]
    public Task<IActionResult> Validate(Guid accountId, BostaDestinationSelection input, CancellationToken ct) => Safe(async () =>
    {
        var result = await Service.DiscoverAsync(tenantContext.RequiredTenantId, accountId, input.CityId, ct);
        var selected = BostaNativeDestination.Select(result, input.CityId, input.DistrictId);
        return Ok(new { accountId, validated = true, serviceable = true, selected.Country, selected.City, selected.District,
            result.ObservedAtUtc, bookingReady = false, requiresShipmentEvidence = true });
    });

    [HttpGet("api/admin/shipments/{shipmentId:guid}/bosta-destination")]
    public Task<IActionResult> Read(Guid shipmentId, CancellationToken ct) => Safe(async () =>
    {
        var tenant = tenantContext.RequiredTenantId;
        var s = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenant && !s.IsDeleted && s.CarrierCode == "bosta", ct);
        if (s == null) return NotFound(new { code = "SHIPMENT_NOT_FOUND" });
        var accountId = await db.CarrierAccounts.IgnoreQueryFilters().Where(a => a.TenantId == tenant && a.CarrierCode == "bosta" && !a.IsDeleted)
            .Select(a => a.Id).SingleOrDefaultAsync(ct);
        return Ok(new { accountId, evidence = BostaNativeDestination.IsNative(s.CarrierGeographicMappingSnapshotJson) ? View(BostaNativeDestination.Restore(s)) : null,
            canSelect = accountId != Guid.Empty && s.CarrierGeographicMappingSnapshotJson == null && s.CarrierRequestSnapshotJson == null &&
                !CarrierRegistrationEvidence.HasOutboundEvidence(s) && s.Status is Core.Enums.ShipmentStatus.Pending or Core.Enums.ShipmentStatus.PendingExternalRegistration });
    });

    [HttpPut("api/admin/shipping/carrier-accounts/{accountId:guid}/bosta/destinations/{shipmentId:guid}")]
    [Authorize(Policy = PosPermissions.ShippingFulfill)]
    public Task<IActionResult> Save(Guid accountId, Guid shipmentId, BostaDestinationSelection input, CancellationToken ct) => Safe(async () =>
    {
        var evidence = await Service.SaveAsync(tenantContext.RequiredTenantId, accountId, shipmentId, input.CityId, input.DistrictId, ct);
        return Ok(new { evidence = View(evidence), bookingReady = false, requiresRevalidationBeforeBooking = true });
    });
    private static object View(BostaNativeDestination e) => new { e.CarrierAccountId, e.UseSandbox, e.Country, e.City, e.District, e.ObservedAtUtc };
    private async Task<IActionResult> Safe(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (KeyNotFoundException) { return NotFound(new { code = "BOSTA_RESOURCE_NOT_FOUND" }); }
        catch (ArgumentException ex) { return Conflict(new { code = ex.Message }); }
    }
}
