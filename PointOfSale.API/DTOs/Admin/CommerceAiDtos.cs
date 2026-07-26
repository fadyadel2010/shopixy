namespace PointOfSale.API.DTOs.Admin;

// =============================================================================
// PHASE 6: Commerce AI Admin Control DTOs
// 
// These DTOs are used for the Commerce AI admin control page.
// Admins can enable/disable the AI assistant on the storefront.
// =============================================================================

/// <summary>
/// Response DTO for Commerce AI status.
/// GET /api/admin/commerce-ai
/// </summary>
public class CommerceAiStatusResponse
{
    /// <summary>
    /// Whether the Commerce AI assistant is currently enabled on the storefront.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When the feature was last enabled.
    /// Null if never enabled or currently disabled.
    /// </summary>
    public DateTime? EnabledAt { get; set; }

    /// <summary>
    /// Who last updated the Commerce AI setting.
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Request DTO for toggling Commerce AI.
/// POST /api/admin/commerce-ai/toggle
/// </summary>
public class CommerceAiToggleRequest
{
    /// <summary>
    /// Whether to enable or disable the Commerce AI assistant.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Response DTO after toggling Commerce AI.
/// </summary>
public class CommerceAiToggleResponse
{
    /// <summary>
    /// The new enabled state.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Success message for the admin.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// When the toggle was applied.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Commerce AI feature information for the admin page.
/// </summary>
public class CommerceAiInfoResponse
{
    /// <summary>
    /// Current enabled status.
    /// </summary>
    public CommerceAiStatusResponse Status { get; set; } = new();

    /// <summary>
    /// Available AI models (display only, no configuration).
    /// </summary>
    public List<CommerceAiModelInfo> Models { get; set; } = new();

    /// <summary>
    /// Supported modes (display only).
    /// </summary>
    public List<string> SupportedModes { get; set; } = new();
}

/// <summary>
/// AI Model information (display only, no API keys).
/// </summary>
public class CommerceAiModelInfo
{
    /// <summary>
    /// Display name of the model.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Model role: "Primary", "Fallback", "Emergency"
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Whether this model is currently available.
    /// </summary>
    public bool Available { get; set; } = true;
}
