using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

public interface IProductMediaService
{
    Task<IReadOnlyList<ProductMedia>> GetMediaAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<ProductMedia> AddMediaAsync(Guid productId, CreateProductMediaModel model, CancellationToken cancellationToken = default);
    Task RemoveMediaAsync(Guid productId, Guid mediaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductMedia>> ReorderMediaAsync(Guid productId, IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default);
}

public sealed record CreateProductMediaModel(string Url, string MediaType, string? Provider);

public class ProductMediaService : IProductMediaService
{
    private static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "video"
    };

    private static readonly HashSet<string> AllowedVideoProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube",
        "vimeo",
        "mp4"
    };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProductMediaService> _logger;

    public ProductMediaService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ILogger<ProductMediaService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductMedia>> GetMediaAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        await EnsureProductExistsAsync(productId, cancellationToken);

        return await _context.ProductMedia
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.Position)
            .ThenBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductMedia> AddMediaAsync(Guid productId, CreateProductMediaModel model, CancellationToken cancellationToken = default)
    {
        ValidateMedia(model);
        var product = await GetProductForMediaMutationAsync(productId, cancellationToken);

        var existingMedia = await _context.ProductMedia
            .Where(m => m.TenantId == _tenantContext.RequiredTenantId && m.ProductId == productId)
            .OrderBy(m => m.Position)
            .ThenBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var nextPosition = existingMedia.Count == 0
            ? 0
            : existingMedia.Max(m => m.Position);

        var normalizedMediaType = model.MediaType.Trim().ToLowerInvariant();
        var normalizedProvider = NormalizeProvider(model.Provider, normalizedMediaType, model.Url);
        var changedAtUtc = DateTime.UtcNow;

        var media = new ProductMedia
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.RequiredTenantId,
            ProductId = productId,
            MediaType = normalizedMediaType,
            Url = model.Url.Trim(),
            Provider = normalizedProvider,
            Position = nextPosition + 1,
            CreatedAtUtc = changedAtUtc
        };

        _context.ProductMedia.Add(media);
        TouchProductForMediaProjection(product, changedAtUtc);
        await PublishMediaChangedEventAsync(
            product,
            existingMedia.Concat(new[] { media }),
            changedAtUtc,
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Product media added | ProductId: {ProductId} | MediaId: {MediaId} | MediaType: {MediaType} | Position: {Position}",
            productId,
            media.Id,
            media.MediaType,
            media.Position);

        return media;
    }

    public async Task RemoveMediaAsync(Guid productId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var product = await GetProductForMediaMutationAsync(productId, cancellationToken);

        var productMedia = await _context.ProductMedia
            .Where(m => m.TenantId == _tenantContext.RequiredTenantId && m.ProductId == productId)
            .OrderBy(m => m.Position)
            .ThenBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var media = productMedia.FirstOrDefault(m => m.Id == mediaId);

        if (media == null)
        {
            throw new KeyNotFoundException("Product media not found.");
        }

        var changedAtUtc = DateTime.UtcNow;
        var affectedVariants = await _context.ProductVariants
            .Where(variant =>
                variant.TenantId == _tenantContext.RequiredTenantId &&
                variant.ProductId == productId &&
                variant.MediaId == mediaId)
            .ToListAsync(cancellationToken);

        foreach (var variant in affectedVariants)
        {
            variant.MediaId = null;
            variant.ImageUrl = null;
            variant.UpdatedAt = changedAtUtc;
        }

        _context.ProductMedia.Remove(media);
        var remainingMedia = productMedia
            .Where(m => m.Id != mediaId)
            .ToList();
        ResequenceMedia(remainingMedia);

        TouchProductForMediaProjection(product, changedAtUtc);
        await PublishMediaChangedEventAsync(product, remainingMedia, changedAtUtc, cancellationToken);
        foreach (var variant in affectedVariants)
        {
            await _eventPublisher.PublishAsync(new ProductVariantUpdatedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                ProductId = productId,
                VariantId = variant.Id,
                ChangedFields = ["MediaId", "ImageUrl"],
                MediaId = null,
                ImageUrl = null,
                SKU = variant.SKU,
                Barcode = variant.Barcode,
                Price = variant.Price,
                OnlinePrice = variant.OnlinePrice,
                CostPrice = variant.CostPrice,
                IsActive = variant.IsActive,
                SortOrder = variant.SortOrder
            }, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Product media removed | ProductId: {ProductId} | MediaId: {MediaId}",
            productId,
            mediaId);
    }

    public async Task<IReadOnlyList<ProductMedia>> ReorderMediaAsync(Guid productId, IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        var product = await GetProductForMediaMutationAsync(productId, cancellationToken);

        if (mediaIds == null || mediaIds.Count == 0)
        {
            throw new ArgumentException("mediaIds is required.", nameof(mediaIds));
        }

        var productMedia = await _context.ProductMedia
            .Where(m => m.TenantId == _tenantContext.RequiredTenantId && m.ProductId == productId)
            .OrderBy(m => m.Position)
            .ToListAsync(cancellationToken);

        if (productMedia.Count != mediaIds.Count)
        {
            throw new InvalidOperationException("Reorder request must include every media item exactly once.");
        }

        var existingIds = productMedia.Select(m => m.Id).OrderBy(id => id).ToList();
        var requestedIds = mediaIds.OrderBy(id => id).ToList();

        if (!existingIds.SequenceEqual(requestedIds))
        {
            throw new InvalidOperationException("Reorder request contains invalid media ids.");
        }

        var mediaLookup = productMedia.ToDictionary(m => m.Id);
        for (var index = 0; index < mediaIds.Count; index++)
        {
            mediaLookup[mediaIds[index]].Position = index + 1;
        }

        var changedAtUtc = DateTime.UtcNow;
        TouchProductForMediaProjection(product, changedAtUtc);
        await PublishMediaChangedEventAsync(product, productMedia, changedAtUtc, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return productMedia
            .OrderBy(m => m.Position)
            .ToList();
    }

    private async Task<Product> GetProductForMediaMutationAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p =>
                p.Id == productId &&
                p.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (product == null)
        {
            throw new KeyNotFoundException("Product not found.");
        }

        return product;
    }

    private async Task EnsureProductExistsAsync(Guid productId, CancellationToken cancellationToken)
    {
        var exists = await _context.Products
            .AnyAsync(p =>
                p.Id == productId &&
                p.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (!exists)
        {
            throw new KeyNotFoundException("Product not found.");
        }
    }

    private static void ResequenceMedia(IReadOnlyList<ProductMedia> remainingMedia)
    {
        for (var index = 0; index < remainingMedia.Count; index++)
        {
            remainingMedia[index].Position = index + 1;
        }
    }

    private async Task PublishMediaChangedEventAsync(
        Product product,
        IEnumerable<ProductMedia> media,
        DateTime changedAtUtc,
        CancellationToken cancellationToken)
    {
        var projectedMedia = media
            .OrderBy(m => m.Position)
            .ThenBy(m => m.CreatedAtUtc)
            .Select(mediaItem => new ProductMediaItemData
            {
                Id = mediaItem.Id,
                MediaType = mediaItem.MediaType,
                Url = mediaItem.Url,
                Provider = mediaItem.Provider,
                Position = mediaItem.Position
            })
            .ToList();

        await _eventPublisher.PublishAsync(new ProductMediaChangedEvent
        {
            TenantId = product.TenantId,
            ProductId = product.Id,
            Media = projectedMedia,
            OccurredAtUtc = changedAtUtc,
            EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion + 1)
        }, cancellationToken);
    }

    private static void TouchProductForMediaProjection(Product product, DateTime changedAtUtc)
    {
        product.UpdatedAt = changedAtUtc;
    }

    private static void ValidateMedia(CreateProductMediaModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Url))
        {
            throw new ArgumentException("url is required.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(model.MediaType))
        {
            throw new ArgumentException("mediaType is required.", nameof(model));
        }

        var mediaType = model.MediaType.Trim().ToLowerInvariant();
        if (!AllowedMediaTypes.Contains(mediaType))
        {
            throw new ArgumentException("mediaType must be image or video.", nameof(model));
        }

        var provider = NormalizeProvider(model.Provider, mediaType, model.Url);

        if (mediaType == "image")
        {
            var extension = GetUrlExtension(model.Url);
            if (!AllowedImageExtensions.Contains(extension))
            {
                throw new ArgumentException("Only JPG, PNG, and WEBP image URLs are allowed.", nameof(model));
            }

            if (!string.IsNullOrWhiteSpace(provider))
            {
                throw new ArgumentException("provider must be empty for image media.", nameof(model));
            }
        }

        if (mediaType == "video")
        {
            if (string.IsNullOrWhiteSpace(provider) || !AllowedVideoProviders.Contains(provider))
            {
                throw new ArgumentException("provider must be youtube, vimeo, or mp4 for video media.", nameof(model));
            }

            ValidateVideoUrl(provider, model.Url);
        }

        if (!IsAbsoluteOrRootRelativeUrl(model.Url))
        {
            throw new ArgumentException("url must be an absolute URL or root-relative path.", nameof(model));
        }
    }

    private static string? NormalizeProvider(string? provider, string mediaType, string url)
    {
        if (mediaType != "video")
        {
            return string.IsNullOrWhiteSpace(provider) ? null : provider.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            return provider.Trim().ToLowerInvariant();
        }

        var extension = GetUrlExtension(url);
        if (string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return "mp4";
        }

        return null;
    }

    private static void ValidateVideoUrl(string provider, string url)
    {
        if (provider == "mp4")
        {
            var extension = GetUrlExtension(url);
            if (!string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("MP4 video URLs must end with .mp4.", nameof(url));
            }

            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Hosted video URLs must be absolute.", nameof(url));
        }

        var host = uri.Host.ToLowerInvariant();
        if (provider == "youtube" && !(host.Contains("youtube.com") || host.Contains("youtu.be")))
        {
            throw new ArgumentException("youtube provider requires a youtube URL.", nameof(url));
        }

        if (provider == "vimeo" && !host.Contains("vimeo.com"))
        {
            throw new ArgumentException("vimeo provider requires a vimeo URL.", nameof(url));
        }
    }

    private static bool IsAbsoluteOrRootRelativeUrl(string url)
    {
        return url.StartsWith("/", StringComparison.Ordinal) ||
               Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetUrlExtension(string url)
    {
        var cleanUrl = url.Split('?', '#')[0];
        return Path.GetExtension(cleanUrl).ToLowerInvariant();
    }
}
