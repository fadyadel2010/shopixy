using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminStorefrontFooterService
{
    Task<AdminStorefrontFooterConfigurationResponse> GetGlobalConfigurationAsync(
        CancellationToken cancellationToken = default);

    Task<AdminStorefrontFooterConfigurationResponse> ReplaceGlobalConfigurationAsync(
        AdminStorefrontFooterConfigurationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminStorefrontFooterService : ISuperAdminStorefrontFooterService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;

    public SuperAdminStorefrontFooterService(ApplicationDbContext dbContext, IEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
    }

    public async Task<AdminStorefrontFooterConfigurationResponse> GetGlobalConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var sections = await LoadSectionsAsync(cancellationToken);
        var socialLinks = await LoadSocialLinksAsync(cancellationToken);

        return new AdminStorefrontFooterConfigurationResponse
        {
            Scope = "Global",
            UpdatedAtUtc = GetUpdatedAtUtc(sections, socialLinks),
            Sections = sections.Select(MapSection).ToList(),
            SocialLinks = socialLinks.Select(MapSocialLink).ToList()
        };
    }

    public async Task<AdminStorefrontFooterConfigurationResponse> ReplaceGlobalConfigurationAsync(
        AdminStorefrontFooterConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = request.Validate(allowCmsWithoutExistingPage: true);
        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        foreach (var section in request.Sections)
        {
            foreach (var link in section.Links)
            {
                ValidateLink(link);
            }
        }

        foreach (var socialLink in request.SocialLinks)
        {
            ValidateSocialLink(socialLink);
        }

        var existingSections = await _dbContext.StorefrontFooterSections
            .Where(section => section.TenantId == null)
            .Include(section => section.Links)
            .ToListAsync(cancellationToken);

        var existingSocialLinks = await _dbContext.StorefrontSocialLinks
            .Where(link => link.TenantId == null)
            .ToListAsync(cancellationToken);

        _dbContext.StorefrontFooterSections.RemoveRange(existingSections);
        _dbContext.StorefrontSocialLinks.RemoveRange(existingSocialLinks);

        var now = DateTime.UtcNow;
        foreach (var section in request.Sections)
        {
            _dbContext.StorefrontFooterSections.Add(new StorefrontFooterSection
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                Title = section.Title.Trim(),
                SortOrder = section.Order,
                IsActive = section.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
                Links = section.Links.Select(link => new StorefrontFooterLink
                {
                    Id = Guid.NewGuid(),
                    Label = link.Label.Trim(),
                    LabelAr = string.IsNullOrWhiteSpace(link.LabelAr) ? null : link.LabelAr.Trim(),
                    LinkType = link.Type,
                    Url = link.Type == FooterLinkType.Cms ? null : link.Url?.Trim(),
                    CmsPageSlug = link.Type == FooterLinkType.Cms
                        ? CmsPageReadService.NormalizeSlug(link.CmsPageSlug ?? string.Empty)
                        : null,
                    OpenInNewTab = link.OpenInNewTab,
                    SortOrder = link.Order,
                    IsActive = link.IsActive,
                    CreatedAt = now,
                    UpdatedAt = now
                }).ToList()
            });
        }

        foreach (var socialLink in request.SocialLinks)
        {
            _dbContext.StorefrontSocialLinks.Add(new StorefrontSocialLink
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                Platform = socialLink.Platform.Trim(),
                Url = socialLink.Url.Trim(),
                Icon = string.IsNullOrWhiteSpace(socialLink.Icon) ? null : socialLink.Icon.Trim(),
                SortOrder = socialLink.Order,
                IsActive = socialLink.IsActive,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await SaveChangesAsync(cancellationToken);
        await _eventPublisher.PublishAsync(
            new StorefrontFooterUpdatedEvent
            {
                TenantId = null,
                UpdatedAtUtc = now
            },
            cancellationToken);

        return await GetGlobalConfigurationAsync(cancellationToken);
    }

    private Task<List<StorefrontFooterSection>> LoadSectionsAsync(CancellationToken cancellationToken)
    {
        return _dbContext.StorefrontFooterSections
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(section => section.TenantId == null)
            .Include(section => section.Links)
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Title)
            .ToListAsync(cancellationToken);
    }

    private Task<List<StorefrontSocialLink>> LoadSocialLinksAsync(CancellationToken cancellationToken)
    {
        return _dbContext.StorefrontSocialLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(link => link.TenantId == null)
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Platform)
            .ToListAsync(cancellationToken);
    }

    private static AdminStorefrontFooterSectionResponse MapSection(StorefrontFooterSection section)
    {
        return new AdminStorefrontFooterSectionResponse
        {
            Id = section.Id,
            Title = section.Title,
            Order = section.SortOrder,
            IsActive = section.IsActive,
            Links = section.Links.OrderBy(link => link.SortOrder).Select(MapLink).ToList()
        };
    }

    private static AdminStorefrontFooterLinkResponse MapLink(StorefrontFooterLink link)
    {
        return new AdminStorefrontFooterLinkResponse
        {
            Id = link.Id,
            Label = link.Label,
            LabelAr = link.LabelAr,
            Type = link.LinkType,
            Url = link.Url,
            CmsPageSlug = link.CmsPageSlug,
            OpenInNewTab = link.OpenInNewTab,
            Order = link.SortOrder,
            IsActive = link.IsActive
        };
    }

    private static AdminStorefrontSocialLinkResponse MapSocialLink(StorefrontSocialLink link)
    {
        return new AdminStorefrontSocialLinkResponse
        {
            Id = link.Id,
            Platform = link.Platform,
            Url = link.Url,
            Icon = link.Icon,
            Order = link.SortOrder,
            IsActive = link.IsActive
        };
    }

    private static DateTime GetUpdatedAtUtc(
        IReadOnlyCollection<StorefrontFooterSection> sections,
        IReadOnlyCollection<StorefrontSocialLink> socialLinks)
    {
        var timestamps = sections.Select(section => section.UpdatedAt)
            .Concat(sections.SelectMany(section => section.Links.Select(link => link.UpdatedAt)))
            .Concat(socialLinks.Select(link => link.UpdatedAt))
            .ToList();

        return timestamps.Count == 0 ? DateTime.UtcNow : timestamps.Max();
    }

    private static void ValidateLink(AdminStorefrontFooterLinkRequest link)
    {
        switch (link.Type)
        {
            case FooterLinkType.Internal when !IsValidInternalUrl(link.Url):
                throw new InvalidOperationException($"Internal footer link '{link.Label}' must start with '/'.");
            case FooterLinkType.External when !IsValidExternalUrl(link.Url):
                throw new InvalidOperationException($"External footer link '{link.Label}' must use http:// or https://.");
            case FooterLinkType.Cms:
                _ = CmsPageReadService.NormalizeSlug(link.CmsPageSlug ?? string.Empty);
                break;
        }
    }

    private static void ValidateSocialLink(AdminStorefrontSocialLinkRequest link)
    {
        if (!IsValidExternalUrl(link.Url))
        {
            throw new InvalidOperationException($"Social link '{link.Platform}' must use http:// or https://.");
        }
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("GlobalStorefrontFooter", Guid.Empty, ex);
        }
    }

    private static bool IsValidInternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.StartsWith("/", StringComparison.Ordinal) &&
               !url.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsValidExternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
               (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
    }
}
