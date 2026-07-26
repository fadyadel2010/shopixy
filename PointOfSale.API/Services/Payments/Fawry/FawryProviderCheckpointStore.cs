using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Fawry;

public interface IFawryProviderCheckpointStore
{
    FawryProviderCheckpoint Load(PaymentIntent intent);

    Task BeginChargeRegistrationAsync(
        PaymentIntent intent,
        CancellationToken cancellationToken);

    Task PersistAcceptedReferenceAsync(
        PaymentIntent intent,
        string referenceCode,
        DateTime expiresAtUtc,
        string? paymentUrl,
        CancellationToken cancellationToken);

    void ApplyAcceptedReference(PaymentIntent intent, FawryProviderCheckpoint checkpoint);
}

/// <summary>
/// Application-owned durable checkpoint for the single-step PayAtFawry charge mutation.
/// Provider identifiers remain in checkpoint metadata until final application event
/// persistence can commit them atomically to the normal PaymentIntent projection.
/// </summary>
public sealed class FawryProviderCheckpointStore : IFawryProviderCheckpointStore
{
    public const string RegistrationStartedState = "charge_registration_started";
    public const string ReferenceAcceptedState = "provider_reference_accepted_runtime_completed_event_pending";
    public const string CompletedState = "provider_reference_committed";

    private const string ProviderCode = "FAWRY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ApplicationDbContext _dbContext;

    public FawryProviderCheckpointStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public FawryProviderCheckpoint Load(PaymentIntent intent)
    {
        EnsureOwner(intent);

        if (string.IsNullOrWhiteSpace(intent.ProviderMetadata))
        {
            if (HasNoProjectedProviderArtifacts(intent))
            {
                return FawryProviderCheckpoint.NotStarted;
            }

            if (HasCompleteProjectedProviderArtifacts(intent))
            {
                return new FawryProviderCheckpoint(
                    FawryProviderCheckpointState.Completed,
                    intent.ReferenceCode!.Trim(),
                    DateTime.SpecifyKind(intent.ExpiresAtUtc!.Value, DateTimeKind.Utc),
                    Normalize(intent.RedirectUrl));
            }

            throw InvalidCheckpoint("The Fawry provider projection is incomplete and has no durable checkpoint state.");
        }

        FawryProviderCheckpointMetadata metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<FawryProviderCheckpointMetadata>(
                           intent.ProviderMetadata,
                           JsonOptions)
                       ?? throw InvalidCheckpoint("The Fawry checkpoint metadata is empty.");
        }
        catch (JsonException exception)
        {
            throw InvalidCheckpoint("The Fawry checkpoint metadata is malformed.", exception);
        }

        if (!string.Equals(metadata.Provider, ProviderCode, StringComparison.Ordinal) ||
            metadata.TenantId != intent.TenantId ||
            metadata.OrderId != intent.OrderId ||
            metadata.PaymentIntentId != intent.Id ||
            metadata.CheckpointedAtUtc == default)
        {
            throw InvalidCheckpoint("The Fawry checkpoint owner or identity is invalid.");
        }

        if (string.Equals(metadata.State, RegistrationStartedState, StringComparison.Ordinal))
        {
            if (!HasNoProjectedProviderArtifacts(intent) || HasAcceptedMetadata(metadata))
            {
                throw InvalidCheckpoint("The Fawry registration-started checkpoint contains conflicting provider artifacts.");
            }

            return new FawryProviderCheckpoint(
                FawryProviderCheckpointState.RegistrationStarted,
                null,
                null,
                null);
        }

        if (string.Equals(metadata.State, ReferenceAcceptedState, StringComparison.Ordinal))
        {
            if (!HasNoProjectedProviderArtifacts(intent))
            {
                throw InvalidCheckpoint("The pending Fawry accepted-reference checkpoint conflicts with the projected provider fields.");
            }

            return BuildAcceptedCheckpoint(metadata);
        }

        if (string.Equals(metadata.State, CompletedState, StringComparison.Ordinal))
        {
            var completed = BuildAcceptedCheckpoint(metadata) with
            {
                State = FawryProviderCheckpointState.Completed
            };

            if (!HasCompleteProjectedProviderArtifacts(intent) ||
                !string.Equals(intent.ReferenceCode, completed.ReferenceCode, StringComparison.Ordinal) ||
                !string.Equals(intent.ProviderIntentId, completed.ReferenceCode, StringComparison.Ordinal) ||
                intent.ExpiresAtUtc != completed.ExpiresAtUtc ||
                !string.Equals(Normalize(intent.RedirectUrl), completed.PaymentUrl, StringComparison.Ordinal))
            {
                throw InvalidCheckpoint("The completed Fawry checkpoint does not match the projected provider fields.");
            }

            return completed;
        }

        throw InvalidCheckpoint("The Fawry checkpoint state is unknown.");
    }

    public async Task BeginChargeRegistrationAsync(
        PaymentIntent intent,
        CancellationToken cancellationToken)
    {
        var checkpoint = Load(intent);
        if (checkpoint.State != FawryProviderCheckpointState.NotStarted)
        {
            throw InvalidCheckpoint("Fawry charge registration can begin only from an unstarted checkpoint.");
        }

        intent.ProviderMetadata = Serialize(
            intent,
            RegistrationStartedState,
            DateTime.UtcNow,
            referenceCode: null,
            expiresAtUtc: null,
            paymentUrl: null);
        intent.FailureCode = null;
        intent.FailureReason = null;
        intent.FailedAt = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PersistAcceptedReferenceAsync(
        PaymentIntent intent,
        string referenceCode,
        DateTime expiresAtUtc,
        string? paymentUrl,
        CancellationToken cancellationToken)
    {
        var checkpoint = Load(intent);
        if (checkpoint.State != FawryProviderCheckpointState.RegistrationStarted)
        {
            throw InvalidCheckpoint("A Fawry provider reference can be accepted only after the registration-started checkpoint.");
        }

        referenceCode = RequireReference(referenceCode);
        expiresAtUtc = RequireUtcExpiration(expiresAtUtc);
        paymentUrl = Normalize(paymentUrl);

        intent.ProviderMetadata = Serialize(
            intent,
            ReferenceAcceptedState,
            DateTime.UtcNow,
            referenceCode,
            expiresAtUtc,
            paymentUrl);

        // Keep the normal provider projection empty until the accepted checkpoint and
        // PaymentIntentCreated outbox event can be finalized in one SQL transaction.
        intent.ReferenceCode = null;
        intent.ProviderIntentId = null;
        intent.ExpiresAtUtc = null;
        intent.RedirectUrl = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public void ApplyAcceptedReference(PaymentIntent intent, FawryProviderCheckpoint checkpoint)
    {
        EnsureOwner(intent);
        if (checkpoint.State != FawryProviderCheckpointState.ReferenceAccepted ||
            string.IsNullOrWhiteSpace(checkpoint.ReferenceCode) ||
            !checkpoint.ExpiresAtUtc.HasValue)
        {
            throw InvalidCheckpoint("The Fawry accepted-reference checkpoint is incomplete.");
        }

        var current = Load(intent);
        if (current != checkpoint)
        {
            throw InvalidCheckpoint("The Fawry accepted-reference checkpoint changed before finalization.");
        }

        intent.ReferenceCode = checkpoint.ReferenceCode;
        intent.ProviderIntentId = checkpoint.ReferenceCode;
        intent.ExpiresAtUtc = checkpoint.ExpiresAtUtc;
        intent.RedirectUrl = checkpoint.PaymentUrl;
        intent.ProviderMetadata = Serialize(
            intent,
            CompletedState,
            DateTime.UtcNow,
            checkpoint.ReferenceCode,
            checkpoint.ExpiresAtUtc,
            checkpoint.PaymentUrl);
    }

    private static FawryProviderCheckpoint BuildAcceptedCheckpoint(FawryProviderCheckpointMetadata metadata)
    {
        return new FawryProviderCheckpoint(
            FawryProviderCheckpointState.ReferenceAccepted,
            RequireReference(metadata.ReferenceCode),
            metadata.ExpiresAtUtc.HasValue
                ? RequireUtcExpiration(metadata.ExpiresAtUtc.Value)
                : throw InvalidCheckpoint("The Fawry accepted-reference checkpoint has no expiration."),
            Normalize(metadata.PaymentUrl));
    }

    private static string Serialize(
        PaymentIntent intent,
        string state,
        DateTime checkpointedAtUtc,
        string? referenceCode,
        DateTime? expiresAtUtc,
        string? paymentUrl)
    {
        return JsonSerializer.Serialize(
            new FawryProviderCheckpointMetadata
            {
                Provider = ProviderCode,
                TenantId = intent.TenantId,
                OrderId = intent.OrderId,
                PaymentIntentId = intent.Id,
                State = state,
                CheckpointedAtUtc = DateTime.SpecifyKind(checkpointedAtUtc, DateTimeKind.Utc),
                ReferenceCode = Normalize(referenceCode),
                ExpiresAtUtc = expiresAtUtc.HasValue
                    ? RequireUtcExpiration(expiresAtUtc.Value)
                    : null,
                PaymentUrl = Normalize(paymentUrl)
            },
            JsonOptions);
    }

    private static void EnsureOwner(PaymentIntent intent)
    {
        if (intent.Id == Guid.Empty ||
            intent.TenantId == Guid.Empty ||
            intent.OrderId == Guid.Empty ||
            intent.Provider != PaymentProvider.Fawry ||
            intent.Status != PaymentIntentStatus.Pending ||
            intent.IsDeleted)
        {
            throw InvalidCheckpoint("The durable Fawry checkpoint owner is invalid.");
        }
    }

    private static bool HasNoProjectedProviderArtifacts(PaymentIntent intent)
    {
        return string.IsNullOrWhiteSpace(intent.ReferenceCode) &&
               string.IsNullOrWhiteSpace(intent.ProviderIntentId) &&
               !intent.ExpiresAtUtc.HasValue &&
               string.IsNullOrWhiteSpace(intent.RedirectUrl);
    }

    private static bool HasCompleteProjectedProviderArtifacts(PaymentIntent intent)
    {
        return !string.IsNullOrWhiteSpace(intent.ReferenceCode) &&
               string.Equals(intent.ReferenceCode, intent.ProviderIntentId, StringComparison.Ordinal) &&
               intent.ExpiresAtUtc.HasValue;
    }

    private static bool HasAcceptedMetadata(FawryProviderCheckpointMetadata metadata)
    {
        return !string.IsNullOrWhiteSpace(metadata.ReferenceCode) ||
               metadata.ExpiresAtUtc.HasValue ||
               !string.IsNullOrWhiteSpace(metadata.PaymentUrl);
    }

    private static string RequireReference(string? referenceCode)
    {
        return !string.IsNullOrWhiteSpace(referenceCode)
            ? referenceCode.Trim()
            : throw InvalidCheckpoint("The Fawry provider reference is missing.");
    }

    private static DateTime RequireUtcExpiration(DateTime expiresAtUtc)
    {
        if (expiresAtUtc == default)
        {
            throw InvalidCheckpoint("The Fawry provider-reference expiration is missing.");
        }

        var utc = expiresAtUtc.Kind switch
        {
            DateTimeKind.Utc => expiresAtUtc,
            DateTimeKind.Local => expiresAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)
        };

        // PostgreSQL timestamptz stores microsecond precision. Normalize the value
        // before writing it both to JSON metadata and the projected timestamp so a
        // reload cannot make a completed checkpoint appear divergent.
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static FawryProviderCheckpointException InvalidCheckpoint(
        string message,
        Exception? innerException = null)
    {
        return new FawryProviderCheckpointException(
            "FAWRY_PROVIDER_CHECKPOINT_INVALID",
            message,
            innerException);
    }

    private sealed class FawryProviderCheckpointMetadata
    {
        public string Provider { get; set; } = string.Empty;
        public Guid TenantId { get; set; }
        public Guid OrderId { get; set; }
        public Guid PaymentIntentId { get; set; }
        public string State { get; set; } = string.Empty;
        public DateTime CheckpointedAtUtc { get; set; }
        public string? ReferenceCode { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string? PaymentUrl { get; set; }
    }
}

public enum FawryProviderCheckpointState
{
    NotStarted = 0,
    RegistrationStarted = 1,
    ReferenceAccepted = 2,
    Completed = 3
}

public sealed record FawryProviderCheckpoint(
    FawryProviderCheckpointState State,
    string? ReferenceCode,
    DateTime? ExpiresAtUtc,
    string? PaymentUrl)
{
    public static FawryProviderCheckpoint NotStarted { get; } = new(
        FawryProviderCheckpointState.NotStarted,
        null,
        null,
        null);
}

public sealed class FawryProviderCheckpointException : InvalidOperationException
{
    public string Code { get; }

    public FawryProviderCheckpointException(
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
