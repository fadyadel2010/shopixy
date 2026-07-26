using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PointOfSale.API.DTOs.CommerceHub;

public sealed class RefundEvidenceUploadUrlRequestDto
{
    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    [Range(1, 50 * 1024 * 1024)]
    public long FileSizeBytes { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 64)]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class RefundEvidenceUploadUrlResponseDto
{
    public string UploadUrl { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string UploadMethod { get; set; } = "PUT";
    public string ContentType { get; set; } = string.Empty;
    public Dictionary<string, string> UploadHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RefundEvidenceUploadFormRequestDto
{
    [Required]
    public IFormFile? File { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 64)]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class RefundEvidenceUploadResponseDto
{
    public string FileUrl { get; set; } = string.Empty;
}

public sealed class RefundEvidenceMediaDto
{
    public Guid MediaId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public string MediaType { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string? Hash { get; init; }
    public DateTime? UploadedAtUtc { get; init; }
    public string FileName { get; init; } = string.Empty;
}
