using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public interface IAdminStorefrontFooterService
{
    Task<AdminStorefrontFooterConfigurationResponse> GetConfigurationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<AdminStorefrontFooterConfigurationResponse> ReplaceConfigurationAsync(
        Guid tenantId,
        AdminStorefrontFooterConfigurationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AdminStorefrontFooterService : IAdminStorefrontFooterService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;

    public AdminStorefrontFooterService(ApplicationDbContext dbContext, IEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
    }

    public async Task<AdminStorefrontFooterConfigurationResponse> GetConfigurationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var sections = await LoadSectionsAsync(tenantId, cancellationToken);
        var socialLinks = await LoadSocialLinksAsync(tenantId, cancellationToken);

        return new AdminStorefrontFooterConfigurationResponse
        {
            Scope = "Tenant",
            UpdatedAtUtc = GetUpdatedAtUtc(sections, socialLinks),
            Sections = sections.Select(MapSection).ToList(),
            SocialLinks = socialLinks.Select(MapSocialLink).ToList()
        };
    }

    public async Task<AdminStorefrontFooterConfigurationResponse> ReplaceConfigurationAsync(
        Guid tenantId,
        AdminStorefrontFooterConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = request.Validate();
        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        var currentSections = await LoadSectionsAsync(tenantId, cancellationToken);
        var currentSocialLinks = await LoadSocialLinksAsync(tenantId, cancellationToken);
        var currentUpdatedAtUtc = GetUpdatedAtUtc(currentSections, currentSocialLinks);
        if (!request.ExpectedUpdatedAtUtc.HasValue ||
            request.ExpectedUpdatedAtUtc.Value.ToUniversalTime() != currentUpdatedAtUtc.ToUniversalTime())
        {
            throw new ConcurrencyConflictException("StorefrontFooter", tenantId);
        }

        var publishedPageSlugs = await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId)
            .Where(page => !page.IsDeleted && page.IsPublished && !page.IsDraft && page.PublishedAt != null)
            .Select(page => page.Slug)
            .Distinct()
            .ToListAsync(cancellationToken);

        var knownSlugs = new HashSet<string>(publishedPageSlugs, StringComparer.OrdinalIgnoreCase);
        var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in request.Sections)
        {
            foreach (var link in section.Links)
            {
                ValidateLink(link, knownSlugs, allowMissingCmsPage: false);
                if (!targetKeys.Add(GetTargetKey(link)))
                {
                    throw new InvalidOperationException($"Duplicate footer target '{GetTargetKey(link)}' is not allowed.");
                }
            }
        }

        foreach (var socialLink in request.SocialLinks)
        {
            ValidateSocialLink(socialLink);
        }

        var existingSections = await _dbContext.StorefrontFooterSections
            .Where(section => section.TenantId == tenantId)
            .Include(section => section.Links)
            .ToListAsync(cancellationToken);

        var existingSocialLinks = await _dbContext.StorefrontSocialLinks
            .Where(link => link.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        _dbContext.StorefrontFooterSections.RemoveRange(existingSections);
        _dbContext.StorefrontSocialLinks.RemoveRange(existingSocialLinks);

        var now = DateTime.UtcNow;
        foreach (var sectionWithIndex in request.Sections.Select((section, index) => (section, index)))
        {
            var section = sectionWithIndex.section;
            var entity = new StorefrontFooterSection
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Title = section.Title.Trim(),
                SortOrder = sectionWithIndex.index,
                IsActive = section.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
                Links = section.Links.Select((link, index) => new StorefrontFooterLink
                {
                    Id = Guid.NewGuid(),
                    Label = link.Label.Trim(),
                    LabelAr = string.IsNullOrWhiteSpace(link.LabelAr) ? null : link.LabelAr.Trim(),
                    LinkType = link.Type,
                    Url = NormalizeUrl(link),
                    CmsPageSlug = NormalizeCmsSlug(link),
                    OpenInNewTab = link.OpenInNewTab,
                    SortOrder = index,
                    IsActive = link.IsActive,
                    CreatedAt = now,
                    UpdatedAt = now
                }).ToList()
            };

            _dbContext.StorefrontFooterSections.Add(entity);
        }

        foreach (var socialWithIndex in request.SocialLinks.Select((socialLink, index) => (socialLink, index)))
        {
            var socialLink = socialWithIndex.socialLink;
            _dbContext.StorefrontSocialLinks.Add(new StorefrontSocialLink
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Platform = socialLink.Platform.Trim(),
                Url = socialLink.Url.Trim(),
                Icon = string.IsNullOrWhiteSpace(socialLink.Icon) ? null : socialLink.Icon.Trim(),
                SortOrder = socialWithIndex.index,
                IsActive = socialLink.IsActive,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await SaveChangesAsync(cancellationToken);
        await _eventPublisher.PublishAsync(
            new StorefrontFooterUpdatedEvent
            {
                TenantId = tenantId,
                UpdatedAtUtc = now
            },
            cancellationToken);

        return await GetConfigurationAsync(tenantId, cancellationToken);
    }

    private Task<List<StorefrontFooterSection>> LoadSectionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return _dbContext.StorefrontFooterSections
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(section => section.TenantId == tenantId)
            .Include(section => section.Links)
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Title)
            .ToListAsync(cancellationToken);
    }

    private Task<List<StorefrontSocialLink>> LoadSocialLinksAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return _dbContext.StorefrontSocialLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(link => link.TenantId == tenantId)
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
            Links = section.Links
                .OrderBy(link => link.SortOrder)
                .Select(MapLink)
                .ToList()
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

        return timestamps.Count == 0 ? DateTime.UnixEpoch : timestamps.Max();
    }

    private static void ValidateLink(
        AdminStorefrontFooterLinkRequest link,
        IReadOnlySet<string> knownSlugs,
        bool allowMissingCmsPage)
    {
        switch (link.Type)
        {
            case FooterLinkType.Internal when !IsValidInternalUrl(link.Url):
                throw new InvalidOperationException($"Internal footer link '{link.Label}' must start with '/'.");
            case FooterLinkType.External when !IsValidExternalUrl(link.Url):
                throw new InvalidOperationException($"External footer link '{link.Label}' must use http:// or https://.");
            case FooterLinkType.Cms:
                var normalizedSlug = CmsPageReadService.NormalizeSlug(link.CmsPageSlug ?? string.Empty);
                if (!allowMissingCmsPage && !knownSlugs.Contains(normalizedSlug))
                {
                    throw new InvalidOperationException($"CMS footer link '{link.Label}' targets page '{normalizedSlug}', which does not exist in this tenant.");
                }
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

    private static string? NormalizeUrl(AdminStorefrontFooterLinkRequest link)
    {
        return link.Type == FooterLinkType.Cms ? null : link.Url?.Trim();
    }

    private static string? NormalizeCmsSlug(AdminStorefrontFooterLinkRequest link)
    {
        return link.Type == FooterLinkType.Cms
            ? CmsPageReadService.NormalizeSlug(link.CmsPageSlug ?? string.Empty)
            : null;
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("StorefrontFooter", Guid.Empty, ex);
        }
    }

    private static bool IsValidInternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.StartsWith("/", StringComparison.Ordinal) &&
               !url.StartsWith("//", StringComparison.Ordinal) &&
               !url.Contains('\\') &&
               !url.Any(char.IsControl) &&
               !url.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
               !url.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) &&
               !url.Contains("preview=true", StringComparison.OrdinalIgnoreCase) &&
               !url.Contains("previewtoken=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidExternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
               (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps) &&
               string.IsNullOrEmpty(parsedUrl.UserInfo);
    }

    private static string GetTargetKey(AdminStorefrontFooterLinkRequest link)
    {
        var target = link.Type == FooterLinkType.Cms
            ? CmsPageReadService.NormalizeSlug(link.CmsPageSlug ?? string.Empty)
            : link.Url?.Trim();
        return $"{link.Type}:{target}";
    }
}
