namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// Represents a single version entry in the store config version history.
/// Phase: Storefront Admin Hardening - P1
/// </summary>
public class VersionHistoryEntry
{
    /// <summary>
    /// Version number (incremental).
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// When the version was published (UTC).
    /// </summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// User ID or identifier who published this version.
    /// </summary>
    public string PublishedBy { get; set; } = "System";

    /// <summary>
    /// Stable user identifier for the publishing actor when available.
    /// </summary>
    public string? PublishedByUserId { get; set; }

    /// <summary>
    /// Type of change: "Theme Published", "Page Published", "Navigation Published", etc.
    /// </summary>
    public string ChangeType { get; set; } = "Unknown";

    /// <summary>
    /// Historical publication used as evidence for a rollback publication.
    /// Null for ordinary publications.
    /// </summary>
    public int? RestoredFromVersion { get; set; }

    /// <summary>
    /// Summary of changes made in this version.
    /// </summary>
    public string? ChangeSummary { get; set; }

    /// <summary>
    /// Whether this version is the currently live version.
    /// </summary>
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Response containing the version history for a store configuration.
/// Phase: Storefront Admin Hardening - P1
/// </summary>
public class VersionHistoryResponse
{
    /// <summary>
    /// Current live version number.
    /// </summary>
    public int CurrentVersion { get; set; }

    /// <summary>
    /// Total number of stored versions (max 50 due to retention policy).
    /// </summary>
    public int TotalVersions { get; set; }

    /// <summary>
    /// List of version entries, ordered newest first.
    /// </summary>
    public List<VersionHistoryEntry> Versions { get; set; } = new();
}
