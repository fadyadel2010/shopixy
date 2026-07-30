using PointOfSale.Core.Application.ProductAuthoring;

namespace PointOfSale.Core.Interfaces.ProductAuthoring;

public interface IProductAuthoringReconstructionService
{
    Task<ProductAuthoringCommand> ReconstructForAdmissionAsync(
        ProductAuthoringCommand command,
        CancellationToken cancellationToken = default);

    Task<List<ProductAuthoringUnit>> LoadCurrentUnitsAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
