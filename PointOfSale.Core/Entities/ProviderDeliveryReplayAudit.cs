using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public sealed class ProviderDeliveryReplayAudit : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ReplayId { get; set; }
    public Guid MessageId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? OperatorUserId { get; set; }

    public DateTime ReplayedAtUtc { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ReplaySource { get; set; } = string.Empty;

    [Required]
    public string PreviousStateJson { get; set; } = string.Empty;

    [Required]
    public string NewStateJson { get; set; } = string.Empty;

    public static ProviderDeliveryReplayAudit Create(
        ProviderDeliveryOutboxMessage message,
        string reason,
        string replaySource,
        string? operatorUserId,
        string previousStateJson,
        string newStateJson,
        DateTime replayedAtUtc)
    {
        return new ProviderDeliveryReplayAudit
        {
            TenantId = message.TenantId,
            ReplayId = Guid.NewGuid(),
            MessageId = message.Id,
            Provider = message.Provider,
            OperatorUserId = string.IsNullOrWhiteSpace(operatorUserId) ? null : operatorUserId.Trim(),
            ReplayedAtUtc = replayedAtUtc,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Provider delivery replay" : reason.Trim(),
            ReplaySource = string.IsNullOrWhiteSpace(replaySource) ? "system" : replaySource.Trim(),
            PreviousStateJson = previousStateJson,
            NewStateJson = newStateJson
        };
    }
}
