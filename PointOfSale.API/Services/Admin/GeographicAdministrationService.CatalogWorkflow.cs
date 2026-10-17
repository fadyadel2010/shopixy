using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.API.Services.Admin;

public sealed partial class GeographicAdministrationService
{
    private const string CatalogRuleVersion = "geo-initial-v1";
    private const string CatalogValidationAction = "CatalogReleaseValidatedV1";
    private static readonly long CatalogLockKey = BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes("Shopixy:canonical-geographic-catalog")));

    // All catalog staging, validation and publication share the same lock. No provider call,
    // migrations, default country, or automatic import is performed by this workflow.
    private async Task<IDbContextTransaction?> BeginCatalogTransactionAsync(CancellationToken ct)
    {
        if (!_dbContext.Database.IsRelational()) return null;
        if (_dbContext.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Catalog administration must own its transaction.");
        var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (_dbContext.Database.IsNpgsql())
                await _dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({CatalogLockKey})", ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private async Task<GeographicCatalogRelease?> LoadCatalogReleaseAsync(Guid id, CancellationToken ct)
    {
        var release = await (_dbContext.Database.IsNpgsql()
            ? _dbContext.GeographicCatalogReleases.FromSqlInterpolated(
                $"SELECT xmin, * FROM \"GeographicCatalogReleases\" WHERE \"Id\"={id} FOR UPDATE")
            : _dbContext.GeographicCatalogReleases.Where(r => r.Id == id)).SingleOrDefaultAsync(ct);
        if (release != null) await _dbContext.Entry(release).ReloadAsync(ct);
        return release;
    }

    public async Task<GeographicOperationResultDto> AddHierarchyEdgeAsync(CatalogHierarchyEdgeRequest request,
        Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        if (!ValidActor(actorId, actorRole) || !ValidReason(request.Reason)) return Fail("CatalogActorAndReasonRequired");
        if (await RequireDraftCatalogReleaseAsync(request.CatalogReleaseId, cancellationToken) == null) return Fail("CatalogDraftRequired");
        var places = await _dbContext.GeographicPlaces.AsNoTracking().Where(p => p.IntroducedCatalogReleaseId == request.CatalogReleaseId &&
            (p.Id == request.ParentPlaceId || p.Id == request.ChildPlaceId || p.Id == request.CountryPlaceId)).ToDictionaryAsync(p => p.Id, cancellationToken);
        if (!places.TryGetValue(request.ParentPlaceId, out var parent) || !places.TryGetValue(request.ChildPlaceId, out var child) ||
            !places.TryGetValue(request.CountryPlaceId, out var country) || country.PlaceKind != GeographicPlaceKind.Country ||
            country.CountryPlaceId != null || parent.Id == child.Id || child.PlaceKind == GeographicPlaceKind.Country ||
            (parent.PlaceKind == GeographicPlaceKind.Country ? parent.Id : parent.CountryPlaceId) != country.Id ||
            child.CountryPlaceId != country.Id || (int)child.PlaceKind != (int)parent.PlaceKind + 1 ||
            places.Values.Any(p => p.Status != GeographicLifecycleStatus.Active || p.RetiredCatalogReleaseId != null))
            return Fail("CatalogHierarchyInvalid");
        if (await _dbContext.GeographicPlaceHierarchyEdges.AnyAsync(e => e.IntroducedCatalogReleaseId == request.CatalogReleaseId &&
                e.ChildPlaceId == child.Id && e.IsPrimary && e.RetiredCatalogReleaseId == null, cancellationToken))
            return Fail("CatalogPrimaryParentAlreadyExists");
        var edge = new GeographicPlaceHierarchyEdge { Id = Guid.NewGuid(), ParentPlaceId = parent.Id, ChildPlaceId = child.Id,
            CountryPlaceId = country.Id, IntroducedCatalogReleaseId = request.CatalogReleaseId, CreatedAtUtc = DateTime.UtcNow };
        _dbContext.GeographicPlaceHierarchyEdges.Add(edge);
        _dbContext.GeographicCatalogReleaseEdges.Add(new GeographicCatalogReleaseEdge { CatalogReleaseId = request.CatalogReleaseId, HierarchyEdgeId = edge.Id });
        await AddAuditAsync("CatalogHierarchyStaged", nameof(GeographicPlaceHierarchyEdge), edge.Id, actorId, actorRole,
            request.Reason, catalogReleaseId: request.CatalogReleaseId, cancellationToken: cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(edge.Id);
    }

    public async Task<CatalogValidationPreviewDto> InspectCatalogDraftAsync(Guid catalogReleaseId, CancellationToken cancellationToken = default)
    {
        var release = await _dbContext.GeographicCatalogReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == catalogReleaseId, cancellationToken);
        if (release == null) return new(catalogReleaseId, false, "CatalogReleaseNotFound", null, CatalogRuleVersion);
        if (release.Status is not (GeographicReleaseStatus.Draft or GeographicReleaseStatus.Validated))
            return new(catalogReleaseId, false, "CatalogDraftRequired", null, CatalogRuleVersion);
        return await InspectInitialCatalogAsync(release, cancellationToken);
    }

    public async Task<GeographicOperationResultDto> ValidateCatalogDraftAsync(Guid catalogReleaseId, CatalogValidationRequest request,
        Guid actorId, string actorRole, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginCatalogTransactionAsync(cancellationToken);
        if (!ValidActor(actorId, actorRole) || !ValidReason(request.Reason)) return Fail("CatalogActorAndReasonRequired");
        var release = await RequireDraftCatalogReleaseAsync(catalogReleaseId, cancellationToken);
        if (release == null) return Fail("CatalogDraftRequired");
        if (await IsCatalogEditorAsync(release, actorId, cancellationToken)) return Fail("FourEyesApprovalRequired");
        if (!request.ConfirmOfficialSourceReviewed || !IsHash(request.SourceEvidenceSha256) ||
            !Uri.TryCreate(request.SourceEvidenceUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsoluteUri.Length > 200)
            return Fail("ReviewedOfficialSourceEvidenceRequired");
        var import = await _dbContext.GeographicAudit.AsNoTracking().SingleOrDefaultAsync(a => a.CatalogReleaseId == release.Id && a.Action == "CatalogImportStagedV1", cancellationToken);
        if (import != null && (import.EvidenceObjectKey != uri.AbsoluteUri ||
            import.CorrelationId != "geo-import-v1:sha256:" + request.SourceEvidenceSha256.ToLowerInvariant()))
            return Fail("CatalogImportSourceEvidenceChanged");
        var preview = await InspectInitialCatalogAsync(release, cancellationToken);
        if (!preview.Succeeded) return Fail(preview.FailureCode!);
        if (!string.Equals(preview.ContentChecksumSha256, request.ExpectedContentChecksumSha256, StringComparison.OrdinalIgnoreCase))
            return Fail("CatalogContentChanged");
        // This is an explicit operator attestation, not a network fetch or machine certification
        // that the URI/hash is official. Actual source review remains an operational prerequisite.
        release.ChecksumSha256 = preview.ContentChecksumSha256!;
        release.Status = GeographicReleaseStatus.Validated;
        _dbContext.GeographicAudit.Add(new GeographicAudit { Id = Guid.NewGuid(), ActorId = actorId,
            ActorKind = GeographicAuditActorKind.PosAdmin, ActorRole = actorRole, Action = CatalogValidationAction,
            AggregateType = nameof(GeographicCatalogRelease), AggregateId = release.Id, CatalogReleaseId = release.Id,
            Reason = $"source={uri.AbsoluteUri}; reason={request.Reason.Trim()}",
            CorrelationId = $"{CatalogRuleVersion}:sha256:{request.SourceEvidenceSha256.ToLowerInvariant()}",
            ValidationChecksumSha256 = release.ChecksumSha256, OccurredAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return Success(release.Id);
    }

    private async Task<string?> RequireInitialCatalogPublicationAsync(GeographicCatalogRelease release, Guid actorId, string actorRole, string reason, CancellationToken ct)
    {
        if (!ValidActor(actorId, actorRole) || !ValidReason(reason)) return "CatalogActorAndReasonRequired";
        if (await IsCatalogEditorAsync(release, actorId, ct)) return "FourEyesApprovalRequired";
        // Runtime selects latest Published rather than a publication pointer. Do not pretend this
        // initial-only workflow implements replacement, supersede, or rollback activation.
        if (await _dbContext.GeographicCatalogPublications.AnyAsync(ct) ||
            await _dbContext.GeographicCatalogReleases.AnyAsync(r => r.Id != release.Id &&
                r.Status == GeographicReleaseStatus.Published, ct)) return "CatalogReplacementAndRollbackWorkflowRequired";
        var validation = await _dbContext.GeographicAudit.AsNoTracking().Where(a => a.CatalogReleaseId == release.Id &&
            a.AggregateId == release.Id && a.AggregateType == nameof(GeographicCatalogRelease) && a.Action == CatalogValidationAction)
            .OrderByDescending(a => a.OccurredAtUtc).FirstOrDefaultAsync(ct);
        if (validation == null || validation.ActorKind != GeographicAuditActorKind.PosAdmin || !validation.ActorId.HasValue ||
            !ValidActor(validation.ActorId.Value, validation.ActorRole) || await IsCatalogEditorAsync(release, validation.ActorId.Value, ct) ||
            !validation.CorrelationId.StartsWith(CatalogRuleVersion + ":sha256:", StringComparison.Ordinal) ||
            !IsHash(validation.CorrelationId[(CatalogRuleVersion.Length + 8)..]) || !validation.Reason.StartsWith("source=https://", StringComparison.Ordinal))
            return "CatalogValidationEvidenceRequired";
        var preview = await InspectInitialCatalogAsync(release, ct);
        if (!preview.Succeeded) return preview.FailureCode;
        if (!string.Equals(preview.ContentChecksumSha256, release.ChecksumSha256, StringComparison.Ordinal) ||
            !string.Equals(preview.ContentChecksumSha256, validation.ValidationChecksumSha256, StringComparison.Ordinal))
            return "CatalogContentChanged";
        return null;
    }

    private Task<bool> IsCatalogEditorAsync(GeographicCatalogRelease release, Guid actor, CancellationToken ct) =>
        release.CreatedByUserId == actor ? Task.FromResult(true) : _dbContext.GeographicAudit.AnyAsync(a =>
            a.CatalogReleaseId == release.Id && a.ActorId == actor && a.Action != CatalogValidationAction, ct);

    private static bool ValidActor(Guid actor, string role) => actor != Guid.Empty && role is "Owner" or "Admin" or "Manager";
    private static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 200 && !reason.Any(char.IsControl);
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private async Task<CatalogValidationPreviewDto> InspectInitialCatalogAsync(GeographicCatalogRelease release, CancellationToken ct)
    {
        CatalogValidationPreviewDto FailPreview(string code) => new(release.Id, false, code, null, CatalogRuleVersion);
        var now = DateTime.UtcNow;
        if (release.CreatedByUserId == Guid.Empty || release.ReleaseNumber <= 0 || string.IsNullOrWhiteSpace(release.ReleaseKey) || release.ReleaseKey.Length > 64 ||
            string.IsNullOrWhiteSpace(release.SourceSummary) || release.SourceSummary.Length > 512 ||
            (release.CountryScopeIso2 != null && !Regex.IsMatch(release.CountryScopeIso2, "\\A[A-Z]{2}\\z"))) return FailPreview("CatalogSourceAndIdentityRequired");
        if (await _dbContext.GeographicPlaceAliases.AnyAsync(x => x.IntroducedCatalogReleaseId == release.Id, ct) ||
            await _dbContext.PlaceMergeHistory.AnyAsync(x => x.CatalogReleaseId == release.Id, ct) ||
            await _dbContext.PlaceDeprecations.AnyAsync(x => x.CatalogReleaseId == release.Id, ct) ||
            await _dbContext.GeographicPlaceLevels.AnyAsync(x => x.IntroducedCatalogReleaseId == release.Id, ct) ||
            await _dbContext.AddressNormalizationRules.AnyAsync(x => x.IntroducedCatalogReleaseId == release.Id, ct))
            return FailPreview("CatalogAdvancedGovernanceWorkflowRequired");
        var places = await _dbContext.GeographicPlaces.AsNoTracking().Where(x => x.IntroducedCatalogReleaseId == release.Id).OrderBy(x => x.Id).ToListAsync(ct);
        if (places.Count == 0) return FailPreview("CatalogCountriesRequired");
        if (places.Any(p => !Enum.IsDefined(p.PlaceKind) || p.Status != GeographicLifecycleStatus.Active || p.RetiredCatalogReleaseId != null ||
            p.SuccessorPlaceId != null || p.ValidFromUtc > now || (p.ValidToUtc.HasValue && p.ValidToUtc <= now) ||
            string.IsNullOrWhiteSpace(p.Source) || p.Source.Length > 128 || string.IsNullOrWhiteSpace(p.StableKey) || p.StableKey.Length > 96 ||
            string.IsNullOrWhiteSpace(p.CanonicalCode) || p.CanonicalCode.Length > 96 || p.LevelCode?.Length > 64 ||
            p.ExternalStandard?.Length > 64 || p.ExternalStandardCode?.Length > 96 ||
            p.CentroidLatitude is < -90 or > 90 || p.CentroidLongitude is < -180 or > 180 ||
            (p.PlaceKind != GeographicPlaceKind.Country && string.IsNullOrWhiteSpace(p.LevelCode))))
            return FailPreview("CatalogActivePlaceEvidenceRequired");
        if (places.GroupBy(p => p.StableKey, StringComparer.Ordinal).Any(g => g.Count() > 1) ||
            places.Where(p => p.ExternalStandard != null && p.ExternalStandardCode != null).GroupBy(p => (p.ExternalStandard, p.ExternalStandardCode)).Any(g => g.Count() > 1) ||
            places.GroupBy(p => (p.CountryPlaceId, p.PlaceKind, p.CanonicalCode)).Any(g => g.Count() > 1)) return FailPreview("CatalogDuplicatePlaceIdentity");
        var countries = places.Where(p => p.PlaceKind == GeographicPlaceKind.Country).ToDictionary(p => p.Id);
        if (countries.Count == 0) return FailPreview("CatalogCountriesRequired");
        if (countries.Values.Any(p => p.CountryPlaceId != null) || places.Any(p => p.PlaceKind != GeographicPlaceKind.Country &&
            (!p.CountryPlaceId.HasValue || !countries.ContainsKey(p.CountryPlaceId.Value)))) return FailPreview("CatalogCountryLineageInvalid");
        var placeManifest = await _dbContext.GeographicCatalogReleasePlaces.AsNoTracking().Where(x => x.CatalogReleaseId == release.Id).ToListAsync(ct);
        if (placeManifest.Count != places.Count || placeManifest.Any(m => places.All(p => p.Id != m.PlaceId || p.Status != m.PlaceStatus)))
            return FailPreview("CatalogPlaceManifestInvalid");
        var profiles = await _dbContext.GeographicCountryProfiles.AsNoTracking().Where(x => x.IntroducedCatalogReleaseId == release.Id).OrderBy(x => x.Id).ToListAsync(ct);
        if (profiles.Count != countries.Count || profiles.Select(p => p.CountryPlaceId).Distinct().Count() != countries.Count ||
            profiles.Select(p => p.Iso2).Distinct().Count() != profiles.Count || profiles.Select(p => p.Iso3).Distinct().Count() != profiles.Count ||
            profiles.Any(p => !countries.TryGetValue(p.CountryPlaceId, out var country) || !p.IsActive || p.RetiredCatalogReleaseId != null ||
                !Regex.IsMatch(p.Iso2, "\\A[A-Z]{2}\\z") || !Regex.IsMatch(p.Iso3, "\\A[A-Z]{3}\\z") || p.Iso2 != country.CanonicalCode ||
                (release.CountryScopeIso2 != null && release.CountryScopeIso2 != p.Iso2) || p.SupportedLanguageTags == null ||
                !p.SupportedLanguageTags.Contains(p.DefaultLanguageTag) || p.SupportedLanguageTags.Any(l => string.IsNullOrWhiteSpace(l) || l.Length > 35) ||
                p.RequiredLevelCodesForDelivery == null || p.RequiredLevelCodesForOperations == null))
            return FailPreview("CatalogCountryProfileInvalid");
        foreach (var profile in profiles)
        {
            if (!Enum.IsDefined(profile.PostalCodePolicy) || (profile.PostalCodePolicy is GeographicPostalCodePolicy.Required or GeographicPostalCodePolicy.Optional &&
                string.IsNullOrWhiteSpace(profile.PostalCodeRegex))) return FailPreview("CatalogPostalRuleInvalid");
            if (!string.IsNullOrWhiteSpace(profile.PostalCodeRegex))
            {
                try { _ = new Regex(profile.PostalCodeRegex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
                catch (ArgumentException) { return FailPreview("CatalogPostalRuleInvalid"); }
            }
        }
        var edges = await _dbContext.GeographicPlaceHierarchyEdges.AsNoTracking().Where(x => x.IntroducedCatalogReleaseId == release.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var byId = places.ToDictionary(p => p.Id);
        if (edges.Any(e => e.RetiredCatalogReleaseId != null || !e.IsPrimary || e.Depth != 1 || e.ParentPlaceId == e.ChildPlaceId ||
            !byId.ContainsKey(e.ParentPlaceId) || !byId.ContainsKey(e.ChildPlaceId) || !countries.ContainsKey(e.CountryPlaceId)) ||
            edges.GroupBy(e => e.ChildPlaceId).Any(g => g.Count() != 1)) return FailPreview("CatalogHierarchyInvalid");
        var parents = edges.ToDictionary(e => e.ChildPlaceId);
        foreach (var place in places.Where(p => p.PlaceKind != GeographicPlaceKind.Country))
        {
            var seen = new HashSet<Guid>(); var current = place.Id;
            while (!countries.ContainsKey(current))
            {
                if (!seen.Add(current)) return FailPreview("CatalogHierarchyCycle");
                if (!parents.TryGetValue(current, out var edge)) return FailPreview("CatalogHierarchyIncomplete");
                current = edge.ParentPlaceId;
            }
            if (current != place.CountryPlaceId) return FailPreview("CatalogCountryLineageInvalid");
            var direct = parents[place.Id]; var parent = byId[direct.ParentPlaceId];
            if (direct.CountryPlaceId != place.CountryPlaceId || (int)place.PlaceKind != (int)parent.PlaceKind + 1)
                return FailPreview("CatalogHierarchyInvalid");
        }
        if (countries.Keys.Any(c => parents.ContainsKey(c) || !places.Any(p => p.CountryPlaceId == c && p.PlaceKind == GeographicPlaceKind.Locality)))
            return FailPreview("CatalogHierarchyIncomplete");
        var edgeManifest = await _dbContext.GeographicCatalogReleaseEdges.AsNoTracking().Where(x => x.CatalogReleaseId == release.Id).ToListAsync(ct);
        if (edgeManifest.Count != edges.Count || edgeManifest.Any(m => edges.All(e => e.Id != m.HierarchyEdgeId))) return FailPreview("CatalogEdgeManifestInvalid");
        foreach (var place in places.Where(p => p.PlaceKind == GeographicPlaceKind.Locality))
        {
            var profile = profiles.Single(p => p.CountryPlaceId == place.CountryPlaceId);
            var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var current = place.Id;
            while (!countries.ContainsKey(current)) { if (byId[current].LevelCode != null) levels.Add(byId[current].LevelCode!); current = parents[current].ParentPlaceId; }
            if (countries[current].LevelCode != null) levels.Add(countries[current].LevelCode!);
            if (profile.RequiredLevelCodesForDelivery.Concat(profile.RequiredLevelCodesForOperations).Any(l => !levels.Contains(l)))
                return FailPreview("CatalogRequiredLevelMissing");
        }
        var names = await _dbContext.GeographicPlaceNames.AsNoTracking().Where(x => x.IntroducedCatalogReleaseId == release.Id).OrderBy(x => x.Id).ToListAsync(ct);
        if (names.Any(n => !byId.ContainsKey(n.PlaceId) || !Enum.IsDefined(n.NameKind) || n.RetiredCatalogReleaseId != null || n.ValidFromUtc > now ||
            (n.ValidToUtc.HasValue && n.ValidToUtc <= now) || string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.NormalizedName) ||
            string.IsNullOrWhiteSpace(n.SearchName) || string.IsNullOrWhiteSpace(n.LanguageTag) || n.LanguageTag.Length > 35)) return FailPreview("CatalogLocalizationInvalid");
        foreach (var place in places)
        {
            var profile = profiles.Single(p => p.CountryPlaceId == (place.PlaceKind == GeographicPlaceKind.Country ? place.Id : place.CountryPlaceId));
            if (profile.SupportedLanguageTags.Any(language => names.Count(n => n.PlaceId == place.Id && n.LanguageTag == language && n.IsPreferred) != 1))
                return FailPreview("CatalogLocalizationRequired");
        }
        var content = JsonSerializer.Serialize(new { version = CatalogRuleVersion, release.Id, release.ReleaseNumber, release.ReleaseKey, release.CountryScopeIso2, release.SourceSummary,
            places = places.Select(p => new { p.Id, p.StableKey, p.PlaceKind, p.CountryPlaceId, p.LevelCode, p.CanonicalCode, p.ExternalStandard, p.ExternalStandardCode,
                p.Status, p.ValidFromUtc, p.ValidToUtc, p.Source, p.CentroidLatitude, p.CentroidLongitude, p.TimeZoneId, p.CreatedByUserId, p.UpdatedByUserId, p.UpdatedAtUtc }),
            edges = edges.Select(e => new { e.Id, e.ParentPlaceId, e.ChildPlaceId, e.CountryPlaceId, e.Depth, e.IsPrimary }),
            profiles = profiles.Select(p => new { p.Id, p.CountryPlaceId, p.Iso2, p.Iso3, p.DefaultLanguageTag, p.SupportedLanguageTags,
                p.RequiredLevelCodesForDelivery, p.RequiredLevelCodesForOperations, p.PostalCodePolicy, p.PostalCodeRegex,
                p.RequiresStreetLine1, p.AllowsCoordinates, p.RequiresCoordinateProvenance, p.OfflinePackageEnabled, p.IsActive }),
            names = names.Select(n => new { n.Id, n.PlaceId, n.LanguageTag, n.ScriptCode, n.NameKind, n.Name, n.NormalizedName, n.SearchName, n.DisplayPriority, n.IsPreferred, n.ValidFromUtc, n.ValidToUtc }) });
        return new(release.Id, true, null, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant(), CatalogRuleVersion);
    }
}
