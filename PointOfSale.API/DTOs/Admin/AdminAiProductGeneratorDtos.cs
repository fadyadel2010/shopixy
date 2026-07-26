using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs.Admin;

public class AdminAiProductGenerationRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 3)]
    public string ProductIdea { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Language { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(30)]
    public string? Tone { get; set; }

    [Range(1, 12)]
    public int FeatureCount { get; set; } = 5;

    [Range(0, 12)]
    public int FaqCount { get; set; } = 3;
}

public class AdminAiGeneratedProductResponse
{
    public string Name { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
    public List<string> MetaKeywords { get; set; } = new();
    public string Slug { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();
    public List<AdminAiGeneratedFaqItemDto> Faq { get; set; } = new();
}

public class AdminAiGeneratedFaqItemDto
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}
