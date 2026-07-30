using PointOfSale.Core.Application.ProductAuthoring;

namespace PointOfSale.Core.Interfaces.ProductAuthoring;

public interface IProductAuthoringValidator
{
    Task<IReadOnlyList<ProductAuthoringValidationError>> ValidateAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken = default);
}
