using System.Security.Cryptography;
using System.Text.Json;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Localization;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.Services.Admin;

public sealed class FooterAuthoringMapper
{
    public AdminStorefrontFooterConfigurationResponse MapStorefrontResponse(
        FooterAuthoringScope scope,
        IReadOnlyCollection<StorefrontFooterSection> sections,
        IReadOnlyCollection<StorefrontSocialLink> socialLinks)
    {
        var response = new AdminStorefrontFooterConfigurationResponse
        {
            Scope = scope == FooterAuthoringScope.Tenant ? "Tenant" : "Global",
            UpdatedAtUtc = GetUpdatedAtUtc(
                sections.Select(section => section.UpdatedAt)
                    .Concat(sections.SelectMany(section => section.Links.Select(link => link.UpdatedAt)))
                    .Concat(socialLinks.Select(link => link.UpdatedAt))),
            Sections = sections
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.Id)
                .Select(section => new AdminStorefrontFooterSectionResponse
                {
                    Id = section.Id,
                    Title = section.Title,
                    TitleLocalized = new LocalizedField { En = section.Title, Ar = section.TitleAr },
                    Order = section.SortOrder,
                    IsActive = section.IsActive,
                    Links = section.Links
                        .OrderBy(link => link.SortOrder)
                        .ThenBy(link => link.Id)
                        .Select(link => new AdminStorefrontFooterLinkResponse
                        {
                            Id = link.Id,
                            Label = link.Label,
                            LabelLocalized = new LocalizedField { En = link.Label, Ar = link.LabelAr },
                            Type = link.LinkType,
                            Url = link.Url,
                            CmsPageSlug = link.CmsPageSlug,
                            OpenInNewTab = link.OpenInNewTab,
                            Order = link.SortOrder,
                            IsActive = link.IsActive
                        })
                        .ToList()
                })
                .ToList(),
            SocialLinks = socialLinks
                .OrderBy(link => link.SortOrder)
                .ThenBy(link => link.Id)
                .Select(link => new AdminStorefrontSocialLinkResponse
                {
                    Id = link.Id,
                    Platform = link.Platform,
                    Url = link.Url,
                    Icon = link.Icon,
                    Order = link.SortOrder,
                    IsActive = link.IsActive
                })
                .ToList()
        };

        response.Revision = ComputeRevision(response);
        return response;
    }

    public AdminStorefrontFooterConfigurationResponse MapMarketplaceResponse(
        IReadOnlyCollection<MarketplaceFooterSection> sections,
        IReadOnlyCollection<MarketplaceSocialLink> socialLinks)
    {
        var response = new AdminStorefrontFooterConfigurationResponse
        {
            Scope = "Marketplace",
            UpdatedAtUtc = GetUpdatedAtUtc(
                sections.Select(section => section.UpdatedAt)
                    .Concat(sections.SelectMany(section => section.Links.Select(link => link.UpdatedAt)))
                    .Concat(socialLinks.Select(link => link.UpdatedAt))),
            Sections = sections
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.Id)
                .Select(section => new AdminStorefrontFooterSectionResponse
                {
                    Id = section.Id,
                    Title = section.Title,
                    TitleLocalized = MarketplaceLocalization.MergeField(section.TitleJson, section.Title),
                    Order = section.SortOrder,
                    IsActive = section.IsActive,
                    Links = section.Links
                        .OrderBy(link => link.SortOrder)
                        .ThenBy(link => link.Id)
                        .Select(link => new AdminStorefrontFooterLinkResponse
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
                        })
                        .ToList()
                })
                .ToList(),
            SocialLinks = socialLinks
                .OrderBy(link => link.SortOrder)
                .ThenBy(link => link.Id)
                .Select(link => new AdminStorefrontSocialLinkResponse
                {
                    Id = link.Id,
                    Platform = link.Platform,
                    Url = link.Url,
                    Icon = link.Icon,
                    Order = link.SortOrder,
                    IsActive = link.IsActive
                })
                .ToList()
        };

        response.Revision = ComputeRevision(response);
        return response;
    }

    public IReadOnlyList<StorefrontFooterSection> MapStorefrontSections(
        AdminStorefrontFooterConfigurationRequest request,
        Guid? tenantId,
        DateTime now)
    {
        return request.Sections!.Select(section => new StorefrontFooterSection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = ResolveEnglish(section.TitleLocalized, section.Title),
            TitleAr = NormalizeOptional(section.TitleLocalized?.Ar),
            SortOrder = section.Order,
            IsActive = section.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            Links = section.Links!.Select(link => new StorefrontFooterLink
            {
                Id = Guid.NewGuid(),
                Label = ResolveEnglish(link.LabelLocalized, link.Label),
                LabelAr = NormalizeOptional(link.LabelLocalized?.Ar),
                LinkType = link.Type,
                Url = link.Type == FooterLinkType.Cms ? null : link.Url!.Trim(),
                CmsPageSlug = NormalizeCmsSlug(link),
                OpenInNewTab = link.OpenInNewTab,
                SortOrder = link.Order,
                IsActive = link.IsActive,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList()
        }).ToList();
    }

    public IReadOnlyList<StorefrontSocialLink> MapStorefrontSocialLinks(
        AdminStorefrontFooterConfigurationRequest request,
        Guid? tenantId,
        DateTime now)
    {
        return request.SocialLinks!.Select(link => new StorefrontSocialLink
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Platform = link.Platform.Trim(),
            Url = link.Url.Trim(),
            Icon = NormalizeOptional(link.Icon),
            SortOrder = link.Order,
            IsActive = link.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
    }

    public IReadOnlyList<MarketplaceFooterSection> MapMarketplaceSections(
        AdminStorefrontFooterConfigurationRequest request,
        DateTime now)
    {
        return request.Sections!.Select(section =>
        {
            var title = BuildLocalized(section.TitleLocalized, section.Title);
            return new MarketplaceFooterSection
            {
                Id = Guid.NewGuid(),
                Title = title.En!,
                TitleJson = MarketplaceLocalization.SerializeField(title, 120),
                SortOrder = section.Order,
                IsActive = section.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
                Links = section.Links!.Select(link =>
                {
                    var label = BuildLocalized(link.LabelLocalized, link.Label);
                    return new MarketplaceFooterLink
                    {
                        Id = Guid.NewGuid(),
                        Label = label.En!,
                        LabelJson = MarketplaceLocalization.SerializeField(label, 160),
                        LinkType = link.Type,
                        Url = link.Type == FooterLinkType.Cms ? null : link.Url!.Trim(),
                        CmsPageSlug = NormalizeCmsSlug(link),
                        OpenInNewTab = link.OpenInNewTab,
                        SortOrder = link.Order,
                        IsActive = link.IsActive,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                }).ToList()
            };
        }).ToList();
    }

    public IReadOnlyList<MarketplaceSocialLink> MapMarketplaceSocialLinks(
        AdminStorefrontFooterConfigurationRequest request,
        DateTime now)
    {
        return request.SocialLinks!.Select(link => new MarketplaceSocialLink
        {
            Id = Guid.NewGuid(),
            Platform = link.Platform.Trim(),
            Url = link.Url.Trim(),
            Icon = NormalizeOptional(link.Icon),
            SortOrder = link.Order,
            IsActive = link.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
    }

    private static string ComputeRevision(AdminStorefrontFooterConfigurationResponse response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            response.Scope,
            response.Sections,
            response.SocialLinks
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static DateTime GetUpdatedAtUtc(IEnumerable<DateTime> timestamps)
    {
        var materialized = timestamps.ToList();
        return materialized.Count == 0 ? DateTime.UnixEpoch : materialized.Max().ToUniversalTime();
    }

    private static LocalizedField BuildLocalized(LocalizedField? localized, string fallback)
    {
        return new LocalizedField
        {
            En = ResolveEnglish(localized, fallback),
            Ar = NormalizeOptional(localized?.Ar)
        };
    }

    private static string ResolveEnglish(LocalizedField? localized, string fallback)
    {
        return (localized?.En ?? fallback).Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeCmsSlug(AdminStorefrontFooterLinkRequest link)
    {
        return link.Type == FooterLinkType.Cms
            ? CmsPageReadService.NormalizeSlug(link.CmsPageSlug!)
            : null;
    }
}
