using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Application.ProductAuthoring;

public sealed record ProductAuthoringResult(
    ProductAuthoringStatus Status,
    Guid? ProductId,
    long? MutationVersion,
    PublishStatus? PublicationState,
    IReadOnlyList<ProductAuthoringValidationError> ValidationErrors,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ProductAuthoringResult Accepted(
        Guid? productId,
        PublishStatus publicationState,
        string correlationId,
        long? mutationVersion = null) =>
        new(
            ProductAuthoringStatus.Accepted,
            ProductId: productId,
            MutationVersion: mutationVersion,
            PublicationState: publicationState,
            ValidationErrors: Array.Empty<ProductAuthoringValidationError>(),
            CorrelationId: correlationId,
            Metadata: new Dictionary<string, string>
            {
                ["recoveryPhase"] = "RECOVERY-1D",
                ["runtimeBehavior"] = "atomic-sql-authoring"
            });

    public static ProductAuthoringResult InfrastructurePrepared(string correlationId) =>
        new(
            ProductAuthoringStatus.InfrastructurePrepared,
            ProductId: null,
            MutationVersion: null,
            PublicationState: null,
            ValidationErrors: Array.Empty<ProductAuthoringValidationError>(),
            CorrelationId: correlationId,
            Metadata: new Dictionary<string, string>
            {
                ["recoveryPhase"] = "RECOVERY-1C",
                ["runtimeBehavior"] = "unchanged"
            });
}

public enum ProductAuthoringStatus
{
    InfrastructurePrepared = 0,
    Accepted = 1,
    ValidationFailed = 2,
    NotReadyForPublication = 3,
    RejectedByProductTypePolicy = 4,
    Failed = 5,
    NotFound = 6,
    ConcurrencyConflict = 7,
    Conflict = 8
}

public sealed record ProductAuthoringValidationError(
    string Code,
    string Field,
    string Message);
