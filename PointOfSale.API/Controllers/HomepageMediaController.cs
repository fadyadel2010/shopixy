using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
public sealed class HomepageMediaController : ControllerBase
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IR2StorageService _storage;
    private readonly IFileValidationService _validation;

    public HomepageMediaController(ApplicationDbContext db, ITenantContext tenant, IR2StorageService storage, IFileValidationService validation)
    {
        _db = db;
        _tenant = tenant;
        _storage = storage;
        _validation = validation;
    }

    [Authorize(Roles = "Owner,Admin")]
    [HttpPost("api/admin/homepage-media")]
    [RequestSizeLimit(MaxBytes + 65536)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxBytes)
            return BadRequest(new { code = "INVALID_HOMEPAGE_IMAGE", message = "Select an image of 10 MB or less." });
        await using var stream = file.OpenReadStream();
        var validation = await _validation.ValidateFileAsync(stream, file.FileName, file.ContentType, FileValidationProfiles.Images, cancellationToken);
        if (!validation.IsValid || validation.CanonicalContentType == null)
            return BadRequest(new { code = "INVALID_HOMEPAGE_IMAGE", message = "Select a valid JPEG, PNG, WebP or GIF image." });
        stream.Position = 0;
        var tenantId = _tenant.RequiredTenantId;
        var id = Guid.NewGuid();
        var uploaded = await _storage.UploadFileAsync(stream, validation.CanonicalContentType,
            $"tenants/{tenantId:D}/homepage/{id:D}", cancellationToken);
        try
        {
            _db.HomepageMediaAssets.Add(new HomepageMediaAsset
            {
                Id = id, TenantId = tenantId, ObjectKey = uploaded.ObjectKey,
                ContentType = validation.CanonicalContentType, Size = file.Length, CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _storage.DeleteObjectAsync(uploaded.ObjectKey, CancellationToken.None);
            throw;
        }
        // Never expose a bucket's public URL. The immutable reference has no authority by itself.
        return Ok(new { mediaId = id, reference = HomepageMediaReferences.Create(id) });
    }

    [Authorize(Roles = "Owner,Admin")]
    [HttpGet("api/admin/homepage-media/{id:guid}/preview")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var asset = await _db.HomepageMediaAssets.AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == id && asset.TenantId == _tenant.RequiredTenantId, cancellationToken);
        if (asset == null) return NotFound();
        var url = await _storage.CreateDownloadUrlAsync(asset.ObjectKey, TimeSpan.FromMinutes(5), cancellationToken);
        return Ok(new { url });
    }

    [AllowAnonymous]
    [HttpGet("api/storefront/homepage-media/{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Content(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.RequiredTenantId;
        var asset = await _db.HomepageMediaAssets.AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == id && asset.TenantId == tenantId, cancellationToken);
        if (asset == null) return NotFound();
        var authority = await _db.StoreConfigs.AsNoTracking().Where(config => config.TenantId == tenantId)
            .Join(_db.Tenants.AsNoTracking().Where(tenant => tenant.IsActive && tenant.Status == TenantStatus.Active && tenant.DeletedAtUtc == null),
                config => config.TenantId, tenant => tenant.Id, (config, tenant) => config)
            .SingleOrDefaultAsync(cancellationToken);
        if (authority == null || authority.StorefrontStatus != "live" || !HomepageMediaReferences.IsPublished(authority.CustomizationJson, id))
            return NotFound();
        try
        {
            var content = await _storage.DownloadObjectAsync(asset.ObjectKey, null, cancellationToken);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(content.Content, asset.ContentType);
        }
        catch (FileNotFoundException) { return NotFound(); }
    }
}
