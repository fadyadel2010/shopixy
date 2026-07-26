using System;
using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents a system-level immutable theme preset.
/// Shared across all tenants.
/// </summary>
public class ThemePreset
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty; // e.g., "modern-light"

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsRtl { get; set; }

    /// <summary>
    /// Base configuration for colors, typography, radius, etc.
    /// stored as JSON.
    /// </summary>
    public string BaseThemeJson { get; set; } = "{}";

    [MaxLength(500)]
    public string? PreviewImageUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
