using System;
using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// DTO for listing pending manual wallet payments for admin verification.
/// </summary>
public class PendingManualPaymentDto
{
    public Guid PaymentIntentId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string WalletType { get; set; } = string.Empty;
    public string WalletPhoneNumber { get; set; } = string.Empty;
    public bool HasProofFile { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PendingManualPaymentPagedResponse
{
    public List<PendingManualPaymentDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Request DTO for rejecting a manual payment.
/// </summary>
public class RejectManualPaymentRequest
{
    [Required(AllowEmptyStrings = false)]
    [MinLength(3)]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
