using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705170009_InitialGeographicRuntimeFoundation")]
    public partial class InitialGeographicRuntimeFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "GeographicCatalogReleases" (
                    "Id" uuid PRIMARY KEY,
                    "ReleaseNumber" bigint NOT NULL,
                    "ReleaseKey" varchar(64) NOT NULL,
                    "Status" varchar(24) NOT NULL,
                    "CountryScopeIso2" char(2) NULL,
                    "SourceSummary" varchar(512) NOT NULL,
                    "ChecksumSha256" char(64) NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    "CreatedByUserId" uuid NOT NULL,
                    "PublishedAtUtc" timestamptz NULL,
                    "PublishedByUserId" uuid NULL,
                    "SupersedesReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RollbackOfReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    CONSTRAINT "CK_GeoCatalogRelease_Status" CHECK ("Status" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')),
                    CONSTRAINT "CK_GeoCatalogRelease_Checksum" CHECK (char_length("ChecksumSha256") = 64)
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoCatalogRelease_Number" ON "GeographicCatalogReleases" ("ReleaseNumber");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoCatalogRelease_Key" ON "GeographicCatalogReleases" ("ReleaseKey");
                CREATE INDEX IF NOT EXISTS "IX_GeoCatalogRelease_Status_Country_Published" ON "GeographicCatalogReleases" ("Status", "CountryScopeIso2", "PublishedAtUtc");

                CREATE TABLE IF NOT EXISTS "GeographicPlaces" (
                    "Id" uuid PRIMARY KEY,
                    "StableKey" varchar(96) NOT NULL,
                    "PlaceKind" varchar(24) NOT NULL,
                    "CountryPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "LevelCode" varchar(64) NULL,
                    "CanonicalCode" varchar(96) NOT NULL,
                    "ExternalStandard" varchar(64) NULL,
                    "ExternalStandardCode" varchar(96) NULL,
                    "Status" varchar(24) NOT NULL,
                    "SuccessorPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "ValidFromUtc" timestamptz NOT NULL,
                    "ValidToUtc" timestamptz NULL,
                    "IntroducedCatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RetiredCatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "CentroidLatitude" numeric(9,6) NULL,
                    "CentroidLongitude" numeric(9,6) NULL,
                    "TimeZoneId" varchar(96) NULL,
                    "Source" varchar(128) NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    "CreatedByUserId" uuid NOT NULL,
                    "UpdatedAtUtc" timestamptz NOT NULL,
                    "UpdatedByUserId" uuid NOT NULL,
                    CONSTRAINT "CK_GeoPlace_Kind" CHECK ("PlaceKind" IN ('Country','AdministrativeArea','Locality','District','Zone')),
                    CONSTRAINT "CK_GeoPlace_Status" CHECK ("Status" IN ('Draft','Active','Deprecated','Merged','Retired','Rejected')),
                    CONSTRAINT "CK_GeoPlace_CountryParent" CHECK (("PlaceKind" = 'Country' AND "CountryPlaceId" IS NULL) OR ("PlaceKind" <> 'Country' AND "CountryPlaceId" IS NOT NULL)),
                    CONSTRAINT "CK_GeoPlace_Successor" CHECK ("SuccessorPlaceId" IS NULL OR "SuccessorPlaceId" <> "Id"),
                    CONSTRAINT "CK_GeoPlace_ValidWindow" CHECK ("ValidToUtc" IS NULL OR "ValidToUtc" > "ValidFromUtc"),
                    CONSTRAINT "CK_GeoPlace_Latitude" CHECK ("CentroidLatitude" IS NULL OR ("CentroidLatitude" >= -90 AND "CentroidLatitude" <= 90)),
                    CONSTRAINT "CK_GeoPlace_Longitude" CHECK ("CentroidLongitude" IS NULL OR ("CentroidLongitude" >= -180 AND "CentroidLongitude" <= 180))
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoPlace_StableKey" ON "GeographicPlaces" ("StableKey");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoPlace_ExternalStandardCode" ON "GeographicPlaces" ("ExternalStandard", "ExternalStandardCode");
                CREATE INDEX IF NOT EXISTS "IX_GeoPlaces_Country_Kind_Code_Status" ON "GeographicPlaces" ("CountryPlaceId", "PlaceKind", "CanonicalCode", "Status");

                CREATE TABLE IF NOT EXISTS "GeographicPlaceLevels" (
                    "Id" uuid PRIMARY KEY,
                    "CountryPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "LevelCode" varchar(64) NOT NULL,
                    "ParentLevelCode" varchar(64) NULL,
                    "SortOrder" integer NOT NULL,
                    "PlaceKind" varchar(24) NOT NULL,
                    "RequiredForDelivery" boolean NOT NULL,
                    "RequiredForOperations" boolean NOT NULL,
                    "RequiredForTax" boolean NOT NULL,
                    "DisplayNameKey" varchar(96) NOT NULL,
                    "AllowsChildren" boolean NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "IntroducedCatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RetiredCatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_GeoPlaceLevel_Kind" CHECK ("PlaceKind" IN ('AdministrativeArea','Locality','District','Zone')),
                    CONSTRAINT "CK_GeoPlaceLevel_Sort" CHECK ("SortOrder" > 0)
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoPlaceLevel_Code" ON "GeographicPlaceLevels" ("CountryPlaceId", "LevelCode");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoPlaceLevel_Order" ON "GeographicPlaceLevels" ("CountryPlaceId", "SortOrder");

                CREATE TABLE IF NOT EXISTS "GeographicPlaceHierarchyEdges" (
                    "Id" uuid PRIMARY KEY,
                    "ParentPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "ChildPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "CountryPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "Depth" integer NOT NULL DEFAULT 1,
                    "IntroducedCatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RetiredCatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "IsPrimary" boolean NOT NULL DEFAULT true,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_GeoHierarchy_NoSelf" CHECK ("ParentPlaceId" <> "ChildPlaceId"),
                    CONSTRAINT "CK_GeoHierarchy_Depth" CHECK ("Depth" >= 1)
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoHierarchy_Active" ON "GeographicPlaceHierarchyEdges" ("ParentPlaceId", "ChildPlaceId", "IntroducedCatalogReleaseId");
                CREATE INDEX IF NOT EXISTS "IX_GeoHierarchy_Country_Parent_Primary" ON "GeographicPlaceHierarchyEdges" ("CountryPlaceId", "ParentPlaceId", "IsPrimary");
                CREATE INDEX IF NOT EXISTS "IX_GeoHierarchy_ChildPlaceId" ON "GeographicPlaceHierarchyEdges" ("ChildPlaceId");

                CREATE TABLE IF NOT EXISTS "GeographicPlaceNames" (
                    "Id" uuid PRIMARY KEY,
                    "PlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "LanguageTag" varchar(35) NOT NULL,
                    "ScriptCode" char(4) NULL,
                    "NameKind" varchar(24) NOT NULL,
                    "Name" text NOT NULL,
                    "NormalizedName" text NOT NULL,
                    "SearchName" text NOT NULL,
                    "DisplayPriority" integer NOT NULL DEFAULT 100,
                    "IsPreferred" boolean NOT NULL,
                    "ValidFromUtc" timestamptz NOT NULL,
                    "ValidToUtc" timestamptz NULL,
                    "IntroducedCatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RetiredCatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_GeoPlaceName_Kind" CHECK ("NameKind" IN ('Official','Display','Short','Search','Historic','Transliteration','ProviderLabel')),
                    CONSTRAINT "CK_GeoPlaceName_Window" CHECK ("ValidToUtc" IS NULL OR "ValidToUtc" > "ValidFromUtc")
                );

                CREATE INDEX IF NOT EXISTS "IX_GeoPlaceNames_Preferred" ON "GeographicPlaceNames" ("PlaceId", "LanguageTag", "IsPreferred");
                CREATE INDEX IF NOT EXISTS "IX_GeoPlaceNames_Search" ON "GeographicPlaceNames" ("LanguageTag", "SearchName");

                CREATE TABLE IF NOT EXISTS "GeographicPlaceAliases" (
                    "Id" uuid PRIMARY KEY,
                    "PlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "CountryPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "LanguageTag" varchar(35) NULL,
                    "AliasKind" varchar(24) NOT NULL,
                    "Alias" text NOT NULL,
                    "NormalizedAlias" text NOT NULL,
                    "SearchAlias" text NOT NULL,
                    "Confidence" smallint NOT NULL,
                    "Source" varchar(128) NOT NULL,
                    "IsAmbiguous" boolean NOT NULL,
                    "IntroducedCatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RetiredCatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_GeoPlaceAlias_Kind" CHECK ("AliasKind" IN ('Spelling','Legacy','Transliteration','Abbreviation','Historic','ProviderLabel','MigrationEvidence')),
                    CONSTRAINT "CK_GeoPlaceAlias_Confidence" CHECK ("Confidence" BETWEEN 0 AND 100)
                );

                CREATE INDEX IF NOT EXISTS "IX_GeoPlaceAliases_Search" ON "GeographicPlaceAliases" ("CountryPlaceId", "LanguageTag", "NormalizedAlias");

                CREATE TABLE IF NOT EXISTS "GeographicCountryProfiles" (
                    "Id" uuid PRIMARY KEY,
                    "CountryPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "Iso2" char(2) NOT NULL,
                    "Iso3" char(3) NOT NULL,
                    "DefaultLanguageTag" varchar(35) NOT NULL,
                    "SupportedLanguageTags" text[] NOT NULL,
                    "RequiredLevelCodesForDelivery" text[] NOT NULL,
                    "RequiredLevelCodesForOperations" text[] NOT NULL,
                    "PostalCodePolicy" varchar(24) NOT NULL,
                    "PostalCodeRegex" text NULL,
                    "RequiresStreetLine1" boolean NOT NULL,
                    "AllowsCoordinates" boolean NOT NULL,
                    "RequiresCoordinateProvenance" boolean NOT NULL,
                    "OfflinePackageEnabled" boolean NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "IntroducedCatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RetiredCatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_GeoCountryProfile_Postal" CHECK ("PostalCodePolicy" IN ('NotUsed','Optional','Required','Forbidden'))
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoCountryProfile_Country" ON "GeographicCountryProfiles" ("CountryPlaceId");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoCountryProfile_Iso2" ON "GeographicCountryProfiles" ("Iso2");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_GeoCountryProfile_Iso3" ON "GeographicCountryProfiles" ("Iso3");

                CREATE TABLE IF NOT EXISTS "GeographicCatalogReleasePlaces" (
                    "CatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "PlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "PlaceStatus" varchar(24) NOT NULL,
                    PRIMARY KEY ("CatalogReleaseId", "PlaceId")
                );

                CREATE TABLE IF NOT EXISTS "GeographicCatalogReleaseEdges" (
                    "CatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "HierarchyEdgeId" uuid NOT NULL REFERENCES "GeographicPlaceHierarchyEdges"("Id") ON DELETE RESTRICT,
                    PRIMARY KEY ("CatalogReleaseId", "HierarchyEdgeId")
                );

                CREATE TABLE IF NOT EXISTS "GeographicCatalogPublications" (
                    "Id" uuid PRIMARY KEY,
                    "CatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "PublicationKind" varchar(24) NOT NULL,
                    "PublishedAtUtc" timestamptz NOT NULL,
                    "PublishedByUserId" uuid NOT NULL,
                    "ActivatedAtUtc" timestamptz NULL,
                    "SupersededAtUtc" timestamptz NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_GeoCatalogPublication_Kind" CHECK ("PublicationKind" IN ('Initial','Publish','Rollback','Supersede'))
                );

                CREATE INDEX IF NOT EXISTS "IX_GeoCatalogPublications_Release_Published" ON "GeographicCatalogPublications" ("CatalogReleaseId", "PublishedAtUtc");

                CREATE TABLE IF NOT EXISTS "CarrierProviders" (
                    "Id" uuid PRIMARY KEY,
                    "CarrierCode" varchar(64) NOT NULL,
                    "DisplayName" varchar(128) NOT NULL,
                    "SupportsGeographicMappings" boolean NOT NULL,
                    "RequiresMappingForDelivery" boolean NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    "UpdatedAtUtc" timestamptz NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CarrierProvider_Code" ON "CarrierProviders" ("CarrierCode");

                CREATE TABLE IF NOT EXISTS "CarrierGeographicMappingReleases" (
                    "Id" uuid PRIMARY KEY,
                    "CarrierProviderId" uuid NOT NULL REFERENCES "CarrierProviders"("Id") ON DELETE RESTRICT,
                    "ReleaseNumber" bigint NOT NULL,
                    "ReleaseKey" varchar(96) NOT NULL,
                    "Status" varchar(24) NOT NULL,
                    "Environment" varchar(24) NOT NULL,
                    "AccountScopeKind" varchar(24) NOT NULL,
                    "AccountScopeId" uuid NULL,
                    "TenantId" uuid NULL,
                    "CatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "SourceSummary" varchar(512) NOT NULL,
                    "ChecksumSha256" char(64) NOT NULL,
                    "EffectiveFromUtc" timestamptz NOT NULL,
                    "EffectiveToUtc" timestamptz NULL,
                    "FreshnessExpiresAtUtc" timestamptz NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    "CreatedByUserId" uuid NOT NULL,
                    "PublishedAtUtc" timestamptz NULL,
                    "PublishedByUserId" uuid NULL,
                    "SupersedesReleaseId" uuid NULL REFERENCES "CarrierGeographicMappingReleases"("Id") ON DELETE RESTRICT,
                    "RollbackOfReleaseId" uuid NULL REFERENCES "CarrierGeographicMappingReleases"("Id") ON DELETE RESTRICT,
                    CONSTRAINT "CK_CarrierGeoRelease_Status" CHECK ("Status" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')),
                    CONSTRAINT "CK_CarrierGeoRelease_Env" CHECK ("Environment" IN ('Production','Sandbox','Test')),
                    CONSTRAINT "CK_CarrierGeoRelease_Scope" CHECK ("AccountScopeKind" IN ('Global','ProviderAccount','TenantProviderAccount')),
                    CONSTRAINT "CK_CarrierGeoRelease_Window" CHECK ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > "EffectiveFromUtc")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CarrierGeoRelease_Number" ON "CarrierGeographicMappingReleases" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "ReleaseNumber");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CarrierGeoRelease_Key" ON "CarrierGeographicMappingReleases" ("ReleaseKey");
                CREATE INDEX IF NOT EXISTS "IX_CarrierGeoRelease_ActiveScope" ON "CarrierGeographicMappingReleases" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "Status", "EffectiveFromUtc");

                CREATE TABLE IF NOT EXISTS "CarrierGeographicMappingGroups" (
                    "Id" uuid PRIMARY KEY,
                    "MappingReleaseId" uuid NOT NULL REFERENCES "CarrierGeographicMappingReleases"("Id") ON DELETE RESTRICT,
                    "GroupKey" varchar(128) NOT NULL,
                    "CardinalityPolicy" varchar(40) NOT NULL,
                    "CanonicalPlaceKind" varchar(24) NOT NULL,
                    "ProviderPlaceType" varchar(64) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_CarrierGeoGroup_Cardinality" CHECK ("CardinalityPolicy" IN ('OneToOne','ManyCanonicalToOneProvider','OneCanonicalToManyProvider'))
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CarrierGeoGroup_Key" ON "CarrierGeographicMappingGroups" ("MappingReleaseId", "GroupKey");

                CREATE TABLE IF NOT EXISTS "CarrierGeographicMappings" (
                    "Id" uuid PRIMARY KEY,
                    "MappingReleaseId" uuid NOT NULL REFERENCES "CarrierGeographicMappingReleases"("Id") ON DELETE RESTRICT,
                    "MappingGroupId" uuid NULL REFERENCES "CarrierGeographicMappingGroups"("Id") ON DELETE RESTRICT,
                    "CarrierProviderId" uuid NOT NULL REFERENCES "CarrierProviders"("Id") ON DELETE RESTRICT,
                    "CanonicalPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "CountryPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "CanonicalPlaceKind" varchar(24) NOT NULL,
                    "ProviderPlaceType" varchar(64) NOT NULL,
                    "ProviderPlaceId" varchar(256) NOT NULL,
                    "ProviderPlaceCode" varchar(256) NULL,
                    "ProviderDisplayName" text NOT NULL,
                    "ProviderNormalizedName" text NOT NULL,
                    "ProviderParentMappingId" uuid NULL REFERENCES "CarrierGeographicMappings"("Id") ON DELETE RESTRICT,
                    "RequiredForOperation" varchar(64) NOT NULL,
                    "MappingStatus" varchar(24) NOT NULL,
                    "VerificationStatus" varchar(24) NOT NULL,
                    "Source" varchar(128) NOT NULL,
                    "EffectiveFromUtc" timestamptz NOT NULL,
                    "EffectiveToUtc" timestamptz NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    "ReviewedAtUtc" timestamptz NULL,
                    "ReviewedByUserId" uuid NULL,
                    CONSTRAINT "CK_CarrierGeoMapping_Status" CHECK ("MappingStatus" IN ('Draft','Active','Deprecated','Retired','Rejected')),
                    CONSTRAINT "CK_CarrierGeoMapping_Verification" CHECK ("VerificationStatus" IN ('Imported','AutoCarried','Reviewed','ManuallyApproved','Rejected')),
                    CONSTRAINT "CK_CarrierGeoMapping_Window" CHECK ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > "EffectiveFromUtc")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CarrierGeoMapping_ProviderId" ON "CarrierGeographicMappings" ("MappingReleaseId", "ProviderPlaceType", "ProviderPlaceId");
                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CarrierGeoMapping_CanonicalProvider" ON "CarrierGeographicMappings" ("MappingReleaseId", "CanonicalPlaceId", "ProviderPlaceType", "ProviderPlaceId");
                CREATE INDEX IF NOT EXISTS "IX_CarrierGeoMapping_Resolution" ON "CarrierGeographicMappings" ("MappingReleaseId", "CanonicalPlaceId", "ProviderPlaceType", "MappingStatus");

                CREATE TABLE IF NOT EXISTS "CarrierGeographicMappingPublications" (
                    "Id" uuid PRIMARY KEY,
                    "MappingReleaseId" uuid NOT NULL REFERENCES "CarrierGeographicMappingReleases"("Id") ON DELETE RESTRICT,
                    "PublicationKind" varchar(24) NOT NULL,
                    "PublishedAtUtc" timestamptz NOT NULL,
                    "PublishedByUserId" uuid NOT NULL,
                    "ActivatedAtUtc" timestamptz NULL,
                    "SupersededAtUtc" timestamptz NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_CarrierGeoPublication_Kind" CHECK ("PublicationKind" IN ('Initial','Publish','Rollback','Supersede'))
                );

                CREATE INDEX IF NOT EXISTS "IX_CarrierGeoPublications_Release_Published" ON "CarrierGeographicMappingPublications" ("MappingReleaseId", "PublishedAtUtc");

                CREATE TABLE IF NOT EXISTS "CarrierGeographicSyncRuns" (
                    "Id" uuid PRIMARY KEY,
                    "CarrierProviderId" uuid NOT NULL REFERENCES "CarrierProviders"("Id") ON DELETE RESTRICT,
                    "Environment" varchar(24) NOT NULL,
                    "AccountScopeKind" varchar(24) NOT NULL,
                    "AccountScopeId" uuid NULL,
                    "TenantId" uuid NULL,
                    "SourceKind" varchar(24) NOT NULL,
                    "SourceUri" text NULL,
                    "SourceVersion" varchar(128) NULL,
                    "SourceHashSha256" char(64) NULL,
                    "StartedAtUtc" timestamptz NOT NULL,
                    "CompletedAtUtc" timestamptz NULL,
                    "Status" varchar(24) NOT NULL,
                    "RawEvidenceObjectKey" text NULL,
                    "AddedCount" integer NOT NULL DEFAULT 0,
                    "ChangedCount" integer NOT NULL DEFAULT 0,
                    "RemovedCount" integer NOT NULL DEFAULT 0,
                    "AmbiguousCount" integer NOT NULL DEFAULT 0,
                    "CreatedAtUtc" timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS "IX_CarrierGeoSyncRuns_Scope_Status" ON "CarrierGeographicSyncRuns" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "StartedAtUtc", "Status");

                CREATE TABLE IF NOT EXISTS "CarrierGeographicSyncCandidates" (
                    "Id" uuid PRIMARY KEY,
                    "SyncRunId" uuid NOT NULL REFERENCES "CarrierGeographicSyncRuns"("Id") ON DELETE RESTRICT,
                    "CarrierProviderId" uuid NOT NULL REFERENCES "CarrierProviders"("Id") ON DELETE RESTRICT,
                    "ProviderPlaceType" varchar(64) NOT NULL,
                    "ProviderPlaceId" varchar(256) NOT NULL,
                    "ProviderPlaceCode" varchar(256) NULL,
                    "ProviderDisplayName" text NOT NULL,
                    "ProviderNormalizedName" text NOT NULL,
                    "ProviderParentPlaceId" varchar(256) NULL,
                    "CandidateCanonicalPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "Confidence" smallint NOT NULL,
                    "CandidateStatus" varchar(24) NOT NULL,
                    "ReviewReason" varchar(512) NULL,
                    "ReviewedByUserId" uuid NULL,
                    "ReviewedAtUtc" timestamptz NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_CarrierGeoSyncCandidate_Confidence" CHECK ("Confidence" BETWEEN 0 AND 100)
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_CarrierGeoSyncCandidate_Node" ON "CarrierGeographicSyncCandidates" ("SyncRunId", "ProviderPlaceType", "ProviderPlaceId");
                CREATE INDEX IF NOT EXISTS "IX_CarrierGeoSyncCandidates_Review" ON "CarrierGeographicSyncCandidates" ("CarrierProviderId", "CandidateStatus", "Confidence");

                CREATE TABLE IF NOT EXISTS "CarrierGeographicOverrides" (
                    "Id" uuid PRIMARY KEY,
                    "CarrierProviderId" uuid NOT NULL REFERENCES "CarrierProviders"("Id") ON DELETE RESTRICT,
                    "Environment" varchar(24) NOT NULL,
                    "AccountScopeKind" varchar(24) NOT NULL,
                    "AccountScopeId" uuid NULL,
                    "TenantId" uuid NULL,
                    "CanonicalPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "ProviderPlaceType" varchar(64) NOT NULL,
                    "ProviderPlaceId" varchar(256) NOT NULL,
                    "ProviderPlaceCode" varchar(256) NULL,
                    "ProviderDisplayName" text NOT NULL,
                    "Status" varchar(24) NOT NULL,
                    "EffectiveFromUtc" timestamptz NOT NULL,
                    "EffectiveToUtc" timestamptz NULL,
                    "Reason" varchar(512) NOT NULL,
                    "ApprovedByUserId" uuid NULL,
                    "ApprovedAtUtc" timestamptz NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_CarrierGeoOverride_Window" CHECK ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > "EffectiveFromUtc")
                );

                CREATE INDEX IF NOT EXISTS "IX_CarrierGeoOverrides_Scope_Place_Status" ON "CarrierGeographicOverrides" ("CarrierProviderId", "Environment", "AccountScopeKind", "AccountScopeId", "TenantId", "CanonicalPlaceId", "ProviderPlaceType", "Status");

                CREATE TABLE IF NOT EXISTS "PlaceMergeHistory" (
                    "Id" uuid PRIMARY KEY,
                    "SourcePlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "TargetPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "CatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "MergeKind" varchar(32) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_PlaceMergeHistory_NoSelf" CHECK ("SourcePlaceId" <> "TargetPlaceId")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_PlaceMergeHistory_Source_Target_Release" ON "PlaceMergeHistory" ("SourcePlaceId", "TargetPlaceId", "CatalogReleaseId");

                CREATE TABLE IF NOT EXISTS "PlaceDeprecations" (
                    "Id" uuid PRIMARY KEY,
                    "PlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "SuccessorPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "CatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "DeprecationKind" varchar(32) NOT NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL,
                    CONSTRAINT "CK_PlaceDeprecation_NoSelf" CHECK ("SuccessorPlaceId" IS NULL OR "SuccessorPlaceId" <> "PlaceId")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_PlaceDeprecations_Place_Release" ON "PlaceDeprecations" ("PlaceId", "CatalogReleaseId");

                CREATE TABLE IF NOT EXISTS "AddressAdmissionEvidence" (
                    "Id" uuid PRIMARY KEY,
                    "TenantId" uuid NULL,
                    "OwnerDomain" varchar(64) NOT NULL,
                    "OwnerId" uuid NULL,
                    "AddressFingerprintSha256" char(64) NOT NULL,
                    "Status" varchar(32) NOT NULL,
                    "FailureCode" varchar(128) NULL,
                    "CatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "CountryPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "AdministrativeAreaPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "LocalityPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "DistrictPlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "ZonePlaceId" uuid NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "AdmittedAtUtc" timestamptz NOT NULL,
                    "NormalizationRuleVersion" varchar(128) NULL,
                    "RawInputEvidenceObjectKey" text NULL,
                    "CreatedAtUtc" timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS "IX_AddressAdmissionEvidence_Owner_Fingerprint" ON "AddressAdmissionEvidence" ("TenantId", "OwnerDomain", "OwnerId", "AddressFingerprintSha256");
                CREATE INDEX IF NOT EXISTS "IX_AddressAdmissionEvidence_Lineage" ON "AddressAdmissionEvidence" ("CatalogReleaseId", "CountryPlaceId", "AdministrativeAreaPlaceId", "LocalityPlaceId", "DistrictPlaceId", "ZonePlaceId");

                CREATE TABLE IF NOT EXISTS "AddressNormalizationRules" (
                    "Id" uuid PRIMARY KEY,
                    "CountryPlaceId" uuid NOT NULL REFERENCES "GeographicPlaces"("Id") ON DELETE RESTRICT,
                    "LanguageTag" varchar(35) NULL,
                    "RuleKind" varchar(32) NOT NULL,
                    "RuleVersion" bigint NOT NULL,
                    "RuleDefinitionJson" text NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "IntroducedCatalogReleaseId" uuid NOT NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "RetiredCatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "CreatedAtUtc" timestamptz NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "UQ_AddressNormalizationRule_Version" ON "AddressNormalizationRules" ("CountryPlaceId", "LanguageTag", "RuleKind", "RuleVersion");
                CREATE INDEX IF NOT EXISTS "IX_AddressNormalizationRules_Active" ON "AddressNormalizationRules" ("CountryPlaceId", "LanguageTag", "RuleKind", "IsActive");

                CREATE TABLE IF NOT EXISTS "GeographicAudit" (
                    "Id" uuid PRIMARY KEY,
                    "TenantId" uuid NULL,
                    "ActorKind" varchar(32) NOT NULL,
                    "ActorId" uuid NULL,
                    "ActorRole" varchar(128) NOT NULL,
                    "Action" varchar(128) NOT NULL,
                    "AggregateType" varchar(128) NOT NULL,
                    "AggregateId" uuid NOT NULL,
                    "CatalogReleaseId" uuid NULL REFERENCES "GeographicCatalogReleases"("Id") ON DELETE RESTRICT,
                    "MappingReleaseId" uuid NULL REFERENCES "CarrierGeographicMappingReleases"("Id") ON DELETE RESTRICT,
                    "CountryScopeIso2" char(2) NULL,
                    "ProviderCode" varchar(64) NULL,
                    "Environment" varchar(24) NULL,
                    "AccountScope" varchar(128) NULL,
                    "Reason" varchar(512) NOT NULL,
                    "CorrelationId" varchar(128) NOT NULL,
                    "ValidationChecksumSha256" char(64) NULL,
                    "EvidenceObjectKey" text NULL,
                    "OccurredAtUtc" timestamptz NOT NULL,
                    "CreatedAtUtc" timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS "IX_GeographicAudit_Aggregate_Timeline" ON "GeographicAudit" ("AggregateType", "AggregateId", "OccurredAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_GeographicAudit_Tenant_Timeline" ON "GeographicAudit" ("TenantId", "OccurredAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_GeographicAudit_Release_Timeline" ON "GeographicAudit" ("CatalogReleaseId", "MappingReleaseId", "OccurredAtUtc");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "GeographicAudit";
                DROP TABLE IF EXISTS "AddressNormalizationRules";
                DROP TABLE IF EXISTS "AddressAdmissionEvidence";
                DROP TABLE IF EXISTS "PlaceDeprecations";
                DROP TABLE IF EXISTS "PlaceMergeHistory";
                DROP TABLE IF EXISTS "CarrierGeographicOverrides";
                DROP TABLE IF EXISTS "CarrierGeographicSyncCandidates";
                DROP TABLE IF EXISTS "CarrierGeographicSyncRuns";
                DROP TABLE IF EXISTS "CarrierGeographicMappingPublications";
                DROP TABLE IF EXISTS "CarrierGeographicMappings";
                DROP TABLE IF EXISTS "CarrierGeographicMappingGroups";
                DROP TABLE IF EXISTS "CarrierGeographicMappingReleases";
                DROP TABLE IF EXISTS "CarrierProviders";
                DROP TABLE IF EXISTS "GeographicCatalogPublications";
                DROP TABLE IF EXISTS "GeographicCatalogReleaseEdges";
                DROP TABLE IF EXISTS "GeographicCatalogReleasePlaces";
                DROP TABLE IF EXISTS "GeographicCountryProfiles";
                DROP TABLE IF EXISTS "GeographicPlaceAliases";
                DROP TABLE IF EXISTS "GeographicPlaceNames";
                DROP TABLE IF EXISTS "GeographicPlaceHierarchyEdges";
                DROP TABLE IF EXISTS "GeographicPlaceLevels";
                DROP TABLE IF EXISTS "GeographicPlaces";
                DROP TABLE IF EXISTS "GeographicCatalogReleases";
                """);
        }
    }
}
