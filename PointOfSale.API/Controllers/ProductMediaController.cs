using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/products/{productId}/media")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class ProductMediaController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string ProductMediaAddOperation = "PRODUCT_MEDIA_ADD";
    private const string ProductMediaDeleteOperation = "PRODUCT_MEDIA_DELETE";
    private const string ProductMediaReorderOperation = "PRODUCT_MEDIA_REORDER";

    private readonly ApplicationDbContext _context;
    private readonly IProductMediaService _productMediaService;
    private readonly IProductMutationIdempotencyService _productMutationIdempotency;
    private readonly ITenantContext _tenantContext;

    public ProductMediaController(
        ApplicationDbContext context,
        IProductMediaService productMediaService,
        IProductMutationIdempotencyService productMutationIdempotency,
        ITenantContext tenantContext)
    {
        _context = context;
        _productMediaService = productMediaService;
        _productMutationIdempotency = productMutationIdempotency;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductMediaResponse>>> GetMedia(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            var media = await _productMediaService.GetMediaAsync(productId, cancellationToken);
            return Ok(media.Select(MapResponse).ToList());
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddMedia(
        Guid productId,
        [FromBody] CreateProductMediaRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyPrecondition = RequireProductMediaMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await TryClaimProductMediaMutationIdempotencyAsync(
            ProductMediaAddOperation,
            productId,
            new
            {
                productId,
                request
            },
            "Idempotency-Key was already used for a different product media add request.",
            "A product media add request with the same idempotency key is already being processed.",
            cancellationToken);
        if (claim.ShortCircuitResult is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return claim.ShortCircuitResult;
        }

        try
        {
            var media = await _productMediaService.AddMediaAsync(
                productId,
                new CreateProductMediaModel(request.Url, request.MediaType, request.Provider),
                cancellationToken);

            var response = MapResponse(media);
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                CreatedAtAction(nameof(GetMedia), new { productId }, response),
                productId,
                cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                NotFound(new { error = ex.Message }),
                productId,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                BadRequest(new { error = ex.Message }),
                productId,
                cancellationToken);
        }
    }

    [HttpDelete("{mediaId}")]
    public async Task<IActionResult> RemoveMedia(Guid productId, Guid mediaId, CancellationToken cancellationToken)
    {
        var idempotencyPrecondition = RequireProductMediaMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await TryClaimProductMediaMutationIdempotencyAsync(
            ProductMediaDeleteOperation,
            productId,
            new
            {
                productId,
                mediaId
            },
            "Idempotency-Key was already used for a different product media delete request.",
            "A product media delete request with the same idempotency key is already being processed.",
            cancellationToken);
        if (claim.ShortCircuitResult is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return claim.ShortCircuitResult;
        }

        try
        {
            await _productMediaService.RemoveMediaAsync(productId, mediaId, cancellationToken);
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                NoContent(),
                productId,
                cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                NotFound(new { error = ex.Message }),
                productId,
                cancellationToken);
        }
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> ReorderMedia(
        Guid productId,
        [FromBody] ReorderProductMediaRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyPrecondition = RequireProductMediaMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var claim = await TryClaimProductMediaMutationIdempotencyAsync(
            ProductMediaReorderOperation,
            productId,
            new
            {
                productId,
                request
            },
            "Idempotency-Key was already used for a different product media reorder request.",
            "A product media reorder request with the same idempotency key is already being processed.",
            cancellationToken);
        if (claim.ShortCircuitResult is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return claim.ShortCircuitResult;
        }

        try
        {
            var reordered = await _productMediaService.ReorderMediaAsync(productId, request.MediaIds, cancellationToken);
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                Ok(reordered.Select(MapResponse).ToList()),
                productId,
                cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                NotFound(new { error = ex.Message }),
                productId,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                BadRequest(new { error = ex.Message }),
                productId,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return await CompleteProductMediaMutationIdempotencyAsync(
                transaction,
                claim.Record,
                BadRequest(new { error = ex.Message }),
                productId,
                cancellationToken);
        }
    }

    private ActionResult? RequireProductMediaMutationIdempotencyKey()
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        var requestId = Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            !string.IsNullOrWhiteSpace(requestId) &&
            !string.Equals(idempotencyKey, requestId, StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} and {RequestIdHeader} must match when both are provided."
            });
        }

        var normalizedIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey)
            ? idempotencyKey
            : requestId;

        if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required for product media mutation requests."
            });
        }

        if (normalizedIdempotencyKey.Length > 200)
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must not exceed 200 characters."
            });
        }

        return null;
    }

    private string ReadProductMediaMutationIdempotencyKey() =>
        Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim()
        ?? Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim()
        ?? string.Empty;

    private async Task<(ProductRequestIdempotency? Record, IActionResult? ShortCircuitResult)>
        TryClaimProductMediaMutationIdempotencyAsync(
            string operation,
            Guid productId,
            object fingerprintPayload,
            string conflictMessage,
            string inProgressMessage,
            CancellationToken cancellationToken)
    {
        var claim = await _productMutationIdempotency.TryClaimAsync(
            new ProductMutationIdempotencyRequest(
                _tenantContext.RequiredTenantId,
                _productMutationIdempotency.ScopeKey("product-media", ReadProductMediaMutationIdempotencyKey()),
                operation,
                productId,
                null,
                DeterministicRequestHasher.ComputeHash(fingerprintPayload)),
            cancellationToken);

        return claim.Replay is null
            ? (claim.Record, null)
            : (null, BuildProductMediaMutationReplayResponse(claim.Replay, conflictMessage, inProgressMessage));
    }

    private async Task<IActionResult> CompleteProductMediaMutationIdempotencyAsync(
        IDbContextTransaction transaction,
        ProductRequestIdempotency? record,
        IActionResult result,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var snapshot = SnapshotProductMediaActionResult(result);
        _productMutationIdempotency.Complete(
            record,
            snapshot.StatusCode,
            snapshot.ResponsePayload,
            productId);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    private static IActionResult BuildProductMediaMutationReplayResponse(
        ProductMutationIdempotencyReplay replay,
        string conflictMessage,
        string inProgressMessage) =>
        replay.Kind switch
        {
            ProductMutationIdempotencyReplayKind.Completed when replay.Record is not null =>
                BuildStoredProductMediaMutationResponse(replay.Record),
            ProductMutationIdempotencyReplayKind.Conflict => new ConflictObjectResult(new
            {
                message = conflictMessage
            }),
            _ => new ConflictObjectResult(new
            {
                message = inProgressMessage
            })
        };

    private static IActionResult BuildStoredProductMediaMutationResponse(ProductRequestIdempotency record)
    {
        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return new StatusCodeResult(statusCode);
        }

        using var document = System.Text.Json.JsonDocument.Parse(record.ResponseJson);
        var payload = document.RootElement.Clone();
        return new ObjectResult(payload)
        {
            StatusCode = statusCode
        };
    }

    private static ProductMediaActionSnapshot SnapshotProductMediaActionResult(IActionResult result) =>
        result switch
        {
            ObjectResult objectResult => new ProductMediaActionSnapshot(
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                objectResult.Value),
            StatusCodeResult statusCodeResult => new ProductMediaActionSnapshot(
                statusCodeResult.StatusCode,
                null),
            _ => new ProductMediaActionSnapshot(StatusCodes.Status200OK, null)
        };

    private static ProductMediaResponse MapResponse(ProductMedia media)
    {
        return new ProductMediaResponse
        {
            Id = media.Id,
            MediaType = media.MediaType,
            Url = media.Url,
            Provider = media.Provider,
            Position = media.Position,
            CreatedAtUtc = media.CreatedAtUtc
        };
    }

    public class CreateProductMediaRequest
    {
        public string Url { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string? Provider { get; set; }
    }

    public class ReorderProductMediaRequest
    {
        public List<Guid> MediaIds { get; set; } = new();
    }

    public class ProductMediaResponse
    {
        public Guid Id { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? Provider { get; set; }
        public int Position { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    private sealed record ProductMediaActionSnapshot(int StatusCode, object? ResponsePayload);
}
