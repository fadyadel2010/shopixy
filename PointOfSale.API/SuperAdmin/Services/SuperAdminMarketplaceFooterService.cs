using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Localization;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Localization;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminMarketplaceFooterService
{
    Task<MarketplaceFooterConfigurationResponse> GetConfigurationAsync(
        CancellationToken cancellationToken = default);

    Task<MarketplaceFooterConfigurationResponse> ReplaceConfigurationAsync(
        MarketplaceFooterConfigurationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminMarketplaceFooterService : ISuperAdminMarketplaceFooterService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;

    public SuperAdminMarketplaceFooterService(ApplicationDbContext dbContext, IEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
    }

    public async Task<MarketplaceFooterConfigurationResponse> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var sections = await _dbContext.MarketplaceFooterSections
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(section => section.Links)
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Title)
            .ToListAsync(cancellationToken);

        var socialLinks = await _dbContext.MarketplaceSocialLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Platform)
            .ToListAsync(cancellationToken);

        return new MarketplaceFooterConfigurationResponse
        {
            UpdatedAtUtc = GetUpdatedAtUtc(sections, socialLinks),
            Sections = sections.Select(MapSection).ToList(),
            SocialLinks = socialLinks.Select(MapSocialLink).ToList()
        };
    }

    public async Task<MarketplaceFooterConfigurationResponse> ReplaceConfigurationAsync(
        MarketplaceFooterConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = request.Validate();
        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        var knownSlugs = await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.IsDraft || page.IsPublished)
            .Select(page => page.Slug)
            .Distinct()
            .ToListAsync(cancellationToken);

        var slugSet = new HashSet<string>(knownSlugs, StringComparer.OrdinalIgnoreCase);
        foreach (var section in request.Sections)
        {
            foreach (var link in section.Links)
            {
                ValidateLink(link, slugSet);
            }
        }

        foreach (var socialLink in request.SocialLinks)
        {
            ValidateSocialLink(socialLink);
        }

        var existingSections = await _dbContext.MarketplaceFooterSections
            .Include(section => section.Links)
            .ToListAsync(cancellationToken);

        var existingSocialLinks = await _dbContext.MarketplaceSocialLinks
            .ToListAsync(cancellationToken);

        _dbContext.MarketplaceFooterSections.RemoveRange(existingSections);
        _dbContext.MarketplaceSocialLinks.RemoveRange(existingSocialLinks);

        var now = DateTime.UtcNow;
        foreach (var section in request.Sections)
        {
            _dbContext.MarketplaceFooterSections.Add(new MarketplaceFooterSection
            {
                Id = Guid.NewGuid(),
                Title = ResolveEnglishText(section.TitleLocalized, section.Title, "section title"),
                TitleJson = MarketplaceLocalization.SerializeField(
                    BuildLocalizedField(section.TitleLocalized, section.Title, 120, true, "section title"),
                    120),
                SortOrder = section.Order,
                IsActive = section.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
                Links = section.Links.Select(link => new MarketplaceFooterLink
                {
                    Id = Guid.NewGuid(),
                    Label = ResolveEnglishText(link.LabelLocalized, link.Label, "footer link label"),
                    LabelJson = MarketplaceLocalization.SerializeField(
                        BuildLocalizedField(link.LabelLocalized, link.Label, 160, true, "footer link label"),
                        160),
                    LinkType = link.Type,
                    Url = link.Type == FooterLinkType.Cms ? null : link.Url?.Trim(),
                    CmsPageSlug = link.Type == FooterLinkType.Cms
                        ? MarketplaceCmsPageReadService.NormalizeSlug(link.CmsPageSlug ?? string.Empty)
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
            _dbContext.MarketplaceSocialLinks.Add(new MarketplaceSocialLink
            {
                Id = Guid.NewGuid(),
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
            new MarketplaceFooterUpdatedEvent
            {
                UpdatedAtUtc = now
            },
            cancellationToken);

        return await GetConfigurationAsync(cancellationToken);
    }

    private static MarketplaceFooterSectionResponse MapSection(MarketplaceFooterSection section)
    {
        return new MarketplaceFooterSectionResponse
        {
            Id = section.Id,
            Title = section.Title,
            TitleLocalized = MarketplaceLocalization.MergeField(section.TitleJson, section.Title),
            Order = section.SortOrder,
            IsActive = section.IsActive,
            Links = section.Links.OrderBy(link => link.SortOrder).Select(MapLink).ToList()
        };
    }

    private static MarketplaceFooterLinkResponse MapLink(MarketplaceFooterLink link)
    {
        return new MarketplaceFooterLinkResponse
        {
            Id = link.Id,
            Label = link.Label,
            LabelLocalized = MarketplaceLocalization.MergeField(link.LabelJson, link.Label),
            Type = link.LinkType,
            Url = link.Url,
            CmsPageSlug = link.CmsPageSlug,
            OpenInNewTab = link.OpenInNewTab,
            Order = link.SortOrder,
            IsActive = link.IsActive
        };
    }

    private static MarketplaceSocialLinkResponse MapSocialLink(MarketplaceSocialLink link)
    {
        return new MarketplaceSocialLinkResponse
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
        IReadOnlyCollection<MarketplaceFooterSection> sections,
        IReadOnlyCollection<MarketplaceSocialLink> socialLinks)
    {
        var timestamps = sections.Select(section => section.UpdatedAt)
            .Concat(sections.SelectMany(section => section.Links.Select(link => link.UpdatedAt)))
            .Concat(socialLinks.Select(link => link.UpdatedAt))
            .ToList();

        return timestamps.Count == 0 ? DateTime.UtcNow : timestamps.Max();
    }

    private static void ValidateLink(
        MarketplaceFooterLinkRequest link,
        IReadOnlySet<string> knownSlugs)
    {
        switch (link.Type)
        {
            case FooterLinkType.Internal when !IsValidInternalUrl(link.Url):
                throw new InvalidOperationException($"Internal footer link '{link.Label}' must start with '/'.");
            case FooterLinkType.External when !IsValidExternalUrl(link.Url):
                throw new InvalidOperationException($"External footer link '{link.Label}' must use http:// or https://.");
            case FooterLinkType.Cms:
                var normalizedSlug = MarketplaceCmsPageReadService.NormalizeSlug(link.CmsPageSlug ?? string.Empty);
                if (!knownSlugs.Contains(normalizedSlug))
                {
                    throw new InvalidOperationException($"Marketplace CMS footer link '{link.Label}' targets page '{normalizedSlug}', which does not exist.");
                }
                break;
        }
    }

    private static void ValidateSocialLink(MarketplaceSocialLinkRequest link)
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
            throw new ConcurrencyConflictException("MarketplaceFooter", Guid.Empty, ex);
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

    private static LocalizedField BuildLocalizedField(
        LocalizedField? requestField,
        string? legacyValue,
        int maxLength,
        bool required,
        string fieldName)
    {
        var englishValue = MarketplaceLocalization.NormalizeOptionalText(requestField?.En ?? legacyValue, maxLength);
        var arabicValue = MarketplaceLocalization.NormalizeOptionalText(requestField?.Ar, maxLength);

        if (required && string.IsNullOrWhiteSpace(englishValue))
        {
            throw new InvalidOperationException($"English {fieldName} is required.");
        }

        return new LocalizedField
        {
            En = englishValue,
            Ar = arabicValue
        };
    }

    private static string ResolveEnglishText(
        LocalizedField? requestField,
        string? legacyValue,
        string fieldName)
    {
        var englishValue = MarketplaceLocalization.NormalizeOptionalText(requestField?.En ?? legacyValue);
        if (string.IsNullOrWhiteSpace(englishValue))
        {
            throw new InvalidOperationException($"English {fieldName} is required.");
        }

        return englishValue;
    }
}
