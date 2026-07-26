using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PointOfSale.API.DTOs.CommerceHub;

public sealed class CommerceHubVendorPaymentsDashboardDto
{
    public Guid VendorId { get; set; }
    public decimal AmountOwedByVendor { get; set; }
    public string Currency { get; set; } = "EGP";
    public string DebtStatus { get; set; } = "ACTIVE";
    public string? RequiredAction { get; set; }
    public bool CanSubmitPayment { get; set; }
    public List<CommerceHubVendorPaymentMethodDto> PaymentMethods { get; set; } = new();
    public List<CommerceHubVendorPaymentHistoryItemDto> PaymentHistory { get; set; } = new();
}

public sealed class CommerceHubVendorPaymentMethodDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DestinationValue { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CommerceHubVendorPaymentHistoryItemDto
{
    public Guid PaymentRequestId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string? TransferReference { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public string DestinationValue { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string? ReviewReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public bool HasProof { get; set; }
    public string? ProofContentType { get; set; }
    public string? ProofFileName { get; set; }
    public long? ProofSizeBytes { get; set; }
}

public sealed class CommerceHubCreateVendorPaymentRequestFormDto
{
    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal Amount { get; set; }

    [Required]
    public Guid MethodId { get; set; }

    [MaxLength(10)]
    public string? Currency { get; set; }

    [MaxLength(200)]
    public string? TransferReference { get; set; }

    [Required]
    [MaxLength(200)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required]
    public IFormFile? ProofFile { get; set; }
}

public sealed class CommerceHubVendorPaymentProofAccessDto
{
    public string FileUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}
