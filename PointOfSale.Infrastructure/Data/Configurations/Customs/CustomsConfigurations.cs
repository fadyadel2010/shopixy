using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Customs;

namespace PointOfSale.Infrastructure.Data.Configurations.Customs;

public sealed class CustomsEvidenceConfiguration : IEntityTypeConfiguration<CustomsEvidence>
{
    public void Configure(EntityTypeBuilder<CustomsEvidence> builder)
    {
        builder.ToTable("CustomsEvidence", table =>
        {
            table.HasCheckConstraint("CK_CustomsEvidence_Status", "\"Status\" IN ('Candidate','Admitted','Draft','UnderReview','Approved','Published','Rejected','Reopened','Retired','Superseded')");
            table.HasCheckConstraint("CK_CustomsEvidence_SourceKind", "\"SourceKind\" IN ('Product','ProductVariant','OrderLine','System')");
            table.HasCheckConstraint("CK_CustomsEvidence_CurrentVersion", "\"CurrentVersionNumber\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.AdmittedBy).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CreatedByActor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.LastEditedByActor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.SubmittedByActor).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.SourceKind, x.SourceId, x.Status }).HasDatabaseName("IX_CustomsEvidence_Source_Status");
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.ProductVariantId, x.Status }).HasDatabaseName("IX_CustomsEvidence_Product_Status");
    }
}

public sealed class CustomsEvidenceVersionConfiguration : IEntityTypeConfiguration<CustomsEvidenceVersion>
{
    public void Configure(EntityTypeBuilder<CustomsEvidenceVersion> builder)
    {
        builder.ToTable("CustomsEvidenceVersions", table =>
        {
            table.HasCheckConstraint("CK_CustomsEvidenceVersion_Version", "\"VersionNumber\" > 0");
            table.HasCheckConstraint("CK_CustomsEvidenceVersion_HsCode", "char_length(btrim(\"HsCode\")) > 0");
            table.HasCheckConstraint("CK_CustomsEvidenceVersion_Commodity", "char_length(btrim(\"CommodityDescription\")) > 0");
            table.HasCheckConstraint("CK_CustomsEvidenceVersion_Country", "char_length(\"CountryOfOriginIso2\") = 2");
            table.HasCheckConstraint("CK_CustomsEvidenceVersion_Value", "\"DeclaredValue\" > 0");
            table.HasCheckConstraint("CK_CustomsEvidenceVersion_NetWeight", "\"NetWeight\" > 0");
            table.HasCheckConstraint("CK_CustomsEvidenceVersion_Fingerprint", "char_length(\"EvidenceFingerprintSha256\") = 64");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.HsCode).IsRequired().HasMaxLength(32);
        builder.Property(x => x.CommodityDescription).IsRequired().HasMaxLength(512);
        builder.Property(x => x.CountryOfOriginIso2).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.CountryOfManufactureIso2).HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.DeclaredValue).HasPrecision(18, 4);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.NetWeight).HasPrecision(18, 6);
        builder.Property(x => x.WeightUnit).IsRequired().HasMaxLength(16);
        builder.Property(x => x.QuantityUnit).IsRequired().HasMaxLength(32);
        builder.Property(x => x.ExportClassification).HasMaxLength(128);
        builder.Property(x => x.ValueSource).IsRequired().HasMaxLength(128);
        builder.Property(x => x.SourceEvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.EvidenceFingerprintSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.AdmittedBy).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.CustomsEvidenceId, x.VersionNumber }).IsUnique().HasDatabaseName("UQ_CustomsEvidenceVersion_Number");
        builder.HasIndex(x => new { x.TenantId, x.EvidenceFingerprintSha256 }).HasDatabaseName("IX_CustomsEvidenceVersion_Fingerprint");
        builder.HasOne(x => x.Evidence).WithMany(x => x.Versions).HasForeignKey(x => x.CustomsEvidenceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShipmentCustomsSnapshotConfiguration : IEntityTypeConfiguration<ShipmentCustomsSnapshot>
{
    public void Configure(EntityTypeBuilder<ShipmentCustomsSnapshot> builder)
    {
        builder.ToTable("ShipmentCustomsSnapshots", table =>
        {
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshot_Version", "\"SnapshotVersion\" = 1 AND \"CarrierNeutralDtoVersion\" = 1");
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshot_Countries", "char_length(\"DestinationCountryIso2\") = 2 AND char_length(\"OriginCountryIso2\") = 2");
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshot_GrossWeight", "\"ShipmentGrossWeight\" > 0");
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshot_Fingerprint", "char_length(\"SnapshotFingerprintSha256\") = 64");
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshot_Frozen", "\"IsRegistrationFrozen\" = TRUE");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DestinationCountryIso2).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.OriginCountryIso2).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.ShipmentGrossWeight).HasPrecision(18, 6);
        builder.Property(x => x.ShipmentGrossWeightUnit).IsRequired().HasMaxLength(16);
        builder.Property(x => x.SnapshotFingerprintSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.ShipmentId }).IsUnique().HasDatabaseName("UQ_ShipmentCustomsSnapshot_Shipment");
        builder.HasIndex(x => new { x.TenantId, x.OrderId }).HasDatabaseName("IX_ShipmentCustomsSnapshot_Order");
    }
}

public sealed class ShipmentCustomsSnapshotLineConfiguration : IEntityTypeConfiguration<ShipmentCustomsSnapshotLine>
{
    public void Configure(EntityTypeBuilder<ShipmentCustomsSnapshotLine> builder)
    {
        builder.ToTable("ShipmentCustomsSnapshotLines", table =>
        {
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshotLine_Value", "\"DeclaredValue\" > 0");
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshotLine_Quantity", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshotLine_NetWeight", "\"NetWeight\" > 0");
            table.HasCheckConstraint("CK_ShipmentCustomsSnapshotLine_Country", "char_length(\"CountryOfOriginIso2\") = 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.HsCode).IsRequired().HasMaxLength(32);
        builder.Property(x => x.CommodityDescription).IsRequired().HasMaxLength(512);
        builder.Property(x => x.CountryOfOriginIso2).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.CountryOfManufactureIso2).HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.DeclaredValue).HasPrecision(18, 4);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.QuantityUnit).IsRequired().HasMaxLength(32);
        builder.Property(x => x.NetWeight).HasPrecision(18, 6);
        builder.Property(x => x.NetWeightUnit).IsRequired().HasMaxLength(16);
        builder.Property(x => x.ExportClassification).HasMaxLength(128);
        builder.Property(x => x.SourceEvidenceFingerprintSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.ShipmentCustomsSnapshotId, x.OrderItemId }).IsUnique().HasDatabaseName("UQ_ShipmentCustomsSnapshotLine_OrderItem");
        builder.HasOne(x => x.Snapshot).WithMany(x => x.Lines).HasForeignKey(x => x.ShipmentCustomsSnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomsAdmissionAuditConfiguration : IEntityTypeConfiguration<CustomsAdmissionAudit>
{
    public void Configure(EntityTypeBuilder<CustomsAdmissionAudit> builder)
    {
        builder.ToTable("CustomsAdmissionAudit");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.SourceEvidenceFingerprintSha256).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.FailureCode).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc }).HasDatabaseName("IX_CustomsAdmissionAudit_Tenant_Timeline");
        builder.HasIndex(x => new { x.ShipmentId, x.Action, x.OccurredAtUtc }).HasDatabaseName("IX_CustomsAdmissionAudit_Shipment_Action");
    }
}

public sealed class CustomsEvidenceHistoryConfiguration : IEntityTypeConfiguration<CustomsEvidenceHistory>
{
    public void Configure(EntityTypeBuilder<CustomsEvidenceHistory> builder)
    {
        builder.ToTable("CustomsEvidenceHistory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.CustomsEvidenceId, x.VersionNumber }).HasDatabaseName("IX_CustomsEvidenceHistory_Evidence_Version");
    }
}

public sealed class CustomsEvidenceFingerprintConfiguration : IEntityTypeConfiguration<CustomsEvidenceFingerprint>
{
    public void Configure(EntityTypeBuilder<CustomsEvidenceFingerprint> builder)
    {
        builder.ToTable("CustomsEvidenceFingerprints", table =>
            table.HasCheckConstraint("CK_CustomsEvidenceFingerprint_Length", "char_length(\"FingerprintSha256\") = 64"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FingerprintSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.TenantId, x.FingerprintSha256 }).HasDatabaseName("IX_CustomsEvidenceFingerprints_Fingerprint");
        builder.HasIndex(x => x.CustomsEvidenceVersionId).IsUnique().HasDatabaseName("UQ_CustomsEvidenceFingerprints_Version");
    }
}

public sealed class CustomsEvidenceApprovalConfiguration : IEntityTypeConfiguration<CustomsEvidenceApproval>
{
    public void Configure(EntityTypeBuilder<CustomsEvidenceApproval> builder)
    {
        builder.ToTable("CustomsEvidenceApprovals", table =>
            table.HasCheckConstraint("CK_CustomsEvidenceApproval_Checksum", "char_length(\"ValidationChecksumSha256\") = 64"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ValidationChecksumSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ApprovedByActor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.CustomsEvidenceId, x.CustomsEvidenceVersionId }).HasDatabaseName("IX_CustomsEvidenceApprovals_Evidence_Version");
    }
}

public sealed class CustomsEvidencePublicationConfiguration : IEntityTypeConfiguration<CustomsEvidencePublication>
{
    public void Configure(EntityTypeBuilder<CustomsEvidencePublication> builder)
    {
        builder.ToTable("CustomsEvidencePublications", table =>
        {
            table.HasCheckConstraint("CK_CustomsEvidencePublication_Action", "\"Action\" IN ('Published','Retired','Superseded')");
            table.HasCheckConstraint("CK_CustomsEvidencePublication_Checksum", "char_length(\"ValidationChecksumSha256\") = 64");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ValidationChecksumSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.PublishedByActor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.ProductVariantId, x.Action, x.OccurredAtUtc }).HasDatabaseName("IX_CustomsEvidencePublications_Product_Timeline");
        builder.HasIndex(x => new { x.TenantId, x.CustomsEvidenceId, x.CustomsEvidenceVersionId }).HasDatabaseName("IX_CustomsEvidencePublications_Evidence_Version");
    }
}

public sealed class CustomsPublicationLockConfiguration : IEntityTypeConfiguration<CustomsPublicationLock>
{
    public void Configure(EntityTypeBuilder<CustomsPublicationLock> builder)
    {
        builder.ToTable("CustomsPublicationLocks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LockKey).IsRequired().HasMaxLength(256);
        builder.Property(x => x.LockedByActor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.LockKey }).IsUnique().HasDatabaseName("UQ_CustomsPublicationLock_Key");
    }
}

public sealed class CustomsAdministrationAuditConfiguration : IEntityTypeConfiguration<CustomsAdministrationAudit>
{
    public void Configure(EntityTypeBuilder<CustomsAdministrationAudit> builder)
    {
        builder.ToTable("CustomsAdministrationAudit");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Role).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(512);
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ValidationChecksumSha256).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.FailureCode).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc }).HasDatabaseName("IX_CustomsAdministrationAudit_Tenant_Timeline");
        builder.HasIndex(x => new { x.CustomsEvidenceId, x.Action, x.OccurredAtUtc }).HasDatabaseName("IX_CustomsAdministrationAudit_Evidence_Action");
    }
}
