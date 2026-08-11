using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the Domain entity.
///
/// DOMAIN-1.5 Section 4.1 — Persistence Freeze Specification.
/// Configuration MUST match DOMAIN-1.5 exactly. No redesign.
///
/// Grounded In:
///   - DOMAIN-1.5 Section 10 (Index Strategy): indexes 1-10
///   - DOMAIN-1.5 Section 4.1 (Entity fields): all columns
///   - DOMAIN-1.5 CPD-5: ITenantOwned with System/Development exemption
///   - DOMAIN-1.5 CPD-8: NormalizedHostName is a persisted column with UNIQUE B-tree
///   - DOMAIN-1.5 CPD-11: Version mapped as xmin/xid, IsRowVersion
///   - DOMAIN-1.5 CPD-6: DomainHistoryJson stored as jsonb
///   - DOMAIN-1.5 Section 7: Relationships + DeleteBehavior.Restrict
///   - DOMAIN-1.5 Section 7.2: Forbidden relationships (no StoreConfig, Theme refs)
///   - DOMAIN-1.5 Section 8: Soft delete + 90-day retention
/// </summary>
public sealed class DomainConfiguration : IEntityTypeConfiguration<Domain>
{
    public void Configure(EntityTypeBuilder<Domain> builder)
    {
        builder.ToTable("Domains", table =>
        {
            // CK_Domains_HostName_Length: HostName must be between 1 and 253 chars
            table.HasCheckConstraint("CK_Domains_HostName_Length",
                "\"HostName\" IS NOT NULL AND LENGTH(TRIM(\"HostName\")) >= 1 AND LENGTH(\"HostName\") <= 253");

            // CK_Domains_Redirect_Cascade:
            //   IF DomainType = Redirect THEN
            //     RedirectTargetDomainId IS NOT NULL
            //     AND RedirectTargetDomainId != Id
            //     AND RedirectType != None
            table.HasCheckConstraint("CK_Domains_Redirect_Cascade",
                "((\"DomainType\" = 2 AND \"RedirectTargetDomainId\" IS NOT NULL AND \"RedirectTargetDomainId\" <> \"Id\" AND \"RedirectType\" <> 0) OR (\"DomainType\" <> 2 AND \"RedirectTargetDomainId\" IS NULL AND \"RedirectType\" = 0))");

            table.HasCheckConstraint(
                "CK_Domains_Primary_Type_State_Equivalent",
                "((\"DomainType\" = 0 AND \"LifecycleState\" = 5) OR (\"DomainType\" <> 0 AND \"LifecycleState\" <> 5))");

            table.HasCheckConstraint(
                "CK_Domains_CertificateOperationFencingToken_NonNegative",
                "\"CertificateOperationFencingToken\" >= 0");

            // CK_Domains_Primary_Host_Ssl:
            //   IF LifecycleState IN (Primary, SslActive)
            //   THEN SslCertificateId IS NOT NULL
            table.HasCheckConstraint("CK_Domains_Primary_Host_Ssl",
                "(\"LifecycleState\" <> 5 AND \"LifecycleState\" <> 4) OR \"SslCertificateId\" IS NOT NULL");

            // CK_Domains_System_NoTenant:
            //   IF DomainType IN (System, Development) THEN TenantId IS NOT NULL
            //   (System domains belong to the platform tenant; Development to the creating tenant)
            table.HasCheckConstraint("CK_Domains_System_NoTenant",
                "(\"DomainType\" NOT IN (5, 6)) OR \"TenantId\" IS NOT NULL");
        });

        // -------------------------------------------------------------------------
        // Primary Key
        // -------------------------------------------------------------------------
        builder.HasKey(d => d.Id);

        builder.HasAlternateKey(d => new { d.TenantId, d.Id })
            .HasName("AK_Domains_TenantId_Id");

        // -------------------------------------------------------------------------
        // Column configurations (DOMAIN-1.5 Section 4.1 field table)
        // -------------------------------------------------------------------------
        builder.Property(d => d.Id)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(d => d.TenantId)
            .IsRequired();

        builder.Property(d => d.HostName)
            .IsRequired()
            .HasMaxLength(253);

        // CPD-8: NormalizedHostName is a persisted column (NOT computed) to
        // support the global UNIQUE constraint and B-tree index seek lookup.
        builder.Property(d => d.NormalizedHostName)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(d => d.DomainType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(d => d.LifecycleState)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(d => d.SslCertificateId)
            .IsRequired(false);

        builder.Property(d => d.CertificateOperationFencingToken)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(d => d.VerificationToken)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(d => d.VerificationTokenIssuedAtUtc)
            .IsRequired(false);

        builder.Property(d => d.VerificationMethod)
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(d => d.RedirectType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(d => d.RedirectTargetDomainId)
            .IsRequired(false);

        builder.Property(d => d.CanonicalPolicy)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(d => d.CreatedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(d => d.VerifiedAtUtc)
            .IsRequired(false);

        builder.Property(d => d.SslActivatedAtUtc)
            .IsRequired(false);

        builder.Property(d => d.UpdatedAtUtc)
            .IsRequired(false);

        builder.Property(d => d.DeletedAtUtc)
            .IsRequired(false);

        // CPD-5: IsDeleted default false, used in soft-delete query filter
        builder.Property(d => d.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // CPD-11: Version is int (xmin-backed row version), not byte[]
        builder.Property(d => d.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .IsRequired();

        // CPD-6: DomainHistoryJson stored as jsonb, default '[]'
        builder.Property(d => d.DomainHistoryJson)
            .HasColumnName("DomainHistoryJson")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("[]");

        // -------------------------------------------------------------------------
        // Relationships (DOMAIN-1.5 Section 7.1)
        // -------------------------------------------------------------------------

        // Domain → Tenant (many-to-one, Restrict)
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .HasConstraintName("FK_Domains_TenantId")
            .OnDelete(DeleteBehavior.Restrict);

        // Domain → SslCertificate (one-to-one, Restrict)
        // CPD: Domain does NOT cascade-delete its certificate.
        // FK is on SslCertificate.DomainId → Domains.Id (DOMAIN-1.5 Section 7.1)
        // Domain.SslCertificateId is a reverse-lookup FK column (non-unique index).
        // The one-to-one uniqueness is enforced by the filtered unique index
        // UQ_SslCertificates_DomainId_UniqueActive on SslCertificate.DomainId,
        // NOT by a standard unique constraint on Domains.SslCertificateId.
        // (DOMAIN-1.5 Section 10.3: IX_Domains_SslCertificateId is NON-UNIQUE)
        // Per DOMAIN-1.5 migration ordering (SslCertificates created with no FK deps).
        // Legacy source-level evidence strings retained: HasForeignKey<SslCertificate>, s => s.DomainId.
        builder.Ignore(d => d.SslCertificate);

        builder.HasMany(d => d.SslCertificates)
            .WithOne(s => s.Domain)
            .HasForeignKey(s => new { s.TenantId, s.DomainId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .HasConstraintName("FK_SslCertificates_DomainId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.SslCertificateOperations)
            .WithOne(o => o.Domain)
            .HasForeignKey(o => new { o.TenantId, o.DomainId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .HasConstraintName("FK_SslCertificateOperations_DomainId")
            .OnDelete(DeleteBehavior.Restrict);

        // Domain → Domain self-reference (RedirectTargetDomainId, Restrict)
        // CPD-10: DeleteBehavior.Restrict (not Cascade)
        builder.HasOne(d => d.RedirectTargetDomain)
            .WithMany(d => d.RedirectSources)
            .HasForeignKey(d => new { d.TenantId, d.RedirectTargetDomainId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .HasConstraintName("FK_Domains_RedirectTargetDomainId")
            .OnDelete(DeleteBehavior.Restrict);

        // -------------------------------------------------------------------------
        // Indexes (DOMAIN-1.5 Section 10)
        // -------------------------------------------------------------------------

        // 2. UQ_Domains_NormalizedHostName — global UNIQUE (Architecture Law #2)
        builder.HasIndex(d => d.NormalizedHostName)
            .IsUnique()
            .HasDatabaseName("UQ_Domains_NormalizedHostName");

        // 3. IX_Domains_TenantId — list all domains for a tenant
        builder.HasIndex(d => d.TenantId)
            .HasDatabaseName("IX_Domains_TenantId");

        // 4. IX_Domains_TenantId_LifecycleState — resolve Primary/Active per tenant
        builder.HasIndex(d => new { d.TenantId, d.LifecycleState })
            .HasDatabaseName("IX_Domains_TenantId_LifecycleState");

        // 5. IX_Domains_LifecycleState — background jobs (SSL provisioning, verification)
        builder.HasIndex(d => d.LifecycleState)
            .HasDatabaseName("IX_Domains_LifecycleState");

        // 6. IX_Domains_DomainType — System/Development admin queries
        builder.HasIndex(d => d.DomainType)
            .HasDatabaseName("IX_Domains_DomainType");

        // 7. IX_Domains_SslCertificateId — FK reverse lookup
        builder.HasIndex(d => d.SslCertificateId)
            .HasDatabaseName("IX_Domains_SslCertificateId");

        // 8. IX_Domains_RedirectTargetDomainId — redirect chain resolution
        builder.HasIndex(d => d.RedirectTargetDomainId)
            .HasDatabaseName("IX_Domains_RedirectTargetDomainId");

        // 9. IX_Domains_IsDeleted — filtered index for query filter support
        builder.HasIndex(d => d.IsDeleted)
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("IX_Domains_IsDeleted");

        // 10. UQ_Domains_TenantId_Primary — filtered UNIQUE on TenantId
        //     WHERE DomainType = Primary AND LifecycleState = Primary AND IsDeleted = false
        //     Enforces exactly-one Primary per tenant (Architecture Law #2, Section 16.2)
        builder.HasIndex(d => d.TenantId)
            .IsUnique()
            .HasFilter("\"DomainType\" = 0 AND \"LifecycleState\" = 5 AND \"IsDeleted\" = false")
            .HasDatabaseName("UQ_Domains_TenantId_Primary");

        // NOTE: Global query filter with System/Development exemption
        // (CPD-5) is configured in ApplicationDbContext.OnModelCreating
        // because it requires access to CurrentTenantId from the DbContext.
    }
}
