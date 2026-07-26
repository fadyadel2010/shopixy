using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using System.Text.Json;

namespace PointOfSale.Infrastructure.Services;

public class ServiceCatalogService : IServiceCatalogService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ServiceCatalogService> _logger;

    public ServiceCatalogService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ILogger<ServiceCatalogService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<Service> CreateServiceAsync(Guid profileId, Guid userId, CreateServiceDto dto, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        var listing = ValidateListingIntent(
            dto.Title,
            dto.Description,
            dto.Category,
            dto.StartingPrice,
            dto.PricingType,
            dto.Currency,
            dto.ServiceImages,
            dto.Packages);

        var profile = await _context.Set<MarketplaceProfile>()
            .FirstOrDefaultAsync(p => p.Id == profileId && !p.IsDeleted, cancellationToken);

        if (profile == null)
            throw MarketplaceDomainException.ProfileNotFound();

        if (profile.UserId != userId)
            throw MarketplaceDomainException.Unauthorized("create services for this profile");

        var service = Service.Create(
            tenantId, 
            profileId, 
            listing.Title,
            listing.Description,
            listing.Category,
            dto.StartingPrice,
            dto.PricingType,
            listing.Currency,
            listing.ServiceImages,
            listing.Packages);
        
        _context.Set<Service>().Add(service);

        // EF OUTBOX PATTERN: Publish BEFORE SaveChangesAsync.
        // With UseBusOutbox(), _eventPublisher.PublishAsync() writes to the OutboxMessage table
        // via the scoped DbContext. The message is ONLY persisted when SaveChangesAsync() is called.
        // Both the Service entity AND the outbox message are committed atomically in one transaction.
        // If SaveChanges fails, both are rolled back (no ghost services, no lost events).
        await _eventPublisher.PublishAsync(new ServiceCreatedEvent
        {
            ServiceId = service.Id,
            TenantId = tenantId,
            ProfileId = profileId,
            Title = service.Title,
            Description = service.Description,
            Category = service.Category,
            IsActive = service.IsActive,
            CreatedAt = service.CreatedAt,
            // Phase FE-3
            StartingPrice = service.StartingPrice,
            PricingType = service.PricingType?.ToString(),
            Currency = service.Currency,
            ServiceImages = service.ServiceImages
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "📤 [EVENT-BUS] ServiceCreatedEvent published | ServiceId: {ServiceId} | TenantId: {TenantId} | Category: {Category}",
            service.Id, tenantId, dto.Category);

        _logger.LogInformation(
            "Service created | ServiceId: {ServiceId} | ProfileId: {ProfileId} | TenantId: {TenantId}",
            service.Id, profileId, tenantId);

        return service;
    }

    public async Task<Service> UpdateServiceAsync(Guid serviceId, Guid userId, UpdateServiceDto dto, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        var listing = ValidateListingIntent(
            dto.Title,
            dto.Description,
            dto.Category,
            dto.StartingPrice,
            dto.PricingType,
            dto.Currency,
            dto.ServiceImages,
            dto.Packages);

        var service = await _context.Set<Service>()
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);

        if (service == null)
            throw MarketplaceDomainException.ServiceNotFound();

        if (service.Profile?.UserId != userId)
            throw MarketplaceDomainException.Unauthorized("update this service");

        service.Update(
            listing.Title,
            listing.Description,
            listing.Category,
            dto.StartingPrice,
            dto.PricingType,
            listing.Currency,
            listing.ServiceImages,
            listing.Packages);

        // EF OUTBOX PATTERN: Publish BEFORE SaveChangesAsync for atomic commit
        await _eventPublisher.PublishAsync(new ServiceUpdatedEvent
        {
            ServiceId = service.Id,
            TenantId = tenantId,
            Title = service.Title,
            Description = service.Description,
            Category = service.Category,
            UpdatedAt = service.UpdatedAt ?? DateTime.UtcNow,
            // Phase FE-3
            StartingPrice = service.StartingPrice,
            PricingType = service.PricingType?.ToString(),
            Currency = service.Currency,
            ServiceImages = service.ServiceImages
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Service updated | ServiceId: {ServiceId} | TenantId: {TenantId}",
            service.Id, tenantId);

        return service;
    }

    public async Task<Service?> GetServiceByIdAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await (
                from service in _context.Set<Service>().IgnoreQueryFilters().Include(s => s.Profile)
                join tenant in _context.Tenants.IgnoreQueryFilters()
                    on service.TenantId equals tenant.Id
                join profile in _context.Set<MarketplaceProfile>().IgnoreQueryFilters()
                    on service.ProfileId equals profile.Id
                where service.Id == serviceId &&
                      !service.IsDeleted &&
                      service.IsActive &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null &&
                      !profile.IsDeleted &&
                      profile.IsActive &&
                      profile.TenantId == service.TenantId &&
                      !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                          governance.SubjectId == service.TenantId &&
                          governance.Role == UserGovernanceRole.Vendor &&
                          (governance.Status == UserGovernanceStatus.Suspended ||
                           governance.Status == UserGovernanceStatus.Banned))
                select service)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Service>> GetServicesByProfileIdAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Service>()
            .Where(s => s.ProfileId == profileId && !s.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Service>> GetActiveServicesAsync(string? category = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Set<Service>()
            .Include(s => s.Profile)
            .Where(s => s.IsActive && !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(s => s.Category == category);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task DeactivateServiceAsync(Guid serviceId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        var service = await _context.Set<Service>()
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);

        if (service == null)
            throw MarketplaceDomainException.ServiceNotFound();

        if (service.Profile?.UserId != userId)
            throw MarketplaceDomainException.Unauthorized("deactivate this service");

        service.Deactivate();

        // EF OUTBOX PATTERN: Publish BEFORE SaveChangesAsync for atomic commit
        await _eventPublisher.PublishAsync(new ServiceActivationChangedEvent
        {
            ServiceId = service.Id,
            TenantId = tenantId,
            IsActive = false,
            ChangedAt = service.UpdatedAt ?? DateTime.UtcNow
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Service deactivated | ServiceId: {ServiceId} | TenantId: {TenantId}",
            service.Id, tenantId);
    }

    public async Task ActivateServiceAsync(Guid serviceId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        var service = await _context.Set<Service>()
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);

        if (service == null)
            throw MarketplaceDomainException.ServiceNotFound();

        if (service.Profile?.UserId != userId)
            throw MarketplaceDomainException.Unauthorized("activate this service");

        service.Activate();

        // EF OUTBOX PATTERN: Publish BEFORE SaveChangesAsync for atomic commit
        await _eventPublisher.PublishAsync(new ServiceActivationChangedEvent
        {
            ServiceId = service.Id,
            TenantId = tenantId,
            IsActive = true,
            ChangedAt = service.UpdatedAt ?? DateTime.UtcNow
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Service activated | ServiceId: {ServiceId} | TenantId: {TenantId}",
            service.Id, tenantId);
    }

    private static ServiceListingIntent ValidateListingIntent(
        string title,
        string description,
        string? category,
        decimal? startingPrice,
        Core.Enums.PricingType? pricingType,
        string? currency,
        string? serviceImages,
        string? packages)
    {
        var normalizedTitle = NormalizeRequiredText(title, 160, "TITLE_INVALID");
        var normalizedDescription = NormalizeRequiredText(description, 5000, "DESCRIPTION_INVALID");
        var normalizedCategory = NormalizeOptionalText(category, 120, "CATEGORY_INVALID");
        var normalizedCurrency = NormalizeOptionalText(currency, 3, "CURRENCY_INVALID")?.ToUpperInvariant();
        var normalizedImages = NormalizeOptionalJsonArray(serviceImages, "SERVICE_IMAGES_INVALID", validateStorageKeys: true);
        var normalizedPackages = NormalizeOptionalJsonArray(packages, "SERVICE_PACKAGES_INVALID", validateStorageKeys: false);

        if (startingPrice is < 0)
        {
            throw new MarketplaceDomainException("PRICE_INVALID", "Service starting price cannot be negative.");
        }

        if (startingPrice.HasValue && pricingType == null)
        {
            throw new MarketplaceDomainException("PRICING_TYPE_REQUIRED", "Service pricing type is required when a starting price is provided.");
        }

        if (startingPrice.HasValue && string.IsNullOrWhiteSpace(normalizedCurrency))
        {
            throw new MarketplaceDomainException("CURRENCY_REQUIRED", "Service currency is required when a starting price is provided.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedCurrency) &&
            (normalizedCurrency.Length != 3 || normalizedCurrency.Any(ch => ch < 'A' || ch > 'Z')))
        {
            throw new MarketplaceDomainException("CURRENCY_INVALID", "Service currency must be a three-letter ISO code.");
        }

        return new ServiceListingIntent(
            normalizedTitle,
            normalizedDescription,
            normalizedCategory,
            normalizedCurrency,
            normalizedImages,
            normalizedPackages);
    }

    private static string NormalizeRequiredText(string value, int maxLength, string errorCode)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength || ContainsControlCharacter(normalized))
        {
            throw new MarketplaceDomainException(errorCode, "Service listing text is invalid.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength || ContainsControlCharacter(normalized))
        {
            throw new MarketplaceDomainException(errorCode, "Service listing text is invalid.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalJsonArray(string? value, string errorCode, bool validateStorageKeys)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 20_000)
        {
            throw new MarketplaceDomainException(errorCode, "Service listing JSON is too large.");
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new MarketplaceDomainException(errorCode, "Service listing JSON must be an array.");
            }

            if (validateStorageKeys)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || !IsSafeServiceMediaReference(item.GetString()))
                    {
                        throw new MarketplaceDomainException(errorCode, "Service media references must be tenant-owned storage keys.");
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            throw new MarketplaceDomainException(errorCode, $"Service listing JSON is invalid: {ex.Message}");
        }

        return normalized;
    }

    private static bool IsSafeServiceMediaReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || ContainsControlCharacter(value))
        {
            return false;
        }

        return !Uri.TryCreate(value, UriKind.Absolute, out _) &&
               !value.Contains("..", StringComparison.Ordinal) &&
               value.StartsWith("marketplace/services/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsControlCharacter(string value)
    {
        return value.Any(char.IsControl);
    }

    private sealed record ServiceListingIntent(
        string Title,
        string Description,
        string? Category,
        string? Currency,
        string? ServiceImages,
        string? Packages);
}
