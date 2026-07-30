using PointOfSale.API.Controllers;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.ProductAuthoring;

public interface IProductAuthoringCommandMapper
{
    ProductAuthoringCommand FromCreateRequest(
        ProductAuthoringOperation operation,
        Guid? productId,
        CreateProductRequest request,
        IReadOnlyList<ProductAuthoringVariantRequest>? variants);

    ProductAuthoringCommand FromUpdateRequest(
        ProductAuthoringOperation operation,
        Guid? productId,
        UpdateProductRequest request,
        IReadOnlyList<ProductAuthoringVariantRequest>? variants);

    ProductAuthoringCommand FromLifecycleProduct(
        ProductAuthoringOperation operation,
        Product product);

    ProductAuthoringCommand FromProductUnitsUpdate(
        Product product,
        List<ProductAuthoringUnitRequest> productUnits);
}
