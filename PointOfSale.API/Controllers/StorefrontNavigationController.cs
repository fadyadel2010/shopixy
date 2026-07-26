using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/storefront/navigation")]
[AllowAnonymous]
public sealed class StorefrontNavigationController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontNavigationController> _logger;

    public StorefrontNavigationController(
        ApplicationDbContext dbContext,
        ILogger<StorefrontNavigationController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(NavigationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetNavigation(CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var validation = PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var tenantId = validation.TenantId!.Value;

        try
        {
            var authority = await (
                    from config in _dbContext.StoreConfigs.AsNoTracking()
                    join tenant in _dbContext.Tenants.AsNoTracking()
                        on config.TenantId equals tenant.Id
                    where config.TenantId == tenantId &&
                          tenant.Status == TenantStatus.Active &&
                          tenant.DeletedAtUtc == null
                    select new
                    {
                        config.CustomizationJson,
                        config.UpdatedAtUtc,
                        config.StorefrontStatus
                    })
                .SingleOrDefaultAsync(cancellationToken);

            if (authority == null ||
                !string.Equals(authority.StorefrontStatus?.Trim(), "live", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(EmptyNavigation(authority?.UpdatedAtUtc));
            }

            var navigation = ExtractLiveNavigation(authority.CustomizationJson, authority.UpdatedAtUtc);
            navigation = await RecomposeNavigationFromSqlAuthorityAsync(
                tenantId,
                navigation,
                traceId,
                cancellationToken);

            return Ok(navigation);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Navigation SQL live configuration is invalid and was suppressed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);
            return Ok(EmptyNavigation());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetNavigation failed | TenantId: {TenantId} | TraceId: {TraceId}", tenantId, traceId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private static NavigationDto ExtractLiveNavigation(string? customizationJson, DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customizationJson))
        {
            return EmptyNavigation(updatedAtUtc);
        }

        using var document = JsonDocument.Parse(customizationJson);
        if (!document.RootElement.TryGetProperty("navigation", out var navigation) ||
            navigation.ValueKind != JsonValueKind.Object ||
            !navigation.TryGetProperty("live", out var live) ||
            live.ValueKind != JsonValueKind.Object)
        {
            return EmptyNavigation(updatedAtUtc);
        }

        return new NavigationDto
        {
            Header = ExtractMenu(live, "header"),
            Footer = ExtractMenu(live, "footer"),
            UpdatedAtUtc = updatedAtUtc
        };
    }

    private static NavigationMenuDto ExtractMenu(JsonElement live, string location)
    {
        if (!live.TryGetProperty(location, out var menu))
        {
            return new NavigationMenuDto();
        }

        var links = menu.ValueKind == JsonValueKind.Object && menu.TryGetProperty("links", out var nested)
            ? nested
            : menu;
        if (links.ValueKind != JsonValueKind.Array)
        {
            return new NavigationMenuDto();
        }

        return new NavigationMenuDto
        {
            Links = links.EnumerateArray()
                .Select((link, index) => new
                {
                    Dto = new NavigationLinkDto
                    {
                        Label = GetString(link, "label")?.Trim() ?? string.Empty,
                        LabelAr = GetString(link, "labelAr")?.Trim(),
                        Type = GetString(link, "type")?.Trim().ToLowerInvariant() ?? string.Empty,
                        Path = GetString(link, "path")?.Trim(),
                        Slug = GetString(link, "slug")?.Trim().Trim('/').ToLowerInvariant(),
                        OpenInNewTab = GetBool(link, "openInNewTab")
                    },
                    Order = GetInt(link, "order", index),
                    SourceIndex = index,
                    Enabled = GetBool(link, "enabled", true)
                })
                .Where(item => item.Enabled)
                .OrderBy(item => item.Order)
                .ThenBy(item => item.SourceIndex)
                .Select(item => item.Dto)
                .ToList()
        };
    }

    private async Task<NavigationDto> RecomposeNavigationFromSqlAuthorityAsync(
        Guid tenantId,
        NavigationDto navigation,
        string traceId,
        CancellationToken cancellationToken)
    {
        var lineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);

        var liveCategorySlugs = lineage.Rows
            .Where(row => lineage.IsLiveInLineage(row.CategoryId))
            .Select(row => GenerateSlug(row.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var publishedCmsSlugs = (await _dbContext.CmsPages
                .AsNoTracking()
                .Where(page => page.TenantId == tenantId)
                .Where(page => !page.IsDeleted && page.IsPublished && !page.IsDraft && page.PublishedAt != null)
                .Select(page => page.Slug)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var header = FilterNavigationMenu(navigation.Header, liveCategorySlugs, publishedCmsSlugs);
        var footer = FilterNavigationMenu(navigation.Footer, liveCategorySlugs, publishedCmsSlugs);

        if (header.Links.Count != navigation.Header.Links.Count ||
            footer.Links.Count != navigation.Footer.Links.Count)
        {
            _logger.LogWarning(
                "Navigation targets were suppressed by current SQL authority | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);
        }

        return new NavigationDto
        {
            Header = header,
            Footer = footer,
            UpdatedAtUtc = navigation.UpdatedAtUtc
        };
    }

    private static NavigationMenuDto FilterNavigationMenu(
        NavigationMenuDto menu,
        ISet<string> liveCategorySlugs,
        ISet<string> publishedCmsSlugs)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new NavigationMenuDto
        {
            Links = menu.Links
                .Where(link => IsAuthoritativeTarget(link, liveCategorySlugs, publishedCmsSlugs))
                .Where(link => targets.Add(GetTargetKey(link)))
                .ToList()
        };
    }

    private static bool IsAuthoritativeTarget(
        NavigationLinkDto link,
        ISet<string> liveCategorySlugs,
        ISet<string> publishedCmsSlugs)
    {
        if (string.IsNullOrWhiteSpace(link.Label) || link.Label.Length > 120)
        {
            return false;
        }

        return link.Type switch
        {
            "cms" => !string.IsNullOrWhiteSpace(link.Slug) && publishedCmsSlugs.Contains(link.Slug),
            "category" => IsLiveCategoryNavigationLink(link, liveCategorySlugs),
            "internal" => IsSafeInternalPath(link.Path),
            "external" => IsSafeExternalUrl(link.Path),
            _ => false
        };
    }

    private static bool IsLiveCategoryNavigationLink(
        NavigationLinkDto link,
        ISet<string> liveCategorySlugs)
    {
        var slug = !string.IsNullOrWhiteSpace(link.Slug)
            ? link.Slug
            : ExtractCategorySlugFromPath(link.Path);

        return !string.IsNullOrWhiteSpace(slug) &&
               liveCategorySlugs.Contains(slug.Trim().Trim('/'));
    }

    private static bool IsSafeInternalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            path.Any(char.IsControl) ||
            !Uri.TryCreate(path, UriKind.Relative, out _))
        {
            return false;
        }

        var lowered = path.ToLowerInvariant();
        return !lowered.StartsWith("/api/", StringComparison.Ordinal) &&
               !lowered.StartsWith("/admin", StringComparison.Ordinal) &&
               !lowered.Contains("preview=true", StringComparison.Ordinal) &&
               !lowered.Contains("previewtoken=", StringComparison.Ordinal);
    }

    private static bool IsSafeExternalUrl(string? path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    private static string GetTargetKey(NavigationLinkDto link)
    {
        var target = link.Type is "cms" or "category" ? link.Slug : link.Path;
        return $"{link.Type}:{target}";
    }

    private static string? ExtractCategorySlugFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim().Trim('/');
        const string prefix = "category/";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }

    private static string GenerateSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "category";
        }

        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");

        return System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
    }

    private static NavigationDto EmptyNavigation(DateTime? updatedAtUtc = null)
    {
        return new NavigationDto
        {
            Header = new NavigationMenuDto(),
            Footer = new NavigationMenuDto(),
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UnixEpoch
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool GetBool(JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return defaultValue;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }
}
