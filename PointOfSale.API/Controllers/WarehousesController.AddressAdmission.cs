using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs;
using PointOfSale.API.Services.Inventory;

namespace PointOfSale.API.Controllers;

public partial class WarehousesController
{
    [HttpGet("{id:guid}/address-admission/places")]
    public Task<IActionResult> GetWarehouseAddressPlaces(Guid id, [FromServices] WarehouseAddressAdmissionService service,
        [FromQuery] Guid? countryPlaceId = null, [FromQuery] Guid? parentPlaceId = null, CancellationToken cancellationToken = default) =>
        ExecuteWarehouseAddressAdmissionAsync(actor => service.GetPlacesAsync(id, actor, countryPlaceId, parentPlaceId, cancellationToken));

    [HttpPost("{id:guid}/address-admission")]
    public Task<IActionResult> AdmitWarehouseAddress(Guid id, WarehouseAddressAdmissionRequest request,
        [FromServices] WarehouseAddressAdmissionService service, CancellationToken cancellationToken) =>
        ExecuteWarehouseAddressAdmissionAsync(actor => service.AdmitAsync(id, actor, request, cancellationToken));

    private async Task<IActionResult> ExecuteWarehouseAddressAdmissionAsync<T>(Func<Guid, Task<T>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty) return Forbid();
        try { return Ok(await action(actor)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(new { code = "WarehouseNotFoundOrInactive", message = "Select an active warehouse owned by the current tenant." }); }
        catch (ArgumentException ex) { return BadRequest(new { code = ex.Message, message = WarehouseAddressFailureMessage(ex.Message) }); }
    }

    private static string WarehouseAddressFailureMessage(string code) => code switch
    {
        "WarehouseStreetConfirmationRequired" => "Confirm the warehouse's actual street address. StreetLine1 must match its saved Address; correct that field through warehouse management first if needed.",
        "WarehouseContactRequired" => "Save the warehouse's genuine contact name and phone through warehouse management before admitting its address.",
        "WarehouseAdmissionReasonAndStreetRequired" => "Provide a non-empty admission reason (up to 1000 characters) and street (up to 300 characters).",
        "PostalCodeRequired" => "Provide the actual postal code required by the published country profile.",
        "PostalCodeInvalid" => "The postal code does not match the published country rule, exceeds 32 characters, or contains control characters.",
        "PostalCodeForbidden" => "The published country profile does not allow a postal code.",
        "WarehouseHierarchyInvalid" => "Select administrative places and locality linked by the published primary hierarchy for this country.",
        "WarehousePlaceKindOrCountryInvalid" => "Select active, unretired country, administrative-area and locality identities of the correct kind and country.",
        "UnknownOrUnpublishedPlace" or "CountryPlaceIdRequired" or "UnknownOrUnpublishedCountry" or "WarehouseParentPlaceInvalid" => "Select the correct published geographic identities using the warehouse address-admission places lookup.",
        "RequiredAdministrativeLevelMissing" => "Include every administrative level required for delivery and operations by the published country profile.",
        "PublishedCatalogReleaseRequired" or "WarehouseCountryProfileRequired" or "WarehousePostalRuleRequired" or "WarehousePostalRuleInvalid" or "WarehousePostalPolicyUnsupported" => "The published geographic catalog/country profile lacks supported admission rules. An authorized geographic administrator must establish genuine catalog evidence first.",
        _ => code
    };
}
