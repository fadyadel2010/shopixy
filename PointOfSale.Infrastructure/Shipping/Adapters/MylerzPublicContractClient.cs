using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

// Retained official Swagger v1, integration.mylerz.net. These are wire schemas,
// not an admitted merchant service catalog or proof of production entitlement.
public sealed record MylerzChargesRequest(decimal CODValue, string WarehouseName, string CustomerZoneCode,
    decimal PackageWeight, bool IsFulfillment, string PackageServiceTypeCode, string PackageServiceCode,
    string PaymentTypeCode, string ServiceCategoryCode);
public sealed record MylerzCharges(decimal? CODValue, decimal? TransferValue, decimal? ShippingFees, decimal? Charges,
    decimal? FulfillmentFees, decimal? NetTransferValue, decimal? VATPercetage, decimal? VAT, decimal? TotalTransfer);
public sealed record MylerzPiece(int pieceNo, string Weight, string Dimensions, int Quantity,
    string? ItemCategory = null, string? SpecialNotes = null, string? PieceReferenceNumber = null,
    string? HSTariffCode = null, decimal? ItemValue = null);
public sealed record MylerzOrder(
    string Description, decimal Total_Weight, string Service_Type, string Service, string Service_Category,
    string Payment_Type, string COD_Value, string Customer_Name, string Mobile_No, string Street,
    string Customer_ReferenceNumber, string Reference, string Country, string Currency, string WarehouseName,
    MylerzPiece[] Pieces, string? City = null, string? District = null, string? Neighborhood = null,
    string? CustomerAddressZipCode = null, string? Customer_Email = null, string? Building_No = null,
    string? Floor_No = null, string? Apartment_No = null, string? Special_Notes = null);
public sealed record MylerzPackageIdentity(string? Barcode, string? ReferenceNumber = null, string? PostalUPNumber = null);
public sealed record MylerzPackageResult(int? packageNo, string? Reference, string? BarCode, string? Status,
    string? PostalUPNumber, JsonElement? Pieces, string? ErrorCode, string? ErrorMessage);
public sealed record MylerzOrderResult(string? PickupOrderCode, string? PickupDateTime, MylerzPackageResult[]? Packages,
    string? ErrorCode, string? ErrorMessage);
public sealed record MylerzTrackLog(string? StatusCode, string? StatusArName, string? StatusEnName, string? ChangedDate, string? TimeZone);
public sealed record MylerzTracking(string? Barcode, string? RefNumber, string? PostalUPNumber, string? PickupDate,
    decimal? CODValue, MylerzTrackLog[]? TrackLog);
public sealed record MylerzContractResponse<T>(bool? IsErrorState, T? Value, string? ErrorDescription, JsonElement? ErrorMetadata);

public sealed class MylerzContractException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Common transport/schema surface only. No automatic repost, token prefix/expiry
/// inference, canonical status mapping, quote-total/currency inference or money authority.
/// A caller must independently qualify the account and downstream Authorization
/// header contract. RequestTokenAsync alone tests the published password form.
/// </summary>
public sealed class MylerzPublicContractClient(HttpClient http)
{
    public const string BaseUrl = "https://integration.mylerz.net/";
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<string> RequestTokenAsync(string username, string password, CancellationToken ct = default)
    {
        Text(username, 200); Text(password, 1000);
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "password", ["username"] = username, ["password"] = password })
        };
        var body = await SendAsync(request, ct);
        // Swagger defines a string, not an OAuth object or refresh/lifetime contract.
        // Return the opaque string; do not invent Bearer or indefinitely cache it.
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.String) throw new MylerzContractException("MYLERZ_TOKEN_RESPONSE_SCHEMA_UNQUALIFIED");
            var value = json.RootElement.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 16384 || value.Any(char.IsControl))
                throw new MylerzContractException("MYLERZ_TOKEN_RESPONSE_INVALID");
            return value;
        }
        catch (JsonException) { throw new MylerzContractException("MYLERZ_TOKEN_RESPONSE_SCHEMA_UNQUALIFIED"); }
    }

    public async Task<MylerzCharges> GetExpectedChargesAsync(MylerzChargesRequest input, string qualifiedAuthorization, CancellationToken ct = default)
    {
        Text(input.WarehouseName, 200); Text(input.CustomerZoneCode, 50);
        Services(input.PackageServiceTypeCode, input.PackageServiceCode, input.PaymentTypeCode, input.ServiceCategoryCode);
        if (input.PackageWeight <= 0 || input.CODValue is < -999999 or > 999999) throw new MylerzContractException("MYLERZ_CHARGES_REQUEST_INVALID");
        var value = await PostAsync<MylerzChargesRequest, MylerzCharges>("api/packages/GetExpectedCharges", input, qualifiedAuthorization, ct);
        if (value.Charges == null || value.ShippingFees == null || value.VAT == null || value.Charges < 0 || value.ShippingFees < 0 || value.VAT < 0)
            throw new MylerzContractException("MYLERZ_CHARGES_RESPONSE_INVALID");
        // No total/currency calculation: Swagger omits billing denomination and tax inclusion semantics.
        return value;
    }

    public async Task<MylerzOrderResult> AddOrderAsync(MylerzOrder input, string qualifiedAuthorization, CancellationToken ct = default)
    {
        ValidateOrder(input);
        var value = await PostAsync<MylerzOrder[], MylerzOrderResult>("api/Orders/AddOrders", [input], qualifiedAuthorization, ct);
        if (!string.IsNullOrWhiteSpace(value.ErrorCode) || value.Packages?.Length != 1)
            throw new MylerzContractException("MYLERZ_CREATE_RESPONSE_PARTIAL_OR_AMBIGUOUS");
        var package = value.Packages[0];
        if (package == null || !string.IsNullOrWhiteSpace(package.ErrorCode) || package.Reference != input.Reference || !ValidBarcode(package.BarCode))
            throw new MylerzContractException("MYLERZ_CREATE_RESPONSE_IDENTITY_INVALID");
        // Raw Status and Pieces are retained, not promoted to known lifecycle or label evidence.
        return value;
    }

    public async Task<MylerzTracking> TrackAsync(MylerzPackageIdentity identity, string qualifiedAuthorization, CancellationToken ct = default)
    {
        if (!ValidBarcode(identity.Barcode) || identity.ReferenceNumber != null || identity.PostalUPNumber != null)
            throw new MylerzContractException("MYLERZ_TRACKING_BARCODE_REQUIRED");
        var values = await PostAsync<MylerzPackageIdentity[], MylerzTracking[]>("api/packages/TrackPackages", [identity], qualifiedAuthorization, ct);
        if (values.Length != 1 || values[0] == null || values[0].Barcode != identity.Barcode || values[0].TrackLog == null || values[0].TrackLog!.Length == 0)
            throw new MylerzContractException("MYLERZ_TRACKING_RESPONSE_IDENTITY_INVALID");
        DateTimeOffset? previous = null;
        foreach (var log in values[0].TrackLog!)
        {
            if (log == null || string.IsNullOrWhiteSpace(log.StatusCode) || log.ChangedDate == null || log.ChangedDate.Length > 100 ||
                !Regex.IsMatch(log.ChangedDate, @"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) ||
                !DateTimeOffset.TryParse(log.ChangedDate, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var at) || at > DateTimeOffset.UtcNow.AddMinutes(5) ||
                previous.HasValue && at < previous.Value)
                throw new MylerzContractException("MYLERZ_TRACKING_TIME_OR_STATE_UNQUALIFIED");
            previous = at;
        }
        // CODValue is a vendor field, never proof of collected money.
        return values[0];
    }

    public static void ValidateOrder(MylerzOrder input)
    {
        Text(input.Description, 1000); Text(input.Customer_Name, 200); Text(input.Mobile_No, 20); Text(input.Street, 500);
        Text(input.WarehouseName, 200); Text(input.Reference, 150); Text(input.Customer_ReferenceNumber, 150);
        Services(input.Service_Type, input.Service, input.Payment_Type, input.Service_Category);
        if (input.Total_Weight <= 0 || input.Pieces == null || input.Pieces.Length != 1 || input.Pieces[0] == null ||
            input.Pieces[0].pieceNo is < 0 or > 100 || input.Pieces[0].Quantity is < 1 or > 999999 ||
            !decimal.TryParse(input.COD_Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var cod) || cod < 0 ||
            !Geographic.CanonicalAddressEvidence.RecognizedCountry(input.Country) || !Core.Financial.Iso4217CurrencyProvider.IsValidCurrency(input.Currency))
            throw new MylerzContractException("MYLERZ_ORDER_REQUEST_INVALID");
        Text(input.Pieces[0].Weight, 100); Text(input.Pieces[0].Dimensions, 200);
        Optional(input.Pieces[0].SpecialNotes, 500); Optional(input.Pieces[0].PieceReferenceNumber, 300); Optional(input.Pieces[0].HSTariffCode, 12);
        if (input.Pieces[0].ItemValue is < -999999 or > 999999) throw new MylerzContractException("MYLERZ_PIECE_VALUE_INVALID");
        // String units/format must be merchant-qualified before invoking this surface.
        if (input.Payment_Type.Equals("PP", StringComparison.OrdinalIgnoreCase) && cod != 0)
            throw new MylerzContractException("MYLERZ_PREPAID_COD_CONFLICT");
        Optional(input.City, 100); Optional(input.District, 100); Optional(input.Neighborhood, 200);
        Optional(input.Customer_Email, 254); Optional(input.Building_No, 50); Optional(input.Floor_No, 50); Optional(input.Apartment_No, 50);
        Optional(input.Special_Notes, 500);
        Optional(input.CustomerAddressZipCode, 20);
    }

    private async Task<TOut> PostAsync<TIn, TOut>(string path, TIn input, string authorization, CancellationToken ct)
    {
        Text(authorization, 16384);
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path) { Content = JsonContent.Create(input, options: Json) };
        request.Headers.Add("Authorization", authorization);
        var body = await SendAsync(request, ct);
        try
        {
            using var document = JsonDocument.Parse(body);
            RejectDuplicateMembers(document.RootElement);
            var result = JsonSerializer.Deserialize<MylerzContractResponse<TOut>>(body, Json);
            if (result?.IsErrorState != false || result.Value == null)
                throw new MylerzContractException("MYLERZ_REMOTE_REJECTION_OR_RESPONSE_INVALID");
            return result.Value;
        }
        catch (JsonException) { throw new MylerzContractException("MYLERZ_RESPONSE_SCHEMA_INVALID"); }
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            // No retry, even for 401/429/timeout. Registration absence is not established.
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new MylerzContractException(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    ? "MYLERZ_REMOTE_AUTHORIZATION_FAILED" : "MYLERZ_REMOTE_HTTP_REJECTED");
            await response.Content.LoadIntoBufferAsync(1024 * 1024, deadline.Token);
            return await response.Content.ReadAsStringAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new MylerzContractException(request.RequestUri?.AbsolutePath == "/token" ? "CONNECTION_TIMEOUT" : "MYLERZ_TRANSPORT_OUTCOME_UNCERTAIN"); }
        catch (HttpRequestException) { throw new MylerzContractException(request.RequestUri?.AbsolutePath == "/token" ? "NETWORK_ERROR" : "MYLERZ_TRANSPORT_OUTCOME_UNCERTAIN"); }
    }
    private static bool ValidBarcode(string? value) => value?.Length == 14 && value.All(c => c is >= '0' and <= '9');
    private static void Text(string value, int limit)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl)) throw new MylerzContractException("MYLERZ_REQUIRED_FIELD_INVALID"); }
    private static void Optional(string? value, int limit) { if (value != null && (value.Length > limit || value.Any(char.IsControl))) throw new MylerzContractException("MYLERZ_FIELD_INVALID"); }
    private static void Services(string type, string service, string payment, string category)
    {
        Text(type, 3); Text(service, 2); Text(payment, 3); Text(category, 20);
        if (!new[] { "DTD", "DTC", "CTD", "CTC" }.Contains(type.ToUpperInvariant()) || !new[] { "SD", "ND" }.Contains(service.ToUpperInvariant()) ||
            !new[] { "PP", "COD", "CC" }.Contains(payment.ToUpperInvariant()) || !new[] { "DELIVERY", "RETURN", "PERSONAL DELIVERY" }.Contains(category.ToUpperInvariant()))
            throw new MylerzContractException("MYLERZ_SERVICE_SCHEMA_INVALID");
    }
    private static void RejectDuplicateMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in value.EnumerateObject())
            {
                if (!seen.Add(member.Name)) throw new MylerzContractException("MYLERZ_RESPONSE_DUPLICATE_MEMBER");
                RejectDuplicateMembers(member.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) RejectDuplicateMembers(child);
    }
}
