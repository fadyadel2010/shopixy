using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services;

public interface IFinancialMutationIdempotencyService
{
    Task<(SalesRequestIdempotency? Record, IActionResult? ShortCircuitResult)> TryClaimAsync(
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken = default);

    Task<IActionResult> CompleteAsync(
        IDbContextTransaction transaction,
        SalesRequestIdempotency record,
        IActionResult result,
        Func<IActionResult, (int StatusCode, string? ResponseJson)> snapshotFactory,
        CancellationToken cancellationToken = default);
}
