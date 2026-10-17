using System.Text.Json.Serialization;

namespace PointOfSale.Core.Interfaces.Payments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CarrierRemittanceRequest(Guid OperationId, IReadOnlyList<Guid> OrderIds,
    DateTimeOffset ReceivedAt, string ReceiptReference, bool ActualReceiptAttested,
    string ReceiptChannel = "PhysicalMerchantCash", string? Notes = null, string? EvidenceReference = null);

public sealed record CarrierRemittanceCandidate(Guid OrderId, string OrderReference, decimal Amount,
    string Currency, string CarrierCode, Guid ConfigurationId, string AccountId, string Environment);
public sealed record CarrierRemittanceSelection(IReadOnlyList<CarrierRemittanceCandidate> Orders,
    string Currency, string DestinationAccountCode, int Page, bool HasMore);
public sealed record CarrierRemittanceResult(Guid OperationId, Guid OriginalOperatorId, string Outcome,
    string ConfirmationBasis, string ReceiptChannel, string ReceiptReference, DateTime ReceivedAtUtc,
    DateTime RecordedAtUtc, DateTime PostedAtUtc, decimal GrossAmount, decimal ReceivedAmount, decimal Fees,
    string Currency, string CarrierCode, Guid ConfigurationId, string AccountId, string Environment,
    IReadOnlyList<Guid> OrderIds, Guid DestinationAccountId, string DestinationAccountCode,
    Guid JournalId, string JournalReference, string AccountingOutcome, bool WasIdempotent);

public interface ICarrierRemittanceService
{
    Task<CarrierRemittanceSelection> SelectAsync(Guid tenantId, Guid actorId, int page = 1, CancellationToken ct = default);
    Task<CarrierRemittanceResult> ReceiveAsync(Guid tenantId, Guid actorId, CarrierRemittanceRequest request, CancellationToken ct = default);
    Task<CarrierRemittanceResult?> GetAsync(Guid tenantId, Guid actorId, Guid operationId, CancellationToken ct = default);
}
