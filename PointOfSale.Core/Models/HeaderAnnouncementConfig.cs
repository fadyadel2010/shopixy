using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Core.Models;

// Header content authority; themes own presentation defaults only. Null config
// preserves pre-authoring theme behavior without rewriting existing stores.
public sealed class HeaderAnnouncementConfig
{
    public bool Enabled { get; set; } = true;
    [StringLength(500)]
    public string? Text { get; set; } = string.Empty;
    [StringLength(500)]
    public string? TextAr { get; set; } = string.Empty;
    [StringLength(2048)]
    public string? Link { get; set; } = string.Empty;
    [StringLength(7)]
    public string? BackgroundColor { get; set; } = string.Empty;
    [StringLength(7)]
    public string? TextColor { get; set; } = string.Empty;
}
