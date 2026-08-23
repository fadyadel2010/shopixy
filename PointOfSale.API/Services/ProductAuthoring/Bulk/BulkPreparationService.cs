using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Bulk;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

public sealed class BulkPreparationService : IBulkPreparationService
{
    private readonly IBulkProductRequestValidator _validator;
    private readonly IBulkProductLoader _loader;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    public BulkPreparationService(
        IBulkProductRequestValidator validator,
        IBulkProductLoader loader,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _validator = validator;
        _loader = loader;
        _correlationIdAccessor = correlationIdAccessor;
    }

    public async Task<BulkPreparationOutcome> PrepareAsync(
        BulkProductRequest? request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = _correlationIdAccessor.GetCorrelationId();
        var requested = request?.Items?.Count ?? 0;

        var validation = await _validator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            return new BulkPreparationOutcome(
                IsValid: false,
                CorrelationId: correlationId,
                Requested: requested,
                Accepted: 0,
                PreparedItems: Array.Empty<BulkPreparedItem>(),
                Errors: validation.Errors);
        }

        var ids = new HashSet<Guid>(validation.NormalizedItems.Select(item => item.ProductId));
        var projections = await _loader.LoadProjectionsAsync(ids, cancellationToken);

        var projectionLookup = projections
            .ToDictionary(projection => projection.Id, projection => projection);

        var preparedItems = new List<BulkPreparedItem>();
        foreach (var item in validation.NormalizedItems)
        {
            if (!projectionLookup.TryGetValue(item.ProductId, out var projection))
            {
                preparedItems.Add(BulkPreparedItem.NotFound(item.ProductId, item.ExpectedVersion));
            }
            else if (projection.RowVersion != item.ExpectedVersion)
            {
                preparedItems.Add(BulkPreparedItem.ConcurrencyConflict(
                    item.ProductId, item.ExpectedVersion, projection.RowVersion));
            }
            else
            {
                preparedItems.Add(BulkPreparedItem.Prepared(
                    item.ProductId,
                    item.ExpectedVersion,
                    projection.RowVersion,
                    projection.IsActive,
                    projection.PublishStatus,
                    projection.IsOnlineVisible));
            }
        }

        return new BulkPreparationOutcome(
            IsValid: true,
            CorrelationId: correlationId,
            Requested: requested,
            Accepted: validation.NormalizedItems.Count,
            PreparedItems: preparedItems,
            Errors: Array.Empty<BulkRequestValidationError>());
    }
}
