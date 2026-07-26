using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/products/{productId}/media")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class ProductMediaController : ControllerBase
{
    private readonly IProductMediaService _productMediaService;

    public ProductMediaController(IProductMediaService productMediaService)
    {
        _productMediaService = productMediaService;
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
    public async Task<ActionResult<ProductMediaResponse>> AddMedia(
        Guid productId,
        [FromBody] CreateProductMediaRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var media = await _productMediaService.AddMediaAsync(
                productId,
                new CreateProductMediaModel(request.Url, request.MediaType, request.Provider),
                cancellationToken);

            return CreatedAtAction(nameof(GetMedia), new { productId }, MapResponse(media));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{mediaId}")]
    public async Task<IActionResult> RemoveMedia(Guid productId, Guid mediaId, CancellationToken cancellationToken)
    {
        try
        {
            await _productMediaService.RemoveMediaAsync(productId, mediaId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPut("reorder")]
    public async Task<ActionResult<IReadOnlyList<ProductMediaResponse>>> ReorderMedia(
        Guid productId,
        [FromBody] ReorderProductMediaRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var reordered = await _productMediaService.ReorderMediaAsync(productId, request.MediaIds, cancellationToken);
            return Ok(reordered.Select(MapResponse).ToList());
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

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
}
