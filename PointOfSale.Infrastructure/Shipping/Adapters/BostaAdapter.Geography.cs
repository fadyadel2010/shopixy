using System.Text.Json;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

public partial class BostaAdapter
{
    public async Task<BostaGeographyResult> GetGeographyAsync(CarrierCredentials credentials, string? cityId,
        CancellationToken cancellationToken = default)
    {
        BostaGeographyResult Fail(string code) => new(false, [], code, DateTime.UtcNow);
        if (cityId != null && !IsBoundedIdentifier(cityId, 200)) return Fail("BOSTA_CITY_ID_INVALID");
        var pickups = await GetPickupLocationsAsync(credentials, cancellationToken);
        if (!pickups.Succeeded) return Fail(pickups.ErrorCode!);
        // This integration deliberately admits domestic Egypt only. Country identity
        // comes from the authenticated account response, never a hardcoded Mongo ID.
        var countries = pickups.Locations.Where(p => p.CountryCode == "EG").Select(p => p.CountryId).Distinct().ToArray();
        if (countries.Length != 1 || !IsBoundedIdentifier(countries[0], 200)) return Fail("BOSTA_COUNTRY_REFERENCE_REQUIRED");
        var places = new List<BostaGeographicReference> { new("country", countries[0]!, "Egypt", "EG", null, true, true) };
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        try
        {
            using var citiesDoc = await ReadGeographyAsync(baseUrl + "/cities?countryId=" + Uri.EscapeDataString(countries[0]!), credentials, cancellationToken);
            var cities = citiesDoc.RootElement.GetProperty("data").GetProperty("list");
            if (cities.ValueKind != JsonValueKind.Array || cities.GetArrayLength() is 0 or > 1000) return Fail("BOSTA_GEOGRAPHY_RESPONSE_INVALID");
            foreach (var city in cities.EnumerateArray())
            {
                var id = BoundedText(city, "_id", 200); var name = BoundedText(city, "name", 200); var code = BoundedText(city, "code", 50);
                if (!IsBoundedIdentifier(id, 200) || name == null || code?.StartsWith("EG-", StringComparison.Ordinal) != true)
                    return Fail("BOSTA_GEOGRAPHY_RESPONSE_INVALID");
                places.Add(new("city", id!, name, code, countries[0], Available(city, "pickupAvailability"), Available(city, "dropOffAvailability")));
            }
            if (places.Select(p => (p.Type, p.Id)).Distinct().Count() != places.Count) return Fail("BOSTA_GEOGRAPHY_AMBIGUOUS");
            if (cityId == null) return new(true, places, null, DateTime.UtcNow);
            if (!places.Any(p => p.Type == "city" && p.Id == cityId)) return Fail("BOSTA_CITY_NOT_IN_REFERENCE");
            using var districtsDoc = await ReadGeographyAsync(baseUrl + "/cities/" + Uri.EscapeDataString(cityId) + "/districts", credentials, cancellationToken);
            var districts = districtsDoc.RootElement.GetProperty("data");
            if (districts.ValueKind != JsonValueKind.Array || districts.GetArrayLength() is 0 or > 10000) return Fail("BOSTA_GEOGRAPHY_RESPONSE_INVALID");
            foreach (var district in districts.EnumerateArray())
            {
                var id = BoundedText(district, "districtId", 200); var name = BoundedText(district, "districtName", 200);
                if (!IsBoundedIdentifier(id, 200) || name == null) return Fail("BOSTA_GEOGRAPHY_RESPONSE_INVALID");
                places.Add(new("district", id!, name, null, cityId, Available(district, "pickupAvailability"), Available(district, "dropOffAvailability")));
            }
            if (places.Select(p => (p.Type, p.Id)).Distinct().Count() != places.Count) return Fail("BOSTA_GEOGRAPHY_AMBIGUOUS");
            return new(true, places, null, DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Fail("CONNECTION_TIMEOUT"); }
        catch (HttpRequestException) { return Fail("BOSTA_GEOGRAPHY_DISCOVERY_FAILED"); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return Fail("BOSTA_GEOGRAPHY_RESPONSE_INVALID"); }
    }

    private static bool Available(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private async Task<JsonDocument> ReadGeographyAsync(string url, CarrierCredentials credentials, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", credentials.ApiKey);
        using var response = await CarrierHttpSafety.SendAsync(_httpClient, request, ct);
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
            return doc;
        doc.Dispose();
        throw new JsonException("Bosta reference data was not acknowledged.");
    }
}
