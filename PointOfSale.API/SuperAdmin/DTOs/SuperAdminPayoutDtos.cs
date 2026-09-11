namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminVendorPayoutQueueItemDto
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string Currency { get; init; } = "EGP";
    public decimal TotalPayable { get; init; }
    public decimal EligibleAmount { get; init; }
    public decimal HoldbackAmount { get; init; }
    public decimal CompletedPayoutsTotal { get; init; }
    public decimal PendingPayoutsTotal { get; init; }
    public DateTime? LastOrderAt { get; init; }
    public Guid? DefaultPayoutMethodId { get; init; }
    public string? DefaultPayoutMethodType { get; init; }
    public string? DefaultPayoutDestinationValue { get; init; }
    public List<SuperAdminVendorPayoutMethodChoiceDto> PayoutMethods { get; init; } = new();
    public List<string> RiskFlags { get; init; } = new();
    public SuperAdminActiveVendorPayoutDto? ActivePayout { get; init; }
    public bool CanPreparePayout { get; init; }
    public bool CanCompletePayout { get; init; }
    public string? ExecutionBlockedCode { get; init; }
    public string? ExecutionBlockedReason { get; init; }
}

public sealed class SuperAdminActiveVendorPayoutDto
{
    public Guid PayoutId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public string Status { get; init; } = string.Empty;
    public Guid? PayoutMethodId { get; init; }
    public string Method { get; init; } = string.Empty;
    public string? DestinationValueSnapshot { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string? ExternalTransferIdempotencyKey { get; init; }
    public string? ExternalTransferHandoffToken { get; init; }
    public DateTime? ExternalTransferHandoffCommittedAtUtc { get; init; }
}

public sealed class SuperAdminVendorPayoutMethodChoiceDto
{
    public Guid Id { get; init; }
    public string MethodType { get; init; } = string.Empty;
    public string DestinationValue { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public bool IsVerified { get; init; }
}

public sealed class SuperAdminVendorPayoutQueueResponseDto
{
    public IReadOnlyList<SuperAdminVendorPayoutQueueItemDto> Payouts { get; init; } = Array.Empty<SuperAdminVendorPayoutQueueItemDto>();
    public IReadOnlyList<SuperAdminVendorPayoutQueueCurrencySummaryDto> CurrencySummaries { get; init; } = Array.Empty<SuperAdminVendorPayoutQueueCurrencySummaryDto>();
    public bool HasCurrencyMix { get; init; }
    public string? AggregateCurrency { get; init; }
    public decimal TotalPayableAmount { get; init; }
    public decimal TotalEligibleAmount { get; init; }
    public decimal TotalHeldAmount { get; init; }
}

public sealed class SuperAdminVendorPayoutQueueResultDto
{
    public IReadOnlyList<SuperAdminVendorPayoutQueueItemDto> Items { get; init; } = Array.Empty<SuperAdminVendorPayoutQueueItemDto>();
    public IReadOnlyList<SuperAdminVendorPayoutQueueCurrencySummaryDto> CurrencySummaries { get; init; } = Array.Empty<SuperAdminVendorPayoutQueueCurrencySummaryDto>();
    public bool HasCurrencyMix { get; init; }
    public string? AggregateCurrency { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public decimal TotalPayableAmount { get; init; }
    public decimal TotalEligibleAmount { get; init; }
    public decimal TotalHeldAmount { get; init; }
}

public sealed class SuperAdminVendorPayoutQueueCurrencySummaryDto
{
    public string Currency { get; init; } = string.Empty;
    public decimal TotalPayableAmount { get; init; }
    public decimal TotalEligibleAmount { get; init; }
    public decimal TotalHeldAmount { get; init; }
}

public sealed class SuperAdminPrepareVendorPayoutRequestDto
{
    public decimal Amount { get; set; }
    public Guid PayoutMethodId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SuperAdminCompleteVendorPayoutRequestDto
{
    public string? ExternalReferenceId { get; set; }
    public string? BankTransactionId { get; set; }
    public string ExternalTransferHandoffToken { get; set; } = string.Empty;
    public bool ExternalTransferConfirmedExecuted { get; set; }
}
