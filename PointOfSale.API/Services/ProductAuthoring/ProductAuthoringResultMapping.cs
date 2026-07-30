using PointOfSale.Core.Application.ProductAuthoring;

namespace PointOfSale.API.Services.ProductAuthoring;

public static class ProductAuthoringResultMapping
{
    public static ProductAuthoringResult ToResult(
        this ProductAuthoringCommand command,
        ProductAuthoringStatus status,
        IReadOnlyList<ProductAuthoringValidationError> validationErrors)
    {
        return new ProductAuthoringResult(
            status,
            ProductId: command.Identity.ProductId,
            MutationVersion: null,
            PublicationState: command.Publication.PublishStatus,
            ValidationErrors: validationErrors,
            CorrelationId: command.Metadata.CorrelationId,
            Metadata: new Dictionary<string, string>
            {
                ["recoveryPhase"] = "RECOVERY-1C",
                ["runtimeBehavior"] = "unchanged"
            });
    }
}
