using PointOfSale.Core.Application.ProductAuthoring;

namespace PointOfSale.Core.Interfaces.ProductAuthoring;

public interface IProductAuthoringOrchestrator
{
    Task<ProductAuthoringResult> AuthorAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken = default);
}
