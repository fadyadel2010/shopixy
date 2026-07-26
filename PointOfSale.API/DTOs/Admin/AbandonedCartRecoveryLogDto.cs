namespace PointOfSale.API.DTOs.Admin;

/// <summary>
/// DTO for logging abandoned cart recovery results from n8n.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// </summary>
public class AbandonedCartRecoveryLogRequest
{
    public Guid AbandonedCartId { get; set; }
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// HARDENING: API key for authenticating this callback.
    /// Must match TenantAutomationSettings.N8nWebhookApiKey.
    /// </summary>
    public string? ApiKey { get; set; }
    
    public bool FirstMessageSent { get; set; }
    public string? FirstMessageSentAt { get; set; }
    public string? FirstMessageError { get; set; }
    public bool SecondMessageSent { get; set; }
    public string? SecondMessageSentAt { get; set; }
    public string? SecondMessageError { get; set; }
    public string? CompletedAt { get; set; }
}

public class AbandonedCartRecoveryLogResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
