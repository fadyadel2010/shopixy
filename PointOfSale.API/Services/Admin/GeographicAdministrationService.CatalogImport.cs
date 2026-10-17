using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.API.Services.Admin;

public sealed partial class GeographicAdministrationService
{
    public async Task<IReadOnlyList<CatalogReleaseSummaryDto>> ListCatalogReleasesAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.GeographicCatalogReleases.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(100)
            .Select(r => new CatalogReleaseSummaryDto(r.Id, r.ReleaseKey, r.ReleaseNumber, r.Status, r.CountryScopeIso2, r.CreatedByUserId, r.CreatedAtUtc)).ToArrayAsync(cancellationToken);

    public async Task<CatalogReviewDto?> ReadCatalogReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default)
    {
        var r = await _dbContext.GeographicCatalogReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        if (r == null) return null;
        var places = await _dbContext.GeographicPlaces.AsNoTracking().Where(p => p.IntroducedCatalogReleaseId == releaseId).OrderBy(p => p.StableKey).ToArrayAsync(cancellationToken);
        var edges = await _dbContext.GeographicPlaceHierarchyEdges.AsNoTracking().Where(e => e.IntroducedCatalogReleaseId == releaseId && e.IsPrimary && e.RetiredCatalogReleaseId == null).ToArrayAsync(cancellationToken);
        var names = await _dbContext.GeographicPlaceNames.AsNoTracking().Where(n => n.IntroducedCatalogReleaseId == releaseId && n.IsPreferred && n.RetiredCatalogReleaseId == null).ToArrayAsync(cancellationToken);
        var profiles = await _dbContext.GeographicCountryProfiles.AsNoTracking().Where(p => p.IntroducedCatalogReleaseId == releaseId).ToArrayAsync(cancellationToken);
        var import = await _dbContext.GeographicAudit.AsNoTracking().SingleOrDefaultAsync(a => a.CatalogReleaseId == releaseId && a.Action == "CatalogImportStagedV1", cancellationToken);
        return new(new(r.Id, r.ReleaseKey, r.ReleaseNumber, r.Status, r.CountryScopeIso2, r.CreatedByUserId, r.CreatedAtUtc), r.SourceSummary,
            import?.EvidenceObjectKey, import?.CorrelationId.Replace("geo-import-v1:sha256:", "", StringComparison.Ordinal),
            places.Select(p => new CatalogImportPlace(p.Id, p.StableKey, p.PlaceKind, p.CountryPlaceId, edges.FirstOrDefault(e => e.ChildPlaceId == p.Id)?.ParentPlaceId,
                p.LevelCode, p.CanonicalCode, p.ExternalStandard, p.ExternalStandardCode, names.Where(n => n.PlaceId == p.Id).Select(n => new CatalogImportName(n.LanguageTag, n.Name)).ToArray())).ToArray(),
            profiles.Select(p => new CatalogImportProfile(p.CountryPlaceId, p.Iso2, p.Iso3, p.DefaultLanguageTag, p.SupportedLanguageTags,
                p.RequiredLevelCodesForDelivery, p.RequiredLevelCodesForOperations, p.PostalCodePolicy, p.PostalCodeRegex, p.RequiresStreetLine1,
                p.AllowsCoordinates, p.RequiresCoordinateProvenance, p.OfflinePackageEnabled)).ToArray(), await InspectCatalogDraftAsync(releaseId, cancellationToken));
    }

    private static bool ImportText(string? value, int max) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && value == value.Trim() && !value.Any(char.IsControl);
    private static bool SourceUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsoluteUri.Length <= 200 && uri.AbsoluteUri == value;

    public async Task<CatalogImportPreviewDto> PreviewCatalogImportAsync(CatalogImportManifest manifest, CancellationToken cancellationToken = default)
    {
        var issues = new List<CatalogImportIssue>();
        void Issue(string code, string? key = null) { if (issues.Count < 100) issues.Add(new(code, key)); }
        var hash = Checksum(JsonSerializer.Serialize(manifest));
        CatalogImportPreviewDto Result() => new(issues.Count == 0, hash, manifest.Places?.Count ?? 0,
            manifest.Places?.Count(p => p?.ParentPlaceId.HasValue == true) ?? 0, issues);
        if (manifest.Places == null || manifest.Places.Count is < 3 or > 10000 || manifest.CountryProfiles == null || manifest.CountryProfiles.Count is < 1 or > 100)
        { Issue("CatalogImportSizeInvalid"); return Result(); }
        if (manifest.ReleaseNumber <= 0 || !ImportText(manifest.ReleaseKey, 64) || !ImportText(manifest.SourceSummary, 512) ||
            !Regex.IsMatch(manifest.CountryScopeIso2 ?? "", "\\A[A-Z]{2}\\z") || !SourceUri(manifest.SourceEvidenceUri) || !IsHash(manifest.SourceEvidenceSha256))
            Issue("CatalogImportProvenanceRequired");
        if (manifest.Places.Any(p => p == null) || manifest.CountryProfiles.Any(p => p == null))
        { Issue("CatalogImportRecordRequired"); return Result(); }
        if (manifest.Places.GroupBy(p => p.Id).Any(g => g.Count() > 1) ||
            manifest.Places.GroupBy(p => p.StableKey, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
            manifest.Places.GroupBy(p => (p.CountryPlaceId, p.PlaceKind, p.CanonicalCode?.ToUpperInvariant())).Any(g => g.Count() > 1) ||
            manifest.Places.Where(p => p.ExternalStandard != null).GroupBy(p => (p.ExternalStandard?.ToUpperInvariant(), p.ExternalStandardCode?.ToUpperInvariant())).Any(g => g.Count() > 1))
        { Issue("CatalogDuplicatePlaceIdentity"); return Result(); }
        var byId = manifest.Places.ToDictionary(p => p.Id);
        var countries = manifest.Places.Where(p => p.PlaceKind == GeographicPlaceKind.Country).ToDictionary(p => p.Id);
        if (countries.Count != manifest.CountryProfiles.Count || manifest.CountryProfiles.Select(p => p.CountryPlaceId).Distinct().Count() != countries.Count ||
            manifest.CountryProfiles.Select(p => p.Iso2).Distinct().Count() != countries.Count || manifest.CountryProfiles.Select(p => p.Iso3).Distinct().Count() != countries.Count)
        { Issue("CatalogCountryProfileInvalid"); return Result(); }
        foreach (var place in manifest.Places)
        {
            if (place.Id == Guid.Empty || !Enum.IsDefined(place.PlaceKind) || !ImportText(place.StableKey, 96) || !ImportText(place.CanonicalCode, 96) ||
                (place.PlaceKind != GeographicPlaceKind.Country && !ImportText(place.LevelCode, 64)) || place.LevelCode?.Length > 64 ||
                (place.ExternalStandard == null) != (place.ExternalStandardCode == null) ||
                place.ExternalStandard != null && (!ImportText(place.ExternalStandard, 64) || !ImportText(place.ExternalStandardCode, 96))) Issue("CatalogImportPlaceInvalid", place.StableKey);
            if (place.PlaceKind == GeographicPlaceKind.Country)
            {
                if (place.CountryPlaceId != null || place.ParentPlaceId != null) Issue("CatalogCountryLineageInvalid", place.StableKey);
            }
            else if (!place.CountryPlaceId.HasValue || !countries.ContainsKey(place.CountryPlaceId.Value) ||
                !place.ParentPlaceId.HasValue || !byId.TryGetValue(place.ParentPlaceId.Value, out var parent) ||
                (parent.PlaceKind == GeographicPlaceKind.Country ? parent.Id : parent.CountryPlaceId) != place.CountryPlaceId ||
                (int)place.PlaceKind != (int)parent.PlaceKind + 1)
                Issue("CatalogHierarchyInvalid", place.StableKey);
            var profile = manifest.CountryProfiles.SingleOrDefault(p => p.CountryPlaceId == (place.PlaceKind == GeographicPlaceKind.Country ? place.Id : place.CountryPlaceId));
            if (place.Names == null || place.Names.Count is < 1 or > 20 || place.Names.Any(n => n == null || !ImportText(n.LanguageTag, 35) || !ImportText(n.Name, 200)) ||
                place.Names.GroupBy(n => n.LanguageTag, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
                profile?.SupportedLanguageTags == null || profile.SupportedLanguageTags.Any(language => !place.Names.Any(n => n.LanguageTag == language)))
                Issue("CatalogLocalizationRequired", place.StableKey);
        }
        foreach (var profile in manifest.CountryProfiles)
        {
            if (!countries.TryGetValue(profile.CountryPlaceId, out var country) || profile.Iso2 != manifest.CountryScopeIso2 || country.CanonicalCode != profile.Iso2 ||
                !Regex.IsMatch(profile.Iso2 ?? "", "\\A[A-Z]{2}\\z") || !Regex.IsMatch(profile.Iso3 ?? "", "\\A[A-Z]{3}\\z") ||
                profile.SupportedLanguageTags == null || profile.SupportedLanguageTags.Length is < 1 or > 20 ||
                !profile.SupportedLanguageTags.Contains(profile.DefaultLanguageTag) || profile.SupportedLanguageTags.Any(l => !ImportText(l, 35)) ||
                profile.SupportedLanguageTags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.SupportedLanguageTags.Length ||
                profile.RequiredLevelCodesForDelivery == null || profile.RequiredLevelCodesForOperations == null ||
                profile.RequiredLevelCodesForDelivery.Concat(profile.RequiredLevelCodesForOperations).Any(l => !ImportText(l, 64)))
            { Issue("CatalogCountryProfileInvalid"); continue; }
            if (!Enum.IsDefined(profile.PostalCodePolicy) || profile.PostalCodeRegex?.Length > 500 ||
                (profile.PostalCodePolicy is GeographicPostalCodePolicy.Required or GeographicPostalCodePolicy.Optional && string.IsNullOrWhiteSpace(profile.PostalCodeRegex)))
                Issue("CatalogPostalRuleInvalid");
            else if (!string.IsNullOrWhiteSpace(profile.PostalCodeRegex))
            { try { _ = new Regex(profile.PostalCodeRegex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); } catch (ArgumentException) { Issue("CatalogPostalRuleInvalid"); } }
            var localities = manifest.Places.Where(p => p.CountryPlaceId == profile.CountryPlaceId && p.PlaceKind == GeographicPlaceKind.Locality).ToArray();
            if (localities.Length == 0) Issue("CatalogHierarchyIncomplete");
            foreach (var locality in localities)
            {
                var seen = new HashSet<Guid>(); var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var current = locality;
                while (true)
                {
                    if (!seen.Add(current.Id)) { Issue("CatalogHierarchyCycle", locality.StableKey); break; }
                    if (current.LevelCode != null) levels.Add(current.LevelCode);
                    if (!current.ParentPlaceId.HasValue || !byId.TryGetValue(current.ParentPlaceId.Value, out var parent)) break;
                    current = parent;
                }
                if (profile.RequiredLevelCodesForDelivery.Concat(profile.RequiredLevelCodesForOperations).Any(l => !levels.Contains(l))) Issue("CatalogRequiredLevelMissing", locality.StableKey);
            }
        }
        // Never overwrite or silently coalesce an existing canonical identity/release.
        if (issues.Count != 0) return Result();
        var ids = byId.Keys.ToArray(); var keys = manifest.Places.Select(p => p.StableKey.ToUpper()).ToArray();
        if (await _dbContext.GeographicPlaces.AnyAsync(p => ids.Contains(p.Id) || keys.Contains(p.StableKey.ToUpper()), cancellationToken)) Issue("CatalogImportExistingIdentityRequiresReconciliation");
        var iso2 = manifest.CountryProfiles.Select(p => p.Iso2).ToArray();
        var iso3 = manifest.CountryProfiles.Select(p => p.Iso3).ToArray();
        if (await _dbContext.GeographicCountryProfiles.AnyAsync(p => iso2.Contains(p.Iso2) || iso3.Contains(p.Iso3), cancellationToken)) Issue("CatalogImportExistingCountryRequiresReconciliation");
        if (await _dbContext.GeographicCatalogReleases.AnyAsync(r => r.ReleaseNumber == manifest.ReleaseNumber || r.ReleaseKey == manifest.ReleaseKey, cancellationToken)) Issue("CatalogImportReleaseAlreadyExists");
        return Result();
    }

    public async Task<GeographicOperationResultDto> StageCatalogImportAsync(StageCatalogImportRequest request, Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var tx = await BeginCatalogTransactionAsync(cancellationToken);
        if (!ValidActor(actorId, actorRole) || !ValidReason(request.Reason)) return Fail("CatalogActorAndReasonRequired");
        var preview = await PreviewCatalogImportAsync(request.Manifest, cancellationToken);
        if (!preview.Succeeded) return Fail(preview.Issues[0].Code);
        if (preview.ManifestChecksumSha256 != request.ExpectedManifestChecksumSha256) return Fail("CatalogImportContentChanged");
        var m = request.Manifest; var now = DateTime.UtcNow; var releaseId = Guid.NewGuid();
        _dbContext.GeographicCatalogReleases.Add(new() { Id = releaseId, ReleaseNumber = m.ReleaseNumber, ReleaseKey = m.ReleaseKey,
            CountryScopeIso2 = m.CountryScopeIso2, Status = GeographicReleaseStatus.Draft, SourceSummary = m.SourceSummary,
            ChecksumSha256 = preview.ManifestChecksumSha256, CreatedByUserId = actorId, CreatedAtUtc = now });
        foreach (var p in m.Places)
        {
            _dbContext.GeographicPlaces.Add(new() { Id = p.Id, StableKey = p.StableKey, PlaceKind = p.PlaceKind, CountryPlaceId = p.CountryPlaceId,
                LevelCode = p.LevelCode, CanonicalCode = p.CanonicalCode, ExternalStandard = p.ExternalStandard, ExternalStandardCode = p.ExternalStandardCode,
                IntroducedCatalogReleaseId = releaseId, Status = GeographicLifecycleStatus.Active, ValidFromUtc = now,
                Source = "catalog-import:" + m.SourceEvidenceSha256.ToLowerInvariant(), CreatedByUserId = actorId, UpdatedByUserId = actorId, UpdatedAtUtc = now, CreatedAtUtc = now });
            _dbContext.GeographicCatalogReleasePlaces.Add(new() { CatalogReleaseId = releaseId, PlaceId = p.Id, PlaceStatus = GeographicLifecycleStatus.Active });
            foreach (var name in p.Names) _dbContext.GeographicPlaceNames.Add(new() { Id = Guid.NewGuid(), PlaceId = p.Id,
                LanguageTag = name.LanguageTag, Name = name.Name, NormalizedName = name.Name.ToUpperInvariant(), SearchName = name.Name.ToUpperInvariant(),
                NameKind = GeographicNameKind.Official, IsPreferred = true, ValidFromUtc = now, IntroducedCatalogReleaseId = releaseId, CreatedAtUtc = now });
            if (p.ParentPlaceId.HasValue)
            {
                var edge = new GeographicPlaceHierarchyEdge { Id = Guid.NewGuid(), ParentPlaceId = p.ParentPlaceId.Value, ChildPlaceId = p.Id,
                    CountryPlaceId = p.CountryPlaceId!.Value, IntroducedCatalogReleaseId = releaseId, CreatedAtUtc = now };
                _dbContext.GeographicPlaceHierarchyEdges.Add(edge);
                _dbContext.GeographicCatalogReleaseEdges.Add(new() { CatalogReleaseId = releaseId, HierarchyEdgeId = edge.Id });
            }
        }
        foreach (var p in m.CountryProfiles) _dbContext.GeographicCountryProfiles.Add(new() { Id = Guid.NewGuid(), CountryPlaceId = p.CountryPlaceId,
            Iso2 = p.Iso2, Iso3 = p.Iso3, DefaultLanguageTag = p.DefaultLanguageTag, SupportedLanguageTags = p.SupportedLanguageTags,
            RequiredLevelCodesForDelivery = p.RequiredLevelCodesForDelivery, RequiredLevelCodesForOperations = p.RequiredLevelCodesForOperations,
            PostalCodePolicy = p.PostalCodePolicy, PostalCodeRegex = p.PostalCodeRegex, RequiresStreetLine1 = p.RequiresStreetLine1,
            AllowsCoordinates = p.AllowsCoordinates, RequiresCoordinateProvenance = p.RequiresCoordinateProvenance, OfflinePackageEnabled = p.OfflinePackageEnabled,
            IsActive = true, IntroducedCatalogReleaseId = releaseId, CreatedAtUtc = now });
        _dbContext.GeographicAudit.Add(new() { Id = Guid.NewGuid(), ActorId = actorId, ActorKind = GeographicAuditActorKind.PosAdmin, ActorRole = actorRole,
            Action = "CatalogImportStagedV1", AggregateType = nameof(GeographicCatalogRelease), AggregateId = releaseId, CatalogReleaseId = releaseId,
            Reason = $"source={m.SourceEvidenceUri}; reason={request.Reason}", EvidenceObjectKey = m.SourceEvidenceUri,
            CorrelationId = "geo-import-v1:sha256:" + m.SourceEvidenceSha256.ToLowerInvariant(), ValidationChecksumSha256 = preview.ManifestChecksumSha256,
            OccurredAtUtc = now, CreatedAtUtc = now });
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return Success(releaseId);
    }
}
