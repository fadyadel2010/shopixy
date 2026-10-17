using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces.Geographic;

namespace PointOfSale.Infrastructure.Geographic;

public sealed record CanonicalPlaceEvidence(Guid Id, GeographicPlaceKind Kind, string Code, string? Level, string Source);
public sealed record CanonicalAddressPayload(
    string Kind, int Version, Guid EvidenceId, Guid TenantId, AddressAdmissionOwnerDomain OwnerDomain, Guid OwnerId,
    DateTime AdmittedAtUtc, string ValidatorVersion, string AddressFingerprintSha256,
    CanonicalAddressInputs Inputs, string InputFingerprintSha256,
    GeographicPlaceLineage Lineage, string CountryIso2, Guid CountryProfileId,
    Guid CatalogReleaseId, long ReleaseNumber, string ReleaseKey, string ReleaseChecksum, string SourceSummary,
    Guid PublicationId, DateTime PublishedAtUtc, DateTime ActivatedAtUtc,
    CanonicalPlaceEvidence[] Places, Guid[] HierarchyEdgeIds, string[] RequiredLevels,
    string? SubmittedPostalCode, string? NormalizedPostalCode, GeographicPostalCodePolicy PostalPolicy,
    string? PostalRegex, string PostalDisposition, string PostalValidationScope, string? PayloadSha256);

/// <summary>Immutable canonical input evidence. Integrity hashes are not signatures or account entitlement.</summary>
public static class CanonicalAddressEvidence
{
    public const string ValidatorVersion = "canonical-address:v1:trim-utf8-sha256";
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static async Task<string> CaptureAsync(GeographicAddressCandidate candidate, AddressAdmissionEvidence evidence,
        GeographicCatalogRelease release, IGeographicRepository repository, CancellationToken ct)
    {
        var inputs = candidate.CanonicalInputs ?? throw new ArgumentException("CanonicalAddressInputsRequired");
        if (!candidate.TenantId.HasValue || candidate.TenantId == Guid.Empty || !candidate.OwnerId.HasValue || candidate.OwnerId == Guid.Empty ||
            candidate.OwnerDomain is not (AddressAdmissionOwnerDomain.Warehouse or AddressAdmissionOwnerDomain.Checkout))
            throw new ArgumentException("CanonicalAddressOwnershipRequired");
        inputs = Normalize(inputs);
        ValidateInputs(inputs, candidate.OwnerDomain);
        if (candidate.AddressFingerprintSha256 != ComputeInputFingerprint(candidate.TenantId.Value, candidate.OwnerDomain,
            candidate.OwnerId.Value, candidate.Lineage, inputs, candidate.PostalCode))
            throw new ArgumentException("CanonicalAddressFingerprintMismatch");
        var profile = await repository.CountryProfiles.SingleAsync(p => p.CountryPlaceId == candidate.Lineage.CountryPlaceId &&
            p.IsActive && p.IntroducedCatalogReleaseId == release.Id && p.RetiredCatalogReleaseId == null, ct);
        var iso = profile.Iso2.Trim().ToUpperInvariant();
        if (!RecognizedCountry(iso) || (inputs.SubmittedCountryIso2 != null && inputs.SubmittedCountryIso2 != iso) ||
            (candidate.OwnerDomain == AddressAdmissionOwnerDomain.Checkout && inputs.SubmittedCountryIso2 == null))
            throw new ArgumentException("CanonicalCountryEvidenceConflictingOrMissing");
        var now = evidence.AdmittedAtUtc;
        var publication = await repository.CatalogPublications.Where(p => p.CatalogReleaseId == release.Id && p.ActivatedAtUtc != null &&
            p.ActivatedAtUtc <= now && p.SupersededAtUtc == null).OrderByDescending(p => p.ActivatedAtUtc).FirstOrDefaultAsync(ct);
        if (publication == null || release.Status != GeographicReleaseStatus.Published || release.PublishedAtUtc == null ||
            release.PublishedAtUtc > now || publication.PublishedAtUtc > now ||
            !HashValid(release.ChecksumSha256) || string.IsNullOrWhiteSpace(release.SourceSummary))
            throw new ArgumentException("CanonicalPublishedLineageRequired");
        var ids = new Guid?[] { candidate.Lineage.CountryPlaceId, candidate.Lineage.AdministrativeAreaPlaceId,
            candidate.Lineage.LocalityPlaceId, candidate.Lineage.DistrictPlaceId, candidate.Lineage.ZonePlaceId }
            .Where(id => id.HasValue).Select(id => id!.Value).ToArray();
        var places = await repository.Places.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (await repository.CatalogReleasePlaces.CountAsync(p => p.CatalogReleaseId == release.Id && ids.Contains(p.PlaceId) &&
            p.PlaceStatus == GeographicLifecycleStatus.Active, ct) != ids.Length)
            throw new ArgumentException("CanonicalPublishedMembershipRequired");
        if (ids.Any(id => !places.ContainsKey(id) || string.IsNullOrWhiteSpace(places[id].Source)))
            throw new ArgumentException("CanonicalPlaceSourceRequired");
        var edges = new List<Guid>();
        for (var i = 1; i < ids.Length; i++)
        {
            var parent = ids[i - 1]; var child = ids[i];
            var edgeIds = await repository.HierarchyEdges.Where(e => e.ParentPlaceId == parent && e.ChildPlaceId == child &&
                e.CountryPlaceId == candidate.Lineage.CountryPlaceId && e.IsPrimary && e.Depth == 1 &&
                e.IntroducedCatalogReleaseId == release.Id && e.RetiredCatalogReleaseId == null).Select(e => e.Id).Take(2).ToListAsync(ct);
            if (edgeIds.Count != 1) throw new ArgumentException("CanonicalHierarchyAmbiguousOrMissing");
            edges.Add(edgeIds[0]);
        }
        if (await repository.CatalogReleaseEdges.CountAsync(e => e.CatalogReleaseId == release.Id && edges.Contains(e.HierarchyEdgeId), ct) != edges.Count)
            throw new ArgumentException("CanonicalPublishedHierarchyRequired");
        var postal = string.IsNullOrWhiteSpace(candidate.PostalCode) ? null : candidate.PostalCode.Trim();
        // Existing shipment mirrors permit twenty characters, not the admission column's thirty-two.
        if (postal?.Length > 20) throw new ArgumentException("CanonicalPostalMirrorLimitExceeded");
        var disposition = postal != null ? "ValueValidated" : profile.PostalCodePolicy switch
        {
            GeographicPostalCodePolicy.Optional => "AbsentAllowed",
            GeographicPostalCodePolicy.NotUsed => "NotUsedAbsent",
            GeographicPostalCodePolicy.Forbidden => "ForbiddenAbsent",
            _ => throw new ArgumentException("PostalCodeRequired")
        };
        var payload = new CanonicalAddressPayload("CanonicalAddressEvidence", 1, evidence.Id, candidate.TenantId.Value,
            candidate.OwnerDomain, candidate.OwnerId.Value, now, ValidatorVersion, candidate.AddressFingerprintSha256,
            inputs, Hash(JsonSerializer.Serialize(inputs, Json)), candidate.Lineage, iso, profile.Id,
            release.Id, release.ReleaseNumber, release.ReleaseKey, release.ChecksumSha256, release.SourceSummary,
            publication.Id, publication.PublishedAtUtc, publication.ActivatedAtUtc!.Value,
            ids.Select(id => new CanonicalPlaceEvidence(id, places[id].PlaceKind, places[id].CanonicalCode, places[id].LevelCode, places[id].Source)).ToArray(),
            edges.ToArray(), profile.RequiredLevelCodesForDelivery.Concat(profile.RequiredLevelCodesForOperations).Distinct().Order().ToArray(),
            candidate.PostalCode, postal, profile.PostalCodePolicy, profile.PostalCodeRegex, disposition, "SyntaxOnly", null);
        var result = JsonSerializer.Serialize(payload with { PayloadSha256 = Hash(JsonSerializer.Serialize(payload, Json)) }, Json);
        Restore(result, evidence);
        return result;
    }

    public static CanonicalAddressPayload Restore(string json, AddressAdmissionEvidence evidence)
    {
        if (Encoding.UTF8.GetByteCount(json) > 65536) throw new ArgumentException("CanonicalEvidenceTooLarge");
        using var document = JsonDocument.Parse(json);
        RejectDuplicateMembers(document.RootElement);
        var p = JsonSerializer.Deserialize<CanonicalAddressPayload>(json, Json) ?? throw new ArgumentException("CanonicalEvidenceInvalid");
        if (p.Inputs == null || p.Lineage == null || p.CountryIso2 == null ||
            p.Kind != "CanonicalAddressEvidence" || p.Version != 1 || p.ValidatorVersion != ValidatorVersion ||
            !HashValid(p.PayloadSha256) || p.PayloadSha256 != Hash(JsonSerializer.Serialize(p with { PayloadSha256 = null }, Json)) ||
            p.EvidenceId != evidence.Id || p.TenantId == Guid.Empty || p.TenantId != evidence.TenantId ||
            p.OwnerId == Guid.Empty || p.OwnerId != evidence.OwnerId || p.OwnerDomain != evidence.OwnerDomain ||
            evidence.Status != AddressAdmissionStatus.Admitted || p.AddressFingerprintSha256 != evidence.AddressFingerprintSha256 ||
            !HashValid(p.AddressFingerprintSha256) || p.AdmittedAtUtc == default || p.AdmittedAtUtc != evidence.AdmittedAtUtc ||
            p.AdmittedAtUtc.Kind != DateTimeKind.Utc || p.PublishedAtUtc == default || p.ActivatedAtUtc == default ||
            p.CatalogReleaseId == Guid.Empty || p.ReleaseNumber <= 0 || string.IsNullOrWhiteSpace(p.ReleaseKey) ||
            p.CatalogReleaseId != evidence.CatalogReleaseId || p.Lineage.CountryPlaceId != evidence.CountryPlaceId ||
            p.Lineage.AdministrativeAreaPlaceId != evidence.AdministrativeAreaPlaceId || p.Lineage.LocalityPlaceId != evidence.LocalityPlaceId ||
            p.Lineage.DistrictPlaceId != evidence.DistrictPlaceId || p.Lineage.ZonePlaceId != evidence.ZonePlaceId ||
            p.NormalizedPostalCode != (string.IsNullOrWhiteSpace(evidence.PostalCode) ? null : evidence.PostalCode.Trim()) ||
            !RecognizedCountry(p.CountryIso2) || p.CountryProfileId == Guid.Empty || p.PublicationId == Guid.Empty ||
            p.ActivatedAtUtc > p.AdmittedAtUtc || p.PublishedAtUtc > p.ActivatedAtUtc ||
            !HashValid(p.ReleaseChecksum) || string.IsNullOrWhiteSpace(p.SourceSummary) ||
            p.InputFingerprintSha256 != Hash(JsonSerializer.Serialize(p.Inputs, Json)) || p.Inputs != Normalize(p.Inputs) ||
            p.AddressFingerprintSha256 != ComputeInputFingerprint(p.TenantId, p.OwnerDomain, p.OwnerId, p.Lineage, p.Inputs, p.SubmittedPostalCode) ||
            p.OwnerDomain == AddressAdmissionOwnerDomain.Checkout && p.Inputs.SubmittedCountryIso2 == null ||
            p.Inputs.SubmittedCountryIso2 != null && p.Inputs.SubmittedCountryIso2 != p.CountryIso2)
            throw new ArgumentException("CanonicalEvidenceBindingOrIntegrityInvalid");
        ValidateInputs(p.Inputs, p.OwnerDomain);
        var ids = new Guid?[] { p.Lineage.CountryPlaceId, p.Lineage.AdministrativeAreaPlaceId, p.Lineage.LocalityPlaceId, p.Lineage.DistrictPlaceId, p.Lineage.ZonePlaceId }
            .Where(id => id.HasValue).Select(id => id!.Value).ToArray();
        if (p.Places == null || p.HierarchyEdgeIds == null || p.RequiredLevels == null ||
            p.Places.Any(x => x == null) || !ids.SequenceEqual(p.Places.Select(x => x.Id)) || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length ||
            p.Places[0].Kind != GeographicPlaceKind.Country || p.Places[0].Code != p.CountryIso2 ||
            p.Places.Any(x => string.IsNullOrWhiteSpace(x.Source) || string.IsNullOrWhiteSpace(x.Code)) ||
            p.HierarchyEdgeIds.Length != ids.Length - 1 || p.HierarchyEdgeIds.Any(x => x == Guid.Empty) ||
            p.HierarchyEdgeIds.Distinct().Count() != p.HierarchyEdgeIds.Length ||
            p.RequiredLevels.Any(level => string.IsNullOrWhiteSpace(level) || p.Places.All(x => x.Level != level)) || p.PostalValidationScope != "SyntaxOnly")
            throw new ArgumentException("CanonicalEvidenceLineageInvalid");
        var expectedKinds = new Dictionary<Guid, GeographicPlaceKind> { [p.Lineage.CountryPlaceId] = GeographicPlaceKind.Country };
        if (p.Lineage.AdministrativeAreaPlaceId.HasValue) expectedKinds.Add(p.Lineage.AdministrativeAreaPlaceId.Value, GeographicPlaceKind.AdministrativeArea);
        if (p.Lineage.LocalityPlaceId.HasValue) expectedKinds.Add(p.Lineage.LocalityPlaceId.Value, GeographicPlaceKind.Locality);
        if (p.Lineage.DistrictPlaceId.HasValue) expectedKinds.Add(p.Lineage.DistrictPlaceId.Value, GeographicPlaceKind.District);
        if (p.Lineage.ZonePlaceId.HasValue) expectedKinds.Add(p.Lineage.ZonePlaceId.Value, GeographicPlaceKind.Zone);
        if (p.Places.Any(place => expectedKinds[place.Id] != place.Kind)) throw new ArgumentException("CanonicalEvidenceLineageInvalid");
        ValidatePostal(p);
        return p;
    }

    private static void ValidatePostal(CanonicalAddressPayload p)
    {
        var postal = p.NormalizedPostalCode;
        if (!Enum.IsDefined(p.PostalPolicy) || postal != (string.IsNullOrWhiteSpace(p.SubmittedPostalCode) ? null : p.SubmittedPostalCode.Trim()) ||
            postal?.Length > 20 || postal?.Any(char.IsControl) == true ||
            postal == null && p.PostalPolicy == GeographicPostalCodePolicy.Required ||
            postal != null && p.PostalPolicy is GeographicPostalCodePolicy.Forbidden or GeographicPostalCodePolicy.NotUsed)
            throw new ArgumentException("CanonicalEvidencePostalInvalid");
        var expected = postal != null ? "ValueValidated" : p.PostalPolicy switch
        { GeographicPostalCodePolicy.Optional => "AbsentAllowed", GeographicPostalCodePolicy.NotUsed => "NotUsedAbsent", _ => "ForbiddenAbsent" };
        if (p.PostalDisposition != expected) throw new ArgumentException("CanonicalEvidencePostalInvalid");
        if (postal != null)
        {
            if (string.IsNullOrWhiteSpace(p.PostalRegex)) throw new ArgumentException("CanonicalEvidencePostalRuleRequired");
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(postal, p.PostalRegex,
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
                if (!match.Success || match.Index != 0 || match.Length != postal.Length) throw new ArgumentException("CanonicalEvidencePostalInvalid");
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { throw new ArgumentException("CanonicalEvidencePostalRuleInvalid"); }
        }
    }

    private static CanonicalAddressInputs Normalize(CanonicalAddressInputs p) => p with
    {
        SubmittedCountryIso2 = Optional(p.SubmittedCountryIso2)?.ToUpperInvariant(), Street = p.Street?.Trim() ?? "", Street2 = Optional(p.Street2),
        City = Optional(p.City), State = Optional(p.State), Building = Optional(p.Building), Floor = Optional(p.Floor),
        Apartment = Optional(p.Apartment), Landmark = Optional(p.Landmark), ContactName = p.ContactName?.Trim() ?? "",
        ContactPhone = p.ContactPhone?.Trim() ?? "", ContactEmail = Optional(p.ContactEmail)
    };
    private static string? Optional(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static void ValidateInputs(CanonicalAddressInputs p, AddressAdmissionOwnerDomain domain)
    {
        if (domain == AddressAdmissionOwnerDomain.Warehouse ? p.AddressRole != "warehouse-origin" :
            domain != AddressAdmissionOwnerDomain.Checkout || p.AddressRole is not ("checkout-shipping" or "checkout-billing"))
            throw new ArgumentException("CanonicalAddressRoleInvalid");
        if (string.IsNullOrWhiteSpace(p.PrincipalReference) || string.IsNullOrWhiteSpace(p.OwnershipSource) ||
            string.IsNullOrWhiteSpace(p.Street) || string.IsNullOrWhiteSpace(p.ContactName) || string.IsNullOrWhiteSpace(p.ContactPhone) ||
            new[] { p.PrincipalReference, p.OwnershipSource, p.Street, p.Street2, p.City, p.State, p.Building, p.Floor, p.Apartment, p.Landmark,
                p.ContactName, p.ContactPhone, p.ContactEmail }.Any(s => s?.Length > 300 || s?.Any(char.IsControl) == true) ||
            p.ContactPhone.Count(char.IsDigit) < 7 || p.ContactPhone.Count(char.IsDigit) > 15 ||
            p.ContactEmail != null && !System.Net.Mail.MailAddress.TryCreate(p.ContactEmail, out _))
            throw new ArgumentException("CanonicalAddressInputsInvalid");
    }
    public static bool RecognizedCountry(string? iso) => iso?.Length == 2 && iso.All(c => c is >= 'A' and <= 'Z') &&
        CultureInfo.GetCultures(CultureTypes.SpecificCultures).Any(c => new RegionInfo(c.Name).TwoLetterISORegionName == iso);
    public static bool HashValid(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    public static string ComputeInputFingerprint(Guid tenant, AddressAdmissionOwnerDomain domain, Guid owner,
        GeographicPlaceLineage lineage, CanonicalAddressInputs inputs, string? postal) => Hash(JsonSerializer.Serialize(new
        { Version = ValidatorVersion, TenantId = tenant, OwnerDomain = domain, OwnerId = owner, Lineage = lineage, Inputs = Normalize(inputs), PostalCode = Optional(postal) }, Json));
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!seen.Add(property.Name)) throw new ArgumentException("CanonicalEvidenceDuplicateMember"); RejectDuplicateMembers(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicateMembers(child);
    }
}
