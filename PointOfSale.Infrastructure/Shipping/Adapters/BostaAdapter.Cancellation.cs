using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

public partial class BostaAdapter
{
    public async Task<CancelShipmentResult> CancelShipmentAsync(CarrierCancellationTarget target,
        CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsBoundedIdentifier(target.TrackingNumber, 100) || !IsBoundedIdentifier(target.ExternalReferenceId, 200))
            return UnknownCancellation("BOSTA_CANCELLATION_TARGET_BINDING_REQUIRED");
        var baseUrl = credentials.UseSandbox ? SandboxUrl : ProductionUrl;
        var url = $"{baseUrl}/deliveries/business/{Uri.EscapeDataString(target.TrackingNumber)}";
        try
        {
            var before = await ReadCancellationTargetAsync(url, target, credentials, cancellationToken);
            if (before == null) return UnknownCancellation("BOSTA_CANCELLATION_TARGET_UNVERIFIED");
            if (before.Value.Status == ShipmentStatus.Cancelled)
                return ConfirmCancellation(target, before.Value.Body);
            if (before.Value.Status is not (ShipmentStatus.Pending or ShipmentStatus.ReadyForPickup))
                return new() { Disposition = CarrierCancellationDisposition.Rejected,
                    ErrorCode = "BOSTA_CANCELLATION_PRE_PICKUP_REQUIRED", ErrorMessage = "The provider shipment is not in an admitted pre-pickup state." };

            // Exactly one DELETE. Neither ambiguous HTTP nor failed confirmation authorizes another.
            using var request = new HttpRequestMessage(HttpMethod.Delete, url + "/terminate");
            request.Headers.Add("Authorization", credentials.ApiKey);
            using var response = await CarrierHttpSafety.SendAsync(_httpClient, request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || !TerminateBindsTarget(body, target.ExternalReferenceId))
                return UnknownCancellation("BOSTA_CANCELLATION_CONFIRMATION_UNVERIFIED", before.Value.Body + body);
            var after = await ReadCancellationTargetAsync(url, target, credentials, cancellationToken);
            if (after?.Status != ShipmentStatus.Cancelled)
                return UnknownCancellation("BOSTA_CANCELLATION_STATE_UNCONFIRMED", before.Value.Body + body + after?.Body);
            return ConfirmCancellation(target, before.Value.Body + body + after.Value.Body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            _logger.LogWarning("Bosta cancellation evidence unavailable ({FailureType})", ex.GetType().Name);
            return UnknownCancellation("BOSTA_CANCELLATION_EVIDENCE_UNAVAILABLE");
        }
    }

    private async Task<(ShipmentStatus Status, string Body)?> ReadCancellationTargetAsync(string url, CarrierCancellationTarget target,
        CarrierCredentials credentials, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", credentials.ApiKey);
        using var response = await CarrierHttpSafety.SendAsync(_httpClient, request, ct);
        if (response.StatusCode != HttpStatusCode.OK) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) return null;
        var parsed = JsonSerializer.Deserialize<BostaDeliveryResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var delivery = parsed?.Data;
        if (delivery?.Id != target.ExternalReferenceId || delivery.TrackingNumber != target.TrackingNumber ||
            !TryMapBostaStatus(delivery.GetStateName(), out var status)) return null;
        return (status, body);
    }

    private static bool TerminateBindsTarget(string body, string id)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True &&
            root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("_id", out var deliveryId) && deliveryId.ValueKind == JsonValueKind.String && deliveryId.GetString() == id;
    }

    private static string CancellationHash(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    private static CancelShipmentResult ConfirmCancellation(CarrierCancellationTarget target, string evidence) => new() {
        Success = true, Disposition = CarrierCancellationDisposition.Confirmed, TargetTrackingNumber = target.TrackingNumber,
        EvidenceReference = target.ExternalReferenceId, EvidenceHash = CancellationHash(evidence) };
    private static CancelShipmentResult UnknownCancellation(string code, string? evidence = null) => new() {
        Disposition = CarrierCancellationDisposition.Unknown, ErrorCode = code,
        EvidenceHash = evidence == null ? null : CancellationHash(evidence),
        ErrorMessage = "Bosta cancellation is not confirmed. Reconciliation is required; do not repeat termination." };
}
