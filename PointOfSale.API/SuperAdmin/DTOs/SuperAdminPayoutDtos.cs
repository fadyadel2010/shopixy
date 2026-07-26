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
    public bool CanRecordCompletedPayout { get; init; }
    public string? ExecutionBlockedCode { get; init; }
    public string? ExecutionBlockedReason { get; init; }
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

public sealed class SuperAdminExecuteVendorPayoutRequestDto
{
    public decimal Amount { get; set; }
    public Guid PayoutMethodId { get; set; }
    public string? TransferReference { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? BankTransactionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
