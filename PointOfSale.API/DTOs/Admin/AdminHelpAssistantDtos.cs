using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs.Admin;

public class AdminHelpAssistantRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Question { get; set; } = string.Empty;

    [Required]
    [StringLength(200000, MinimumLength = 1)]
    public string HelpArticleContent { get; set; } = string.Empty;

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Route { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Module { get; set; }

    [StringLength(2000)]
    public string? PageDescription { get; set; }

    [StringLength(30000)]
    public string? UiContext { get; set; }

    [StringLength(100)]
    public string? Intent { get; set; }
}

public class AdminHelpAssistantResponse
{
    public bool Success { get; set; }

    public string Answer { get; set; } = string.Empty;

    public bool IsFallback { get; set; }
}
