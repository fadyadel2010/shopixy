using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Geographic;

public sealed class GeographicRepository : IGeographicRepository
{
    private readonly ApplicationDbContext _dbContext;

    public GeographicRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<GeographicCatalogRelease> CatalogReleases => _dbContext.GeographicCatalogReleases.AsNoTracking();
    public IQueryable<GeographicPlace> Places => _dbContext.GeographicPlaces.AsNoTracking();
    public IQueryable<GeographicPlaceHierarchyEdge> HierarchyEdges => _dbContext.GeographicPlaceHierarchyEdges.AsNoTracking();
    public IQueryable<GeographicCountryProfile> CountryProfiles => _dbContext.GeographicCountryProfiles.AsNoTracking();
    public IQueryable<GeographicCatalogPublication> CatalogPublications => _dbContext.GeographicCatalogPublications.AsNoTracking();
    public IQueryable<GeographicCatalogReleasePlace> CatalogReleasePlaces => _dbContext.GeographicCatalogReleasePlaces.AsNoTracking();
    public IQueryable<GeographicCatalogReleaseEdge> CatalogReleaseEdges => _dbContext.GeographicCatalogReleaseEdges.AsNoTracking();
    public IQueryable<CarrierProvider> CarrierProviders => _dbContext.CarrierProviders.AsNoTracking();
    public IQueryable<CarrierGeographicMappingRelease> MappingReleases => _dbContext.CarrierGeographicMappingReleases.AsNoTracking();
    public IQueryable<CarrierGeographicMappingPublication> MappingPublications => _dbContext.CarrierGeographicMappingPublications.AsNoTracking();
    public IQueryable<CarrierGeographicMapping> Mappings => _dbContext.CarrierGeographicMappings.AsNoTracking();

    public async Task AddAddressAdmissionEvidenceAsync(
        AddressAdmissionEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        await _dbContext.AddressAdmissionEvidence.AddAsync(evidence, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GeographicCatalogService : IGeographicCatalogService
{
    private readonly IGeographicRepository _repository;

    public GeographicCatalogService(IGeographicRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<GeographicPlaceReadModel>> SearchPublishedPlacesAsync(
        GeographicCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LanguageTag) ||
            string.IsNullOrWhiteSpace(request.SearchText))
        {
            return Array.Empty<GeographicPlaceReadModel>();
        }

        var release = request.CatalogReleaseId.HasValue
            ? await _repository.CatalogReleases
                .SingleOrDefaultAsync(release =>
                    release.Id == request.CatalogReleaseId.Value &&
                    release.Status == GeographicReleaseStatus.Published,
                    cancellationToken)
            : await GetActivePublishedCatalogReleaseAsync(null, cancellationToken);

        if (release is null)
        {
            return Array.Empty<GeographicPlaceReadModel>();
        }

        var search = request.SearchText.Trim();
        var query = _repository.Places
            .Where(place => place.CountryPlaceId == request.CountryPlaceId || place.Id == request.CountryPlaceId)
            .Where(place => place.Status == GeographicLifecycleStatus.Active)
            .Where(place => place.IntroducedCatalogReleaseId == release.Id && place.RetiredCatalogReleaseId == null);

        if (request.ParentPlaceId.HasValue)
        {
            var childIds = _repository.HierarchyEdges
                .Where(edge =>
                    edge.ParentPlaceId == request.ParentPlaceId.Value &&
                    edge.IntroducedCatalogReleaseId == release.Id &&
                    edge.RetiredCatalogReleaseId == null)
                .Select(edge => edge.ChildPlaceId);

            query = query.Where(place => childIds.Contains(place.Id));
        }

        var results =
            from place in query
            from name in place.Names
            where name.LanguageTag == request.LanguageTag &&
                  name.RetiredCatalogReleaseId == null &&
                  name.SearchName.Contains(search)
            orderby name.IsPreferred descending, name.DisplayPriority, name.Name
            select new GeographicPlaceReadModel(
                place.Id,
                place.PlaceKind,
                request.ParentPlaceId,
                place.CanonicalCode,
                name.Name,
                release.Id);

        return await results.Take(50).ToListAsync(cancellationToken);
    }

    public Task<GeographicCatalogRelease?> GetActivePublishedCatalogReleaseAsync(
        string? countryIso2,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(countryIso2) ? null : countryIso2.Trim().ToUpperInvariant();
        return _repository.CatalogReleases
            .Where(release => release.Status == GeographicReleaseStatus.Published)
            .Where(release => normalized == null || release.CountryScopeIso2 == null || release.CountryScopeIso2 == normalized)
            .OrderByDescending(release => release.PublishedAtUtc)
            .ThenByDescending(release => release.ReleaseNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public sealed class GeographicResolver : IGeographicResolver, IAddressAdmissionService
{
    private readonly IGeographicCatalogService _catalogService;
    private readonly IGeographicRepository _repository;

    public GeographicResolver(IGeographicCatalogService catalogService, IGeographicRepository repository)
    {
        _catalogService = catalogService;
        _repository = repository;
    }

    public Task<GeographicAdmissionResult> AdmitAsync(
        GeographicAddressCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        return ValidatePublishedLineageAsync(candidate, cancellationToken);
    }

    public async Task<GeographicAdmissionResult> ValidatePublishedLineageAsync(
        GeographicAddressCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Lineage.CountryPlaceId == Guid.Empty)
        {
            return Fail("CountryPlaceIdRequired");
        }

        if (string.IsNullOrWhiteSpace(candidate.AddressFingerprintSha256) ||
            candidate.AddressFingerprintSha256.Length != 64 || !candidate.AddressFingerprintSha256.All(Uri.IsHexDigit))
        {
            return Fail("AddressFingerprintRequired");
        }

        if ((candidate.Latitude.HasValue || candidate.Longitude.HasValue) &&
            (!candidate.Latitude.HasValue || !candidate.Longitude.HasValue || string.IsNullOrWhiteSpace(candidate.CoordinateSource)))
        {
            return Fail("CoordinateProvenanceRequired");
        }

        var countryScope = candidate.CanonicalInputs?.SubmittedCountryIso2;
        if (candidate.CanonicalInputs != null && countryScope == null)
            countryScope = await _repository.Places.Where(p => p.Id == candidate.Lineage.CountryPlaceId && p.PlaceKind == GeographicPlaceKind.Country)
                .Select(p => p.CanonicalCode).SingleOrDefaultAsync(cancellationToken);
        var release = await _catalogService.GetActivePublishedCatalogReleaseAsync(countryScope, cancellationToken);
        if (release is null)
        {
            return Fail("PublishedCatalogReleaseRequired");
        }

        var requestedIds = new[]
            {
                candidate.Lineage.CountryPlaceId,
                candidate.Lineage.AdministrativeAreaPlaceId,
                candidate.Lineage.LocalityPlaceId,
                candidate.Lineage.DistrictPlaceId,
                candidate.Lineage.ZonePlaceId
            }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray();

        var activeCount = await _repository.Places
            .CountAsync(place =>
                requestedIds.Contains(place.Id) &&
                place.Status == GeographicLifecycleStatus.Active &&
                place.IntroducedCatalogReleaseId == release.Id,
                cancellationToken);

        if (activeCount != requestedIds.Length)
        {
            return Fail("UnknownOrUnpublishedPlace");
        }

        if (candidate.OwnerDomain is AddressAdmissionOwnerDomain.Warehouse or AddressAdmissionOwnerDomain.Checkout)
        {
            var failure = await ValidateWarehouseAddressAsync(candidate, release, cancellationToken);
            if (failure != null) return Fail(failure);
        }

        var admittedAt = DateTime.UtcNow;
        admittedAt = new DateTime(admittedAt.Ticks - admittedAt.Ticks % 10, DateTimeKind.Utc);
        var evidence = new AddressAdmissionEvidence
        {
            Id = Guid.NewGuid(),
            TenantId = candidate.TenantId,
            OwnerDomain = candidate.OwnerDomain,
            OwnerId = candidate.OwnerId,
            AddressFingerprintSha256 = candidate.AddressFingerprintSha256,
            PostalCode = candidate.PostalCode?.Trim(),
            NormalizationRuleVersion = candidate.CanonicalInputs != null ? CanonicalAddressEvidence.ValidatorVersion : candidate.OwnerDomain == AddressAdmissionOwnerDomain.Warehouse
                ? WarehouseAddressFingerprint.Version : null,
            Status = AddressAdmissionStatus.Admitted,
            CatalogReleaseId = release.Id,
            CountryPlaceId = candidate.Lineage.CountryPlaceId,
            AdministrativeAreaPlaceId = candidate.Lineage.AdministrativeAreaPlaceId,
            LocalityPlaceId = candidate.Lineage.LocalityPlaceId,
            DistrictPlaceId = candidate.Lineage.DistrictPlaceId,
            ZonePlaceId = candidate.Lineage.ZonePlaceId,
            AdmittedAtUtc = admittedAt,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (candidate.CanonicalInputs != null)
        {
            try
            {
                evidence.CanonicalAddressEvidenceJson = await CanonicalAddressEvidence.CaptureAsync(
                    candidate, evidence, release, _repository, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return Fail(ex.Message);
            }
        }

        await _repository.AddAddressAdmissionEvidenceAsync(evidence, cancellationToken);
        return new GeographicAdmissionResult(true, evidence.Id, release.Id, null);
    }

    private async Task<string?> ValidateWarehouseAddressAsync(
        GeographicAddressCandidate candidate, GeographicCatalogRelease release, CancellationToken ct)
    {
        if (!candidate.TenantId.HasValue || candidate.TenantId == Guid.Empty ||
            !candidate.OwnerId.HasValue || candidate.OwnerId == Guid.Empty)
            return "WarehouseOwnershipRequired";
        if (!candidate.Lineage.AdministrativeAreaPlaceId.HasValue || !candidate.Lineage.LocalityPlaceId.HasValue)
            return "WarehouseAdministrativeAreaAndLocalityRequired";

        var selections = new (Guid? Id, GeographicPlaceKind Kind)[]
        {
            (candidate.Lineage.CountryPlaceId, GeographicPlaceKind.Country),
            (candidate.Lineage.AdministrativeAreaPlaceId, GeographicPlaceKind.AdministrativeArea),
            (candidate.Lineage.LocalityPlaceId, GeographicPlaceKind.Locality),
            (candidate.Lineage.DistrictPlaceId, GeographicPlaceKind.District),
            (candidate.Lineage.ZonePlaceId, GeographicPlaceKind.Zone)
        };
        var ids = selections.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        var now = DateTime.UtcNow;
        var places = await _repository.Places.Where(p => ids.Contains(p.Id) &&
            p.Status == GeographicLifecycleStatus.Active && p.IntroducedCatalogReleaseId == release.Id &&
            p.RetiredCatalogReleaseId == null && p.ValidFromUtc <= now && (p.ValidToUtc == null || p.ValidToUtc > now))
            .ToDictionaryAsync(p => p.Id, ct);
        foreach (var (id, kind) in selections.Where(x => x.Id.HasValue))
        {
            if (!places.TryGetValue(id!.Value, out var place) || place.PlaceKind != kind ||
                string.IsNullOrWhiteSpace(place.CanonicalCode) ||
                (kind != GeographicPlaceKind.Country && place.CountryPlaceId != candidate.Lineage.CountryPlaceId))
                return "WarehousePlaceKindOrCountryInvalid";
        }
        for (var i = 1; i < ids.Length; i++)
        {
            var parent = ids[i - 1]; var child = ids[i];
            if (!await _repository.HierarchyEdges.AnyAsync(e => e.ParentPlaceId == parent && e.ChildPlaceId == child &&
                e.CountryPlaceId == candidate.Lineage.CountryPlaceId && e.IsPrimary && e.Depth == 1 &&
                e.IntroducedCatalogReleaseId == release.Id && e.RetiredCatalogReleaseId == null, ct))
                return "WarehouseHierarchyInvalid";
        }
        var profiles = await _repository.CountryProfiles.Where(p => p.CountryPlaceId == candidate.Lineage.CountryPlaceId &&
            p.IsActive && p.IntroducedCatalogReleaseId == release.Id && p.RetiredCatalogReleaseId == null).Take(2).ToListAsync(ct);
        if (profiles.Count != 1 || profiles[0].Iso2.Length != 2 ||
            places[candidate.Lineage.CountryPlaceId].CanonicalCode != profiles[0].Iso2 ||
            (release.CountryScopeIso2 != null && release.CountryScopeIso2 != profiles[0].Iso2))
            return "WarehouseCountryProfileRequired";
        var profile = profiles[0];
        if (!Enum.IsDefined(profile.PostalCodePolicy)) return "WarehousePostalPolicyUnsupported";
        var postal = candidate.PostalCode?.Trim();
        if (profile.PostalCodePolicy == GeographicPostalCodePolicy.Required && string.IsNullOrWhiteSpace(postal))
            return "PostalCodeRequired";
        if (profile.PostalCodePolicy is GeographicPostalCodePolicy.Forbidden or GeographicPostalCodePolicy.NotUsed &&
            !string.IsNullOrWhiteSpace(postal)) return "PostalCodeForbidden";
        if (postal != null && (postal.Length > 32 || postal.Any(char.IsControl))) return "PostalCodeInvalid";
        if (!string.IsNullOrWhiteSpace(postal))
        {
            if (string.IsNullOrWhiteSpace(profile.PostalCodeRegex)) return "WarehousePostalRuleRequired";
            try
            {
                var match = Regex.Match(postal, profile.PostalCodeRegex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
                if (!match.Success || match.Index != 0 || match.Length != postal.Length) return "PostalCodeInvalid";
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            { return "WarehousePostalRuleInvalid"; }
        }
        if (profile.RequiredLevelCodesForDelivery.Concat(profile.RequiredLevelCodesForOperations).Any(required =>
            places.Values.All(p => !string.Equals(p.LevelCode, required, StringComparison.OrdinalIgnoreCase))))
            return "RequiredAdministrativeLevelMissing";
        return null;
    }

    private static GeographicAdmissionResult Fail(string failureCode)
    {
        return new GeographicAdmissionResult(false, null, null, failureCode);
    }
}

public sealed class CarrierGeographicMappingService : ICarrierGeographicMappingService
{
    private readonly IGeographicRepository _repository;

    public CarrierGeographicMappingService(IGeographicRepository repository)
    {
        _repository = repository;
    }

    public async Task<CarrierGeographicMappingResult> ResolvePublishedMappingAsync(
        CarrierGeographicMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CarrierCode))
        {
            return Fail("CarrierCodeRequired");
        }

        var provider = await _repository.CarrierProviders
            .SingleOrDefaultAsync(
                provider => provider.CarrierCode.ToLower() == request.CarrierCode.Trim().ToLowerInvariant() && provider.IsActive,
                cancellationToken);

        if (provider is null)
        {
            return Fail("UnknownCarrierProvider");
        }

        var now = DateTime.UtcNow;
        var release = await (
                from publication in _repository.MappingPublications
                join candidate in _repository.MappingReleases
                    on publication.MappingReleaseId equals candidate.Id
                where publication.ActivatedAtUtc != null &&
                      publication.SupersededAtUtc == null &&
                      publication.ActivatedAtUtc <= now &&
                      !_repository.MappingPublications.Any(later => later.ActivatedAtUtc <= now && later.ActivatedAtUtc > publication.ActivatedAtUtc &&
                          _repository.MappingReleases.Any(latest => latest.Id == later.MappingReleaseId && latest.CarrierProviderId == candidate.CarrierProviderId &&
                              latest.Environment == candidate.Environment && latest.AccountScopeKind == candidate.AccountScopeKind &&
                              latest.AccountScopeId == candidate.AccountScopeId && latest.TenantId == candidate.TenantId)) &&
                      candidate.CarrierProviderId == provider.Id &&
                      candidate.Environment == request.Environment &&
                      candidate.AccountScopeKind == request.AccountScopeKind &&
                      candidate.AccountScopeId == request.AccountScopeId &&
                      candidate.TenantId == request.TenantId &&
                      candidate.CatalogReleaseId == request.CatalogReleaseId &&
                      candidate.Status == GeographicReleaseStatus.Published &&
                      candidate.PublishedAtUtc != null &&
                      candidate.EffectiveFromUtc <= now &&
                      (candidate.EffectiveToUtc == null || candidate.EffectiveToUtc > now)
                orderby publication.ActivatedAtUtc descending, publication.PublishedAtUtc descending, candidate.ReleaseNumber descending
                select candidate)
            .FirstOrDefaultAsync(cancellationToken);

        if (release is null)
        {
            return Fail("PublishedMappingReleaseRequired");
        }

        if (release.FreshnessExpiresAtUtc.HasValue && release.FreshnessExpiresAtUtc <= DateTime.UtcNow)
        {
            return Fail("StaleMappingRelease");
        }

        var countryProfile = await _repository.CountryProfiles
            .SingleOrDefaultAsync(
                profile =>
                    profile.CountryPlaceId == request.Lineage.CountryPlaceId &&
                    profile.IsActive &&
                    profile.IntroducedCatalogReleaseId == release.CatalogReleaseId &&
                    profile.RetiredCatalogReleaseId == null,
                cancellationToken);
        if (countryProfile is null)
        {
            return Fail("UnsupportedCountry");
        }

        if (countryProfile.RequiresStreetLine1 && !request.HasStreetLine1)
        {
            return Fail("StreetLine1Required");
        }

        var postalCode = request.PostalCode?.Trim();
        if (countryProfile.PostalCodePolicy == GeographicPostalCodePolicy.Required &&
            string.IsNullOrWhiteSpace(postalCode))
        {
            return Fail("PostalCodeRequired");
        }

        if (countryProfile.PostalCodePolicy == GeographicPostalCodePolicy.Forbidden &&
            !string.IsNullOrWhiteSpace(postalCode))
        {
            return Fail("PostalCodeForbidden");
        }

        if (!string.IsNullOrWhiteSpace(postalCode) &&
            !string.IsNullOrWhiteSpace(countryProfile.PostalCodeRegex) &&
            !Regex.IsMatch(postalCode, countryProfile.PostalCodeRegex, RegexOptions.CultureInvariant))
        {
            return Fail("PostalCodeInvalid");
        }

        var lineageIds = new[]
            {
                request.Lineage.CountryPlaceId,
                request.Lineage.AdministrativeAreaPlaceId,
                request.Lineage.LocalityPlaceId,
                request.Lineage.DistrictPlaceId,
                request.Lineage.ZonePlaceId
            }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray();

        var selectedLevelCodes = await _repository.Places
            .Where(place =>
                lineageIds.Contains(place.Id) &&
                place.Status == GeographicLifecycleStatus.Active &&
                place.IntroducedCatalogReleaseId == release.CatalogReleaseId &&
                place.RetiredCatalogReleaseId == null)
            .Select(place => place.LevelCode)
            .Where(levelCode => levelCode != null)
            .ToListAsync(cancellationToken);
        if (countryProfile.RequiredLevelCodesForDelivery.Any(
                required => selectedLevelCodes.All(
                    selected => !string.Equals(selected, required, StringComparison.OrdinalIgnoreCase))))
        {
            return Fail("RequiredAdministrativeLevelMissing");
        }

        var mappingsQuery = _repository.Mappings
            .Where(mapping =>
                mapping.MappingReleaseId == release.Id &&
                mapping.CarrierProviderId == provider.Id &&
                mapping.MappingStatus == CarrierGeographicMappingStatus.Active &&
                (mapping.VerificationStatus == CarrierGeographicVerificationStatus.Reviewed ||
                 mapping.VerificationStatus == CarrierGeographicVerificationStatus.ManuallyApproved) &&
                mapping.CountryPlaceId == request.Lineage.CountryPlaceId &&
                mapping.RequiredForOperation == request.RequiredForOperation &&
                mapping.EffectiveFromUtc <= DateTime.UtcNow &&
                (mapping.EffectiveToUtc == null || mapping.EffectiveToUtc > DateTime.UtcNow) &&
                lineageIds.Contains(mapping.CanonicalPlaceId) &&
                (request.RequiredProviderPlaceTypes.Length == 0 ||
                 request.RequiredProviderPlaceTypes.Contains(mapping.ProviderPlaceType)));

        var mappings = await mappingsQuery
            .Select(mapping => new CarrierGeographicMappingItem(
                mapping.Id,
                mapping.MappingGroupId,
                mapping.CanonicalPlaceId,
                mapping.CountryPlaceId,
                mapping.ProviderPlaceType,
                mapping.ProviderPlaceId,
                mapping.ProviderPlaceCode,
                mapping.ProviderDisplayName,
                mapping.ProviderParentMappingId))
            .ToListAsync(cancellationToken);

        if (provider.RequiresMappingForDelivery && mappings.Count == 0)
        {
            return Fail("MissingRequiredProviderMapping");
        }

        if (request.RequiredProviderPlaceTypes.Any(required => mappings.All(mapping => mapping.ProviderPlaceType != required)))
        {
            return Fail("MissingRequiredProviderMapping");
        }

        if (mappings.GroupBy(mapping => mapping.ProviderPlaceType, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            return Fail("AmbiguousProviderMapping");
        }

        if (mappings.Any(mapping => mapping.CountryPlaceId != request.Lineage.CountryPlaceId))
        {
            return Fail("CrossCountryProviderMapping");
        }

        return new CarrierGeographicMappingResult(
            true,
            release.Id,
            release.ReleaseNumber,
            release.ReleaseKey,
            release.Environment,
            release.AccountScopeKind,
            release.AccountScopeId,
            release.FreshnessExpiresAtUtc,
            countryProfile.Iso2,
            mappings,
            null);
    }

    private static CarrierGeographicMappingResult Fail(string failureCode)
    {
        return new CarrierGeographicMappingResult(false, null, null, null, null, null, null, null, null, Array.Empty<CarrierGeographicMappingItem>(), failureCode);
    }
}

public sealed class GeographicPublicationService : IGeographicPublicationService
{
    public Task<GeographicPublicationResult> PublishCatalogReleaseAsync(
        Guid catalogReleaseId,
        Guid actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GeographicPublicationResult(false, null, null, "PublicationWorkflowNotImplementedInFoundation"));
    }

    public Task<GeographicPublicationResult> RollbackCatalogReleaseAsync(
        Guid targetCatalogReleaseId,
        Guid actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GeographicPublicationResult(false, null, null, "RollbackWorkflowNotImplementedInFoundation"));
    }
}

public sealed class GeographicSynchronizationService : IGeographicSynchronizationService
{
    public Task<CarrierGeographicSyncRun> RecordSyncRunAsync(
        CarrierGeographicSyncRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = new CarrierGeographicSyncRun
        {
            Id = Guid.NewGuid(),
            CarrierProviderId = request.CarrierProviderId,
            Environment = request.Environment,
            AccountScopeKind = request.AccountScopeKind,
            AccountScopeId = request.AccountScopeId,
            TenantId = request.TenantId,
            SourceKind = request.SourceKind,
            SourceUri = request.SourceUri,
            SourceVersion = request.SourceVersion,
            SourceHashSha256 = request.SourceHashSha256,
            StartedAtUtc = DateTime.UtcNow,
            Status = CarrierGeographicSyncStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        return Task.FromResult(run);
    }
}
