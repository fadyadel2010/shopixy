using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminVendorPaymentMethodResponseDto
{
    public IReadOnlyList<SuperAdminVendorPaymentMethodDto> Methods { get; init; } = Array.Empty<SuperAdminVendorPaymentMethodDto>();
}

public sealed class SuperAdminVendorPaymentMethodDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DestinationValue { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public Guid CreatedBy { get; init; }
}

public sealed class SuperAdminCreateVendorPaymentMethodRequestDto
{
    [Required]
    [MaxLength(120)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DestinationValue { get; init; } = string.Empty;

    [Required]
    public string Instructions { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}

public sealed class SuperAdminVendorPaymentRequestQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public CommerceHubVendorPaymentRequestStatus? Status { get; init; }
    public Guid? VendorId { get; init; }
    public string? Search { get; init; }

    public string? Validate()
    {
        if (Page < 1)
        {
            return "Page must be greater than or equal to 1.";
        }

        if (PageSize < 1 || PageSize > MaxPageSize)
        {
            return $"PageSize must be between 1 and {MaxPageSize}.";
        }

        return null;
    }
}

public sealed class SuperAdminVendorPaymentRequestListResponseDto
{
    public IReadOnlyList<SuperAdminVendorPaymentRequestItemDto> Requests { get; init; } = Array.Empty<SuperAdminVendorPaymentRequestItemDto>();
}

public sealed class SuperAdminVendorPaymentRequestItemDto
{
    public Guid PaymentRequestId { get; init; }
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public Guid PaymentMethodId { get; init; }
    public string MethodName { get; init; } = string.Empty;
    public string DestinationValue { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EGP";
    public string Status { get; init; } = string.Empty;
    public string? TransferReference { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTime? RejectedAtUtc { get; init; }
    public Guid? RejectedBy { get; init; }
    public string? ReviewReason { get; init; }
    public bool HasProof { get; init; }
    public string? ProofContentType { get; init; }
    public string? ProofFileName { get; init; }
    public long? ProofSizeBytes { get; init; }
}

public sealed class SuperAdminApproveVendorPaymentRequestDto
{
    [Required]
    public string Reason { get; init; } = string.Empty;

    public bool AllowOverpayment { get; init; }
}

public sealed class SuperAdminRejectVendorPaymentRequestDto
{
    [Required]
    public string Reason { get; init; } = string.Empty;
}
