using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Core.DTOs.Marketplace;

/// <summary>
/// DTO for creating a service review.
/// If the service category is Marketing, Evidence is required.
/// If the service category is NOT Marketing, Evidence must be null.
/// These invariants are enforced at the application service layer.
/// </summary>
public record CreateReviewDto(
    Guid ServiceRequestId,
    [property: Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    int Rating,
    [property: StringLength(5000, ErrorMessage = "Comment must not exceed 5000 characters")]
    string? Comment = null,
    CreateMarketingEvidenceDto? Evidence = null
);
