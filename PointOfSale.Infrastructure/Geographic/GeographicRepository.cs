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
            candidate.AddressFingerprintSha256.Length != 64)
        {
            return Fail("AddressFingerprintRequired");
        }

        if ((candidate.Latitude.HasValue || candidate.Longitude.HasValue) &&
            (!candidate.Latitude.HasValue || !candidate.Longitude.HasValue || string.IsNullOrWhiteSpace(candidate.CoordinateSource)))
        {
            return Fail("CoordinateProvenanceRequired");
        }

        var release = await _catalogService.GetActivePublishedCatalogReleaseAsync(null, cancellationToken);
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

        var evidence = new AddressAdmissionEvidence
        {
            Id = Guid.NewGuid(),
            TenantId = candidate.TenantId,
            OwnerDomain = candidate.OwnerDomain,
            OwnerId = candidate.OwnerId,
            AddressFingerprintSha256 = candidate.AddressFingerprintSha256,
            Status = AddressAdmissionStatus.Admitted,
            CatalogReleaseId = release.Id,
            CountryPlaceId = candidate.Lineage.CountryPlaceId,
            AdministrativeAreaPlaceId = candidate.Lineage.AdministrativeAreaPlaceId,
            LocalityPlaceId = candidate.Lineage.LocalityPlaceId,
            DistrictPlaceId = candidate.Lineage.DistrictPlaceId,
            ZonePlaceId = candidate.Lineage.ZonePlaceId,
            AdmittedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repository.AddAddressAdmissionEvidenceAsync(evidence, cancellationToken);
        return new GeographicAdmissionResult(true, evidence.Id, release.Id, null);
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
                      publication.ActivatedAtUtc <= now &&
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
                orderby publication.ActivatedAtUtc descending, publication.PublishedAtUtc descending
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
