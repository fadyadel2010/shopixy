namespace PointOfSale.API.DTOs;

public class FinancialAccountDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AccountNumber { get; set; }
    public bool IsSystem { get; set; }
    public decimal Balance { get; set; }
    public Guid? ParentAccountId { get; set; }
    public string? ParentAccountName { get; set; }
    public List<FinancialAccountDto> SubAccounts { get; set; } = new();
}

public class CreateFinancialAccountDto
{
    public string Name { get; set; } = string.Empty;
    public string? AccountNumber { get; set; }
    public Guid? ParentAccountId { get; set; }
}

public class UpdateFinancialAccountDto
{
    public string Name { get; set; } = string.Empty;
    public string? AccountNumber { get; set; }
    public Guid? ParentAccountId { get; set; }
}

public class TransferRequestDto
{
    public Guid SourceId { get; set; }
    public string SourceType { get; set; } = "Account"; // "Account" or "Cashbox"
    public Guid DestinationId { get; set; }
    public string DestinationType { get; set; } = "Account"; // "Account" or "Cashbox"
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime Date { get; set; }
}
