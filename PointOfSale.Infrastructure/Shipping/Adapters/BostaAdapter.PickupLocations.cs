using System.Text.Json;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

public partial class BostaAdapter
{
    public async Task<BostaPickupLocationResult> GetPickupLocationsAsync(CarrierCredentials credentials, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credentials.ApiKey)) return new(false, [], "CREDENTIAL_APIKEY_MISSING");
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/pickup-locations");
            request.Headers.Add("Authorization", credentials.ApiKey);
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, request, ct);
            var failure = CarrierConnectionDiagnostics.HttpFailure(response.StatusCode);
            if (failure != null) return new(false, [], failure.ErrorCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 1000 ||
                !data.TryGetProperty("total", out var total) || !total.TryGetInt32(out var count) || count != list.GetArrayLength())
                return new(false, [], "BOSTA_PICKUP_RESPONSE_INVALID");
            var locations = new List<BostaPickupLocationOption>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var location in list.EnumerateArray())
            {
                var id = BoundedText(location, "_id", 200);
                if (!IsBoundedIdentifier(id, 200) || !ids.Add(id!)) return new(false, [], "BOSTA_PICKUP_RESPONSE_INVALID");
                if (!location.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
                    return new(false, [], "BOSTA_PICKUP_RESPONSE_INVALID");
                locations.Add(new(id!, location.TryGetProperty("isDefault", out var primary) && primary.ValueKind == JsonValueKind.True,
                    PickupGeographyText(address, "country", "code"), PickupGeographyText(address, "city", "_id"),
                    PickupGeographyText(address, "city", "name"), PickupGeographyText(address, "district", "_id"),
                    PickupGeographyText(address, "district", "name")) { CountryId = PickupGeographyText(address, "country", "_id") });
            }
            return new(true, locations, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, [], "CONNECTION_TIMEOUT"); }
        catch (HttpRequestException) { return new(false, [], "NETWORK_ERROR"); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(false, [], "BOSTA_PICKUP_RESPONSE_INVALID"); }
    }

    private static string? BoundedText(JsonElement element, string key, int max) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { } text && text.Length is > 0 && text.Length <= max && !text.Any(char.IsControl) ? text : null;
    private static string? PickupGeographyText(JsonElement address, string geography, string key) =>
        address.TryGetProperty(geography, out var value) ? BoundedText(value, key, 200) : null;
}
