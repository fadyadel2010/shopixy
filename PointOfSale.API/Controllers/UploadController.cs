using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Authorize]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const long MaxVideoBytes = 100 * 1024 * 1024;

    private readonly IR2StorageService _r2StorageService;
    private readonly IFileValidationService _fileValidationService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<UploadController> _logger;
    private readonly ApplicationDbContext _context;

    public UploadController(
        IR2StorageService r2StorageService,
        IFileValidationService fileValidationService,
        ITenantContext tenantContext,
        ILogger<UploadController> logger,
        ApplicationDbContext context)
    {
        _r2StorageService = r2StorageService;
        _fileValidationService = fileValidationService;
        _tenantContext = tenantContext;
        _logger = logger;
        _context = context;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxVideoBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxVideoBytes)]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        FileValidationResult validationResult;
        await using (var validationStream = request.File.OpenReadStream())
        {
            validationResult = await _fileValidationService.ValidateFileAsync(
                validationStream,
                request.File.FileName,
                request.File.ContentType,
                FileValidationProfiles.ProductMedia,
                cancellationToken);
        }

        if (!validationResult.IsValid || string.IsNullOrWhiteSpace(validationResult.CanonicalContentType))
        {
            return BadRequest(new { error = "Unsupported or invalid file type." });
        }

        var normalizedContentType = validationResult.CanonicalContentType;
        var maxBytes = normalizedContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            ? MaxVideoBytes
            : MaxImageBytes;

        if (request.File.Length > maxBytes)
        {
            return BadRequest(new
            {
                error = normalizedContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                    ? "Video size exceeds 100MB limit."
                    : "Image size exceeds 10MB limit."
            });
        }

        if (!TryNormalizeEntityType(request.EntityType, out var entityType))
        {
            return BadRequest(new { error = "entityType must be one of: product, variant, general." });
        }

        var normalizedEntityId = SanitizePathSegment(request.EntityId);

        if ((entityType == "product" || entityType == "variant") && string.IsNullOrWhiteSpace(normalizedEntityId))
        {
            return BadRequest(new { error = "entityId is required for product and variant uploads." });
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var objectPath = BuildObjectPath(tenantId, entityType, normalizedEntityId);

        try
        {
            await using var stream = request.File.OpenReadStream();
            var result = await _r2StorageService.UploadFileAsync(
                stream,
                normalizedContentType,
                objectPath,
                cancellationToken);

            return Ok(new UploadResponse
            {
                Url = result.Url ?? (entityType == "product" && Guid.TryParse(normalizedEntityId, out var productId)
                    ? $"/api/upload/product-media/{productId:D}/{result.FileName}"
                    : null),
                FileName = result.FileName,
                ContentType = normalizedContentType,
                Size = request.File.Length
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "R2 upload rejected due to configuration | TenantId: {TenantId}", tenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred" });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "R2 upload failed | TenantId: {TenantId} | EntityType: {EntityType} | EntityId: {EntityId}",
                tenantId,
                entityType,
                request.EntityId);

            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Upload failed." });
        }
    }

    // When object storage has no public URL (including the development storage),
    // serve only registered media belonging to a product in the current tenant.
    // The bytes still come from the canonical IR2StorageService implementation.
    [AllowAnonymous]
    [HttpGet("product-media/{productId:guid}/{fileName}")]
    public async Task<IActionResult> GetProductMediaContent(
        Guid productId, string fileName, CancellationToken cancellationToken)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(fileName, "^[0-9a-fA-F]{32}\\.(jpg|jpeg|png|webp|mp4)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return NotFound();
        }

        var url = $"/api/upload/product-media/{productId:D}/{fileName}";
        var exists = await _context.ProductMedia.AnyAsync(media =>
            media.TenantId == _tenantContext.RequiredTenantId &&
            media.ProductId == productId && media.Url == url &&
            media.Product.TenantId == _tenantContext.RequiredTenantId && !media.Product.IsDeleted,
            cancellationToken);
        if (!exists)
        {
            return NotFound();
        }

        var objectKey = $"tenants/{_tenantContext.RequiredTenantId:D}/products/{productId:D}/{fileName}";
        try
        {
            var result = await _r2StorageService.DownloadObjectAsync(objectKey, null, cancellationToken);
            return File(result.Content, result.ContentType);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }

    private static bool TryNormalizeEntityType(string? entityType, out string normalized)
    {
        normalized = (entityType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "product" or "variant" or "general";
    }

    private static string NormalizeContentType(string? contentType)
    {
        return (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private static string BuildObjectPath(Guid tenantId, string entityType, string? entityId)
    {
        var tenantSegment = tenantId.ToString("D");
        var entitySegment = entityId ?? string.Empty;

        return entityType switch
        {
            "product" => $"tenants/{tenantSegment}/products/{entitySegment}",
            "variant" => $"tenants/{tenantSegment}/variants/{entitySegment}",
            _ => string.IsNullOrWhiteSpace(entitySegment)
                ? $"tenants/{tenantSegment}/general"
                : $"tenants/{tenantSegment}/general/{entitySegment}"
        };
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Where(ch => !invalidChars.Contains(ch) && ch != '/' && ch != '\\')
            .ToArray());

        return sanitized.Replace("..", string.Empty, StringComparison.Ordinal);
    }

    public class UploadRequest
    {
        [Required]
        public IFormFile? File { get; set; }

        [Required]
        [StringLength(20)]
        public string? EntityType { get; set; }

        [StringLength(128)]
        public string? EntityId { get; set; }
    }

    public class UploadResponse
    {
        public string? Url { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
