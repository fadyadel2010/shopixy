using PointOfSale.Core.Application.ProductAuthoring;

namespace PointOfSale.Core.Interfaces.ProductAuthoring;

public interface IProductAuthoringPolicy
{
    Task<ProductAuthoringPolicyDecision> EvaluateAdmissionAsync(
        ProductAuthoringCommand command,
        ProductAuthoringAdmissionFacts facts,
        CancellationToken cancellationToken = default);

    Task<ProductAuthoringPolicyDecision> EvaluateSimpleProductIdentityAsync(
        ProductAuthoringCommand command,
        ProductAuthoringSimpleProductIdentityFacts facts,
        CancellationToken cancellationToken = default);

    Task<ProductAuthoringPolicyDecision> EvaluateUnitReferencesAsync(
        ProductAuthoringCommand command,
        ProductAuthoringUnitReferenceFacts facts,
        CancellationToken cancellationToken = default);

    Task<ProductAuthoringPolicyDecision> EvaluateLifecycleAsync(
        ProductAuthoringCommand command,
        ProductAuthoringLifecycleFacts facts,
        CancellationToken cancellationToken = default);
}

public sealed record ProductAuthoringPolicyDecision(
    ProductAuthoringStatus Status,
    IReadOnlyList<ProductAuthoringValidationError> Errors)
{
    public bool IsAllowed => Status == ProductAuthoringStatus.Accepted;

    public static ProductAuthoringPolicyDecision Allowed() =>
        new(ProductAuthoringStatus.Accepted, Array.Empty<ProductAuthoringValidationError>());
}

public sealed record ProductAuthoringAdmissionFacts(bool CategoryIsLive);

public sealed record ProductAuthoringSimpleProductIdentityFacts(bool BarcodeIsOwnedByAnotherSemanticEntity);

public sealed record ProductAuthoringUnitReferenceFacts(bool AllAuthoredUnitsReferenceActiveTenantUnits);

public sealed record ProductAuthoringLifecycleFacts(bool ProductExists);
