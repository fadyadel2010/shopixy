using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Geographic;

namespace PointOfSale.Infrastructure.Data.Configurations.Geographic;

public sealed class GeographicCatalogReleaseConfiguration : IEntityTypeConfiguration<GeographicCatalogRelease>
{
    public void Configure(EntityTypeBuilder<GeographicCatalogRelease> builder)
    {
        builder.ToTable("GeographicCatalogReleases", table =>
        {
            table.HasCheckConstraint("CK_GeoCatalogRelease_Status", "\"Status\" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')");
            table.HasCheckConstraint("CK_GeoCatalogRelease_Checksum", "char_length(\"ChecksumSha256\") = 64");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReleaseKey).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.CountryScopeIso2).HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.SourceSummary).IsRequired().HasMaxLength(512);
        builder.Property(x => x.ChecksumSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.ReleaseNumber).IsUnique().HasDatabaseName("UQ_GeoCatalogRelease_Number");
        builder.HasIndex(x => x.ReleaseKey).IsUnique().HasDatabaseName("UQ_GeoCatalogRelease_Key");
        builder.HasIndex(x => new { x.Status, x.CountryScopeIso2, x.PublishedAtUtc }).HasDatabaseName("IX_GeoCatalogRelease_Status_Country_Published");
        builder.HasOne(x => x.SupersedesRelease)
            .WithMany()
            .HasForeignKey(x => x.SupersedesReleaseId)
            .HasConstraintName("FK_GeoCatalogRelease_Supersedes")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RollbackOfRelease)
            .WithMany()
            .HasForeignKey(x => x.RollbackOfReleaseId)
            .HasConstraintName("FK_GeoCatalogRelease_RollbackOf")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicPlaceConfiguration : IEntityTypeConfiguration<GeographicPlace>
{
    public void Configure(EntityTypeBuilder<GeographicPlace> builder)
    {
        builder.ToTable("GeographicPlaces", table =>
        {
            table.HasCheckConstraint("CK_GeoPlace_Kind", "\"PlaceKind\" IN ('Country','AdministrativeArea','Locality','District','Zone')");
            table.HasCheckConstraint("CK_GeoPlace_Status", "\"Status\" IN ('Draft','Active','Deprecated','Merged','Retired','Rejected')");
            table.HasCheckConstraint("CK_GeoPlace_CountryParent", "(\"PlaceKind\" = 'Country' AND \"CountryPlaceId\" IS NULL) OR (\"PlaceKind\" <> 'Country' AND \"CountryPlaceId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_GeoPlace_Successor", "\"SuccessorPlaceId\" IS NULL OR \"SuccessorPlaceId\" <> \"Id\"");
            table.HasCheckConstraint("CK_GeoPlace_ValidWindow", "\"ValidToUtc\" IS NULL OR \"ValidToUtc\" > \"ValidFromUtc\"");
            table.HasCheckConstraint("CK_GeoPlace_Latitude", "\"CentroidLatitude\" IS NULL OR (\"CentroidLatitude\" >= -90 AND \"CentroidLatitude\" <= 90)");
            table.HasCheckConstraint("CK_GeoPlace_Longitude", "\"CentroidLongitude\" IS NULL OR (\"CentroidLongitude\" >= -180 AND \"CentroidLongitude\" <= 180)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StableKey).IsRequired().HasMaxLength(96);
        builder.Property(x => x.PlaceKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.LevelCode).HasMaxLength(64);
        builder.Property(x => x.CanonicalCode).IsRequired().HasMaxLength(96);
        builder.Property(x => x.ExternalStandard).HasMaxLength(64);
        builder.Property(x => x.ExternalStandardCode).HasMaxLength(96);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.CentroidLatitude).HasPrecision(9, 6);
        builder.Property(x => x.CentroidLongitude).HasPrecision(9, 6);
        builder.Property(x => x.TimeZoneId).HasMaxLength(96);
        builder.Property(x => x.Source).IsRequired().HasMaxLength(128);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.StableKey).IsUnique().HasDatabaseName("UQ_GeoPlace_StableKey");
        builder.HasIndex(x => new { x.ExternalStandard, x.ExternalStandardCode }).IsUnique().HasDatabaseName("UQ_GeoPlace_ExternalStandardCode");
        builder.HasIndex(x => new { x.CountryPlaceId, x.PlaceKind, x.CanonicalCode, x.Status }).HasDatabaseName("IX_GeoPlaces_Country_Kind_Code_Status");
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SuccessorPlace).WithMany().HasForeignKey(x => x.SuccessorPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IntroducedCatalogRelease).WithMany().HasForeignKey(x => x.IntroducedCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetiredCatalogRelease).WithMany().HasForeignKey(x => x.RetiredCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicPlaceLevelConfiguration : IEntityTypeConfiguration<GeographicPlaceLevel>
{
    public void Configure(EntityTypeBuilder<GeographicPlaceLevel> builder)
    {
        builder.ToTable("GeographicPlaceLevels", table =>
        {
            table.HasCheckConstraint("CK_GeoPlaceLevel_Kind", "\"PlaceKind\" IN ('AdministrativeArea','Locality','District','Zone')");
            table.HasCheckConstraint("CK_GeoPlaceLevel_Sort", "\"SortOrder\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LevelCode).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ParentLevelCode).HasMaxLength(64);
        builder.Property(x => x.PlaceKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.DisplayNameKey).IsRequired().HasMaxLength(96);
        builder.HasIndex(x => new { x.CountryPlaceId, x.LevelCode }).IsUnique().HasDatabaseName("UQ_GeoPlaceLevel_Code");
        builder.HasIndex(x => new { x.CountryPlaceId, x.SortOrder }).IsUnique().HasDatabaseName("UQ_GeoPlaceLevel_Order");
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IntroducedCatalogRelease).WithMany().HasForeignKey(x => x.IntroducedCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetiredCatalogRelease).WithMany().HasForeignKey(x => x.RetiredCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicPlaceHierarchyEdgeConfiguration : IEntityTypeConfiguration<GeographicPlaceHierarchyEdge>
{
    public void Configure(EntityTypeBuilder<GeographicPlaceHierarchyEdge> builder)
    {
        builder.ToTable("GeographicPlaceHierarchyEdges", table =>
        {
            table.HasCheckConstraint("CK_GeoHierarchy_NoSelf", "\"ParentPlaceId\" <> \"ChildPlaceId\"");
            table.HasCheckConstraint("CK_GeoHierarchy_Depth", "\"Depth\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ParentPlaceId, x.ChildPlaceId, x.IntroducedCatalogReleaseId }).IsUnique().HasDatabaseName("UQ_GeoHierarchy_Active");
        builder.HasIndex(x => new { x.CountryPlaceId, x.ParentPlaceId, x.IsPrimary }).HasDatabaseName("IX_GeoHierarchy_Country_Parent_Primary");
        builder.HasIndex(x => x.ChildPlaceId).HasDatabaseName("IX_GeoHierarchy_ChildPlaceId");
        builder.HasOne(x => x.ParentPlace).WithMany().HasForeignKey(x => x.ParentPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ChildPlace).WithMany().HasForeignKey(x => x.ChildPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IntroducedCatalogRelease).WithMany().HasForeignKey(x => x.IntroducedCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetiredCatalogRelease).WithMany().HasForeignKey(x => x.RetiredCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicPlaceNameConfiguration : IEntityTypeConfiguration<GeographicPlaceName>
{
    public void Configure(EntityTypeBuilder<GeographicPlaceName> builder)
    {
        builder.ToTable("GeographicPlaceNames", table =>
        {
            table.HasCheckConstraint("CK_GeoPlaceName_Kind", "\"NameKind\" IN ('Official','Display','Short','Search','Historic','Transliteration','ProviderLabel')");
            table.HasCheckConstraint("CK_GeoPlaceName_Window", "\"ValidToUtc\" IS NULL OR \"ValidToUtc\" > \"ValidFromUtc\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LanguageTag).IsRequired().HasMaxLength(35);
        builder.Property(x => x.ScriptCode).HasMaxLength(4).IsFixedLength();
        builder.Property(x => x.NameKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.NormalizedName).IsRequired();
        builder.Property(x => x.SearchName).IsRequired();
        builder.HasIndex(x => new { x.PlaceId, x.LanguageTag, x.IsPreferred }).HasDatabaseName("IX_GeoPlaceNames_Preferred");
        builder.HasIndex(x => new { x.LanguageTag, x.SearchName }).HasDatabaseName("IX_GeoPlaceNames_Search");
        builder.HasOne(x => x.Place).WithMany(x => x.Names).HasForeignKey(x => x.PlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IntroducedCatalogRelease).WithMany().HasForeignKey(x => x.IntroducedCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetiredCatalogRelease).WithMany().HasForeignKey(x => x.RetiredCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicPlaceAliasConfiguration : IEntityTypeConfiguration<GeographicPlaceAlias>
{
    public void Configure(EntityTypeBuilder<GeographicPlaceAlias> builder)
    {
        builder.ToTable("GeographicPlaceAliases", table =>
        {
            table.HasCheckConstraint("CK_GeoPlaceAlias_Kind", "\"AliasKind\" IN ('Spelling','Legacy','Transliteration','Abbreviation','Historic','ProviderLabel','MigrationEvidence')");
            table.HasCheckConstraint("CK_GeoPlaceAlias_Confidence", "\"Confidence\" BETWEEN 0 AND 100");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LanguageTag).HasMaxLength(35);
        builder.Property(x => x.AliasKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Alias).IsRequired();
        builder.Property(x => x.NormalizedAlias).IsRequired();
        builder.Property(x => x.SearchAlias).IsRequired();
        builder.Property(x => x.Source).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.CountryPlaceId, x.LanguageTag, x.NormalizedAlias }).HasDatabaseName("IX_GeoPlaceAliases_Search");
        builder.HasOne(x => x.Place).WithMany(x => x.Aliases).HasForeignKey(x => x.PlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IntroducedCatalogRelease).WithMany().HasForeignKey(x => x.IntroducedCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetiredCatalogRelease).WithMany().HasForeignKey(x => x.RetiredCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicCountryProfileConfiguration : IEntityTypeConfiguration<GeographicCountryProfile>
{
    public void Configure(EntityTypeBuilder<GeographicCountryProfile> builder)
    {
        builder.ToTable("GeographicCountryProfiles", table =>
            table.HasCheckConstraint("CK_GeoCountryProfile_Postal", "\"PostalCodePolicy\" IN ('NotUsed','Optional','Required','Forbidden')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Iso2).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.Iso3).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.DefaultLanguageTag).IsRequired().HasMaxLength(35);
        builder.Property(x => x.SupportedLanguageTags).HasColumnType("text[]");
        builder.Property(x => x.RequiredLevelCodesForDelivery).HasColumnType("text[]");
        builder.Property(x => x.RequiredLevelCodesForOperations).HasColumnType("text[]");
        builder.Property(x => x.PostalCodePolicy).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.HasIndex(x => x.CountryPlaceId).IsUnique().HasDatabaseName("UQ_GeoCountryProfile_Country");
        builder.HasIndex(x => x.Iso2).IsUnique().HasDatabaseName("UQ_GeoCountryProfile_Iso2");
        builder.HasIndex(x => x.Iso3).IsUnique().HasDatabaseName("UQ_GeoCountryProfile_Iso3");
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IntroducedCatalogRelease).WithMany().HasForeignKey(x => x.IntroducedCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetiredCatalogRelease).WithMany().HasForeignKey(x => x.RetiredCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicCatalogReleasePlaceConfiguration : IEntityTypeConfiguration<GeographicCatalogReleasePlace>
{
    public void Configure(EntityTypeBuilder<GeographicCatalogReleasePlace> builder)
    {
        builder.ToTable("GeographicCatalogReleasePlaces");
        builder.HasKey(x => new { x.CatalogReleaseId, x.PlaceId });
        builder.Property(x => x.PlaceStatus).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Place).WithMany().HasForeignKey(x => x.PlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicCatalogReleaseEdgeConfiguration : IEntityTypeConfiguration<GeographicCatalogReleaseEdge>
{
    public void Configure(EntityTypeBuilder<GeographicCatalogReleaseEdge> builder)
    {
        builder.ToTable("GeographicCatalogReleaseEdges");
        builder.HasKey(x => new { x.CatalogReleaseId, x.HierarchyEdgeId });
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.HierarchyEdge).WithMany().HasForeignKey(x => x.HierarchyEdgeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicCatalogPublicationConfiguration : IEntityTypeConfiguration<GeographicCatalogPublication>
{
    public void Configure(EntityTypeBuilder<GeographicCatalogPublication> builder)
    {
        builder.ToTable("GeographicCatalogPublications", table =>
            table.HasCheckConstraint("CK_GeoCatalogPublication_Kind", "\"PublicationKind\" IN ('Initial','Publish','Rollback','Supersede')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicationKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.HasIndex(x => new { x.CatalogReleaseId, x.PublishedAtUtc }).HasDatabaseName("IX_GeoCatalogPublications_Release_Published");
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierProviderConfiguration : IEntityTypeConfiguration<CarrierProvider>
{
    public void Configure(EntityTypeBuilder<CarrierProvider> builder)
    {
        builder.ToTable("CarrierProviders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CarrierCode).IsRequired().HasMaxLength(64);
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => x.CarrierCode).IsUnique().HasDatabaseName("UQ_CarrierProvider_Code");
    }
}

public sealed class CarrierGeographicMappingReleaseConfiguration : IEntityTypeConfiguration<CarrierGeographicMappingRelease>
{
    public void Configure(EntityTypeBuilder<CarrierGeographicMappingRelease> builder)
    {
        builder.ToTable("CarrierGeographicMappingReleases", table =>
        {
            table.HasCheckConstraint("CK_CarrierGeoRelease_Status", "\"Status\" IN ('Draft','Validated','Published','Superseded','RolledBack','Rejected')");
            table.HasCheckConstraint("CK_CarrierGeoRelease_Env", "\"Environment\" IN ('Production','Sandbox','Test')");
            table.HasCheckConstraint("CK_CarrierGeoRelease_Scope", "\"AccountScopeKind\" IN ('Global','ProviderAccount','TenantProviderAccount')");
            table.HasCheckConstraint("CK_CarrierGeoRelease_Window", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReleaseKey).IsRequired().HasMaxLength(96);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.AccountScopeKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.SourceSummary).IsRequired().HasMaxLength(512);
        builder.Property(x => x.ChecksumSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.CarrierProviderId, x.Environment, x.AccountScopeKind, x.AccountScopeId, x.ReleaseNumber }).IsUnique().HasDatabaseName("UQ_CarrierGeoRelease_Number");
        builder.HasIndex(x => x.ReleaseKey).IsUnique().HasDatabaseName("UQ_CarrierGeoRelease_Key");
        builder.HasIndex(x => new { x.CarrierProviderId, x.Environment, x.AccountScopeKind, x.AccountScopeId, x.Status, x.EffectiveFromUtc }).HasDatabaseName("IX_CarrierGeoRelease_ActiveScope");
        builder.HasOne(x => x.CarrierProvider).WithMany().HasForeignKey(x => x.CarrierProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupersedesRelease)
            .WithMany()
            .HasForeignKey(x => x.SupersedesReleaseId)
            .HasConstraintName("FK_CarrierGeoRelease_Supersedes")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RollbackOfRelease)
            .WithMany()
            .HasForeignKey(x => x.RollbackOfReleaseId)
            .HasConstraintName("FK_CarrierGeoRelease_RollbackOf")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierGeographicMappingGroupConfiguration : IEntityTypeConfiguration<CarrierGeographicMappingGroup>
{
    public void Configure(EntityTypeBuilder<CarrierGeographicMappingGroup> builder)
    {
        builder.ToTable("CarrierGeographicMappingGroups", table =>
            table.HasCheckConstraint("CK_CarrierGeoGroup_Cardinality", "\"CardinalityPolicy\" IN ('OneToOne','ManyCanonicalToOneProvider','OneCanonicalToManyProvider')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GroupKey).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CardinalityPolicy).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.CanonicalPlaceKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.ProviderPlaceType).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.HasIndex(x => new { x.MappingReleaseId, x.GroupKey }).IsUnique().HasDatabaseName("UQ_CarrierGeoGroup_Key");
        builder.HasOne(x => x.MappingRelease).WithMany().HasForeignKey(x => x.MappingReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierGeographicMappingConfiguration : IEntityTypeConfiguration<CarrierGeographicMapping>
{
    public void Configure(EntityTypeBuilder<CarrierGeographicMapping> builder)
    {
        builder.ToTable("CarrierGeographicMappings", table =>
        {
            table.HasCheckConstraint("CK_CarrierGeoMapping_Status", "\"MappingStatus\" IN ('Draft','Active','Deprecated','Retired','Rejected')");
            table.HasCheckConstraint("CK_CarrierGeoMapping_Verification", "\"VerificationStatus\" IN ('Imported','AutoCarried','Reviewed','ManuallyApproved','Rejected')");
            table.HasCheckConstraint("CK_CarrierGeoMapping_Window", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CanonicalPlaceKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.ProviderPlaceType).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ProviderPlaceId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ProviderPlaceCode).HasMaxLength(256);
        builder.Property(x => x.ProviderDisplayName).IsRequired();
        builder.Property(x => x.ProviderNormalizedName).IsRequired();
        builder.Property(x => x.RequiredForOperation).IsRequired().HasMaxLength(64);
        builder.Property(x => x.MappingStatus).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.VerificationStatus).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Source).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.MappingReleaseId, x.ProviderPlaceType, x.ProviderPlaceId }).IsUnique().HasDatabaseName("UQ_CarrierGeoMapping_ProviderId");
        builder.HasIndex(x => new { x.MappingReleaseId, x.CanonicalPlaceId, x.ProviderPlaceType, x.ProviderPlaceId }).IsUnique().HasDatabaseName("UQ_CarrierGeoMapping_CanonicalProvider");
        builder.HasIndex(x => new { x.MappingReleaseId, x.CanonicalPlaceId, x.ProviderPlaceType, x.MappingStatus }).HasDatabaseName("IX_CarrierGeoMapping_Resolution");
        builder.HasOne(x => x.MappingRelease).WithMany(x => x.Mappings).HasForeignKey(x => x.MappingReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MappingGroup).WithMany().HasForeignKey(x => x.MappingGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CarrierProvider).WithMany().HasForeignKey(x => x.CarrierProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CanonicalPlace).WithMany().HasForeignKey(x => x.CanonicalPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProviderParentMapping).WithMany().HasForeignKey(x => x.ProviderParentMappingId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierGeographicMappingPublicationConfiguration : IEntityTypeConfiguration<CarrierGeographicMappingPublication>
{
    public void Configure(EntityTypeBuilder<CarrierGeographicMappingPublication> builder)
    {
        builder.ToTable("CarrierGeographicMappingPublications", table =>
            table.HasCheckConstraint("CK_CarrierGeoPublication_Kind", "\"PublicationKind\" IN ('Initial','Publish','Rollback','Supersede')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PublicationKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.HasIndex(x => new { x.MappingReleaseId, x.PublishedAtUtc }).HasDatabaseName("IX_CarrierGeoPublications_Release_Published");
        builder.HasOne(x => x.MappingRelease).WithMany().HasForeignKey(x => x.MappingReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierGeographicSyncRunConfiguration : IEntityTypeConfiguration<CarrierGeographicSyncRun>
{
    public void Configure(EntityTypeBuilder<CarrierGeographicSyncRun> builder)
    {
        builder.ToTable("CarrierGeographicSyncRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.AccountScopeKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.SourceVersion).HasMaxLength(128);
        builder.Property(x => x.SourceHashSha256).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.CarrierProviderId, x.Environment, x.AccountScopeKind, x.AccountScopeId, x.StartedAtUtc, x.Status }).HasDatabaseName("IX_CarrierGeoSyncRuns_Scope_Status");
        builder.HasOne(x => x.CarrierProvider).WithMany().HasForeignKey(x => x.CarrierProviderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierGeographicSyncCandidateConfiguration : IEntityTypeConfiguration<CarrierGeographicSyncCandidate>
{
    public void Configure(EntityTypeBuilder<CarrierGeographicSyncCandidate> builder)
    {
        builder.ToTable("CarrierGeographicSyncCandidates", table =>
            table.HasCheckConstraint("CK_CarrierGeoSyncCandidate_Confidence", "\"Confidence\" BETWEEN 0 AND 100"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderPlaceType).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ProviderPlaceId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ProviderPlaceCode).HasMaxLength(256);
        builder.Property(x => x.ProviderDisplayName).IsRequired();
        builder.Property(x => x.ProviderNormalizedName).IsRequired();
        builder.Property(x => x.ProviderParentPlaceId).HasMaxLength(256);
        builder.Property(x => x.CandidateStatus).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.ReviewReason).HasMaxLength(512);
        builder.HasIndex(x => new { x.SyncRunId, x.ProviderPlaceType, x.ProviderPlaceId }).IsUnique().HasDatabaseName("UQ_CarrierGeoSyncCandidate_Node");
        builder.HasIndex(x => new { x.CarrierProviderId, x.CandidateStatus, x.Confidence }).HasDatabaseName("IX_CarrierGeoSyncCandidates_Review");
        builder.HasOne(x => x.SyncRun).WithMany().HasForeignKey(x => x.SyncRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CarrierProvider).WithMany().HasForeignKey(x => x.CarrierProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CandidateCanonicalPlace).WithMany().HasForeignKey(x => x.CandidateCanonicalPlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CarrierGeographicOverrideConfiguration : IEntityTypeConfiguration<CarrierGeographicOverride>
{
    public void Configure(EntityTypeBuilder<CarrierGeographicOverride> builder)
    {
        builder.ToTable("CarrierGeographicOverrides", table =>
            table.HasCheckConstraint("CK_CarrierGeoOverride_Window", "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\""));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.AccountScopeKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.ProviderPlaceType).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ProviderPlaceId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ProviderPlaceCode).HasMaxLength(256);
        builder.Property(x => x.ProviderDisplayName).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.HasIndex(x => new { x.CarrierProviderId, x.Environment, x.AccountScopeKind, x.AccountScopeId, x.TenantId, x.CanonicalPlaceId, x.ProviderPlaceType, x.Status }).HasDatabaseName("IX_CarrierGeoOverrides_Scope_Place_Status");
        builder.HasOne(x => x.CarrierProvider).WithMany().HasForeignKey(x => x.CarrierProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CanonicalPlace).WithMany().HasForeignKey(x => x.CanonicalPlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PlaceMergeHistoryConfiguration : IEntityTypeConfiguration<PlaceMergeHistory>
{
    public void Configure(EntityTypeBuilder<PlaceMergeHistory> builder)
    {
        builder.ToTable("PlaceMergeHistory", table =>
            table.HasCheckConstraint("CK_PlaceMergeHistory_NoSelf", "\"SourcePlaceId\" <> \"TargetPlaceId\""));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MergeKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.HasIndex(x => new { x.SourcePlaceId, x.TargetPlaceId, x.CatalogReleaseId }).IsUnique().HasDatabaseName("UQ_PlaceMergeHistory_Source_Target_Release");
        builder.HasOne(x => x.SourcePlace).WithMany().HasForeignKey(x => x.SourcePlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetPlace).WithMany().HasForeignKey(x => x.TargetPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PlaceDeprecationConfiguration : IEntityTypeConfiguration<PlaceDeprecation>
{
    public void Configure(EntityTypeBuilder<PlaceDeprecation> builder)
    {
        builder.ToTable("PlaceDeprecations", table =>
            table.HasCheckConstraint("CK_PlaceDeprecation_NoSelf", "\"SuccessorPlaceId\" IS NULL OR \"SuccessorPlaceId\" <> \"PlaceId\""));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DeprecationKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.HasIndex(x => new { x.PlaceId, x.CatalogReleaseId }).IsUnique().HasDatabaseName("UQ_PlaceDeprecations_Place_Release");
        builder.HasOne(x => x.Place).WithMany().HasForeignKey(x => x.PlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SuccessorPlace).WithMany().HasForeignKey(x => x.SuccessorPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AddressAdmissionEvidenceConfiguration : IEntityTypeConfiguration<AddressAdmissionEvidence>
{
    public void Configure(EntityTypeBuilder<AddressAdmissionEvidence> builder)
    {
        builder.ToTable("AddressAdmissionEvidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OwnerDomain).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(x => x.AddressFingerprintSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(128);
        builder.Property(x => x.NormalizationRuleVersion).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.OwnerDomain, x.OwnerId, x.AddressFingerprintSha256 }).HasDatabaseName("IX_AddressAdmissionEvidence_Owner_Fingerprint");
        builder.HasIndex(x => new { x.CatalogReleaseId, x.CountryPlaceId, x.AdministrativeAreaPlaceId, x.LocalityPlaceId, x.DistrictPlaceId, x.ZonePlaceId }).HasDatabaseName("IX_AddressAdmissionEvidence_Lineage");
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AdministrativeAreaPlace).WithMany().HasForeignKey(x => x.AdministrativeAreaPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LocalityPlace).WithMany().HasForeignKey(x => x.LocalityPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DistrictPlace).WithMany().HasForeignKey(x => x.DistrictPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ZonePlace).WithMany().HasForeignKey(x => x.ZonePlaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AddressNormalizationRuleConfiguration : IEntityTypeConfiguration<AddressNormalizationRule>
{
    public void Configure(EntityTypeBuilder<AddressNormalizationRule> builder)
    {
        builder.ToTable("AddressNormalizationRules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LanguageTag).HasMaxLength(35);
        builder.Property(x => x.RuleKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RuleDefinitionJson).IsRequired();
        builder.HasIndex(x => new { x.CountryPlaceId, x.LanguageTag, x.RuleKind, x.RuleVersion }).IsUnique().HasDatabaseName("UQ_AddressNormalizationRule_Version");
        builder.HasIndex(x => new { x.CountryPlaceId, x.LanguageTag, x.RuleKind, x.IsActive }).HasDatabaseName("IX_AddressNormalizationRules_Active");
        builder.HasOne(x => x.CountryPlace).WithMany().HasForeignKey(x => x.CountryPlaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.IntroducedCatalogRelease).WithMany().HasForeignKey(x => x.IntroducedCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetiredCatalogRelease).WithMany().HasForeignKey(x => x.RetiredCatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GeographicAuditConfiguration : IEntityTypeConfiguration<GeographicAudit>
{
    public void Configure(EntityTypeBuilder<GeographicAudit> builder)
    {
        builder.ToTable("GeographicAudit");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActorRole).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(128);
        builder.Property(x => x.AggregateType).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CountryScopeIso2).HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.ProviderCode).HasMaxLength(64);
        builder.Property(x => x.Environment).HasMaxLength(24);
        builder.Property(x => x.AccountScope).HasMaxLength(128);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ValidationChecksumSha256).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.AggregateType, x.AggregateId, x.OccurredAtUtc }).HasDatabaseName("IX_GeographicAudit_Aggregate_Timeline");
        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc }).HasDatabaseName("IX_GeographicAudit_Tenant_Timeline");
        builder.HasIndex(x => new { x.CatalogReleaseId, x.MappingReleaseId, x.OccurredAtUtc }).HasDatabaseName("IX_GeographicAudit_Release_Timeline");
        builder.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MappingRelease).WithMany().HasForeignKey(x => x.MappingReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
