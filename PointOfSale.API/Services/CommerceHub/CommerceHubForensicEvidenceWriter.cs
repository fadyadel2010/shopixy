using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public static class CommerceHubForensicEvidenceWriter
{
    public static void AppendMerchantLifecycle(
        ApplicationDbContext dbContext,
        CommerceHubListing listing,
        string eventType,
        Guid? actorUserId,
        string? correlationId,
        string? requestId,
        string? reason,
        object? details = null)
    {
        dbContext.CommerceHubMerchantLifecycleLedgerEntries.Add(
            CommerceHubMerchantLifecycleLedgerEntry.Create(
                listing,
                eventType,
                actorUserId,
                correlationId,
                requestId,
                reason,
                details == null ? null : JsonSerializer.Serialize(details)));
    }

    public static void AppendRecoveryOperation(
        ApplicationDbContext dbContext,
        string operationType,
        string actorIdentity,
        Guid? actorUserId,
        Guid? tenantId,
        Guid? listingId,
        string scope,
        string outcome,
        DateTime startedAtUtc,
        string? correlationId,
        string? requestId,
        string? reason,
        object? details = null,
        Guid? operationId = null)
    {
        dbContext.CommerceHubRecoveryOperationLedgerEntries.Add(
            CommerceHubRecoveryOperationLedgerEntry.Create(
                operationId ?? Guid.NewGuid(),
                operationType,
                actorIdentity,
                actorUserId,
                tenantId,
                listingId,
                scope,
                outcome,
                startedAtUtc,
                DateTime.UtcNow,
                correlationId,
                requestId,
                reason,
                details == null ? null : JsonSerializer.Serialize(details)));
    }
}
