namespace PointOfSale.API.DTOs.CommerceHub;

public class CommerceHubWalletBalanceDto
{
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public decimal Balance { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal TotalDebits { get; set; }
    public string Currency { get; set; } = "EGP";
    public int TransactionsCount { get; set; }
    public DateTime? LastTransactionAtUtc { get; set; }
}

public class CommerceHubWalletStatementDto
{
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public decimal Balance { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal TotalDebits { get; set; }
    public string Currency { get; set; } = "EGP";
    public List<CommerceHubWalletStatementTransactionDto> Transactions { get; set; } = new();
}

public class CommerceHubWalletStatementTransactionDto
{
    public Guid TransactionId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RunningTotal { get; set; }
    public string Currency { get; set; } = "EGP";
    public string ReferenceType { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// W1 FIX: Paginated vendor wallet statement with carry-forward balance.
/// Prevents loading all transactions in-memory for high-volume vendors.
/// </summary>
public class CommerceHubWalletStatementPagedDto
{
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public decimal Balance { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal TotalDebits { get; set; }
    public string Currency { get; set; } = "EGP";
    public List<CommerceHubWalletStatementTransactionDto> Transactions { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }

    /// <summary>
    /// Running balance of all transactions BEFORE this page.
    /// Clients can use this as the starting balance for page rendering.
    /// </summary>
    public decimal CarryForwardBalance { get; set; }
}
