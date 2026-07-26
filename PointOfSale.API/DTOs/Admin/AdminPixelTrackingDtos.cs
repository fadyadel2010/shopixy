using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs.Admin;

public class AdminPixelTrackingDtos
{
    public class PixelTrackingConfigDto
    {
        public bool IsEnabled { get; set; }
        
        [Required]
        public required string Strategy { get; set; }  // "PERCENTAGE" or "FIXED"
        
        [Range(0, 1000000)]
        public decimal Value { get; set; }
        
        public string[] AppliesTo { get; set; } = Array.Empty<string>();  // ["Meta", "Google"]
        
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class UpdatePixelTrackingConfigDto
    {
        public bool IsEnabled { get; set; }
        
        [Required]
        public required string Strategy { get; set; }
        
        [Range(0, 1000000)]
        public decimal Value { get; set; }
        
        public string[] AppliesTo { get; set; } = Array.Empty<string>();
    }

    public class PixelValuePreviewDto
    {
        public decimal ActualOrderTotal { get; set; }
        public decimal PixelReportedValue { get; set; }
        public decimal OverrideDifference { get; set; }
        public required string OverridePercentage { get; set; }
        public required string StrategyDescription { get; set; }
    }

    // Phase M2: Product Overrides
    public class ProductPixelOverrideDto
    {
        public Guid ProductId { get; set; }
        public bool IsEnabled { get; set; }
        public required string Strategy { get; set; }
        public decimal Value { get; set; }
        
        // Granularity
        public bool ApplyToMeta { get; set; }
        public bool ApplyToGoogle { get; set; }
        public bool ApplyToTikTok { get; set; }
        
        public DateTime UpdatedAtUtc { get; set; }
        public Guid UpdatedBy { get; set; }
    }

    public class UpdateProductPixelOverrideDto
    {
        [Required]
        public bool IsEnabled { get; set; }
        
        [Required]
        public required string Strategy { get; set; }
        
        [Range(0, 1000000)]
        public decimal Value { get; set; }
        
        public bool ApplyToMeta { get; set; }
        public bool ApplyToGoogle { get; set; }
        public bool ApplyToTikTok { get; set; }
    }
}
