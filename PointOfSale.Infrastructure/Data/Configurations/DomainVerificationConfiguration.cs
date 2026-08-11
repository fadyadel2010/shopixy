using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the DomainVerification entity.
///
/// DOMAIN-1.5 Section 4.2 — Persistence Freeze Specification.
/// Configuration MUST match DOMAIN-1.5 exactly. No redesign.
///
/// Grounded In:
///   - DOMAIN-1.5 Section 10.4/10.6 (Verification Indexes)
///   - DOMAIN-1.5 Section 4.2 (Entity fields)
///   - DOMAIN-1.5 Section 7.1 (Relationships: DomainVerification → Domain, Cascade soft)
///   - DOMAIN-1.5 CPD-5: Soft-deleted with parent Domain
/// </summary>
public sealed class DomainVerificationConfiguration : IEntityTypeConfiguration<DomainVerification>
{
    public void Configure(EntityTypeBuilder<DomainVerification> builder)
    {
        builder.ToTable("DomainVerifications");

        builder.HasKey(dv => dv.Id);

        builder.Property(dv => dv.Id)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(dv => dv.DomainId)
            .IsRequired();

        builder.Property(dv => dv.Method)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(dv => dv.Token)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(dv => dv.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(dv => dv.AttemptedAtUtc)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(dv => dv.VerifiedAtUtc)
            .IsRequired(false);

        builder.Property(dv => dv.FailureReason)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(dv => dv.CorrelationId)
            .IsRequired(false)
            .HasMaxLength(128);

        builder.Property(dv => dv.ResolverEndpoint)
            .IsRequired(false)
            .HasMaxLength(128);

        builder.Property(dv => dv.LatencyMilliseconds)
            .IsRequired(false);

        builder.Property(dv => dv.Attempt)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(dv => dv.Ttl)
            .IsRequired(false);

        builder.Property(dv => dv.ResolvedValue)
            .IsRequired(false)
            .HasMaxLength(1024);

        builder.Property(dv => dv.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(dv => dv.UpdatedAt)
            .IsRequired(false);

        // CPD-5: IsDeleted — soft-deleted with parent Domain.
        // Query filter configured in ApplicationDbContext: WHERE IsDeleted = false
        // Cascade soft-delete through parent Domain is handled at the application layer
        // via the Domain query filter (System/Development exempted).
        builder.Property(dv => dv.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // -------------------------------------------------------------------------
        // Relationship: DomainVerification → Domain (many-to-one, Cascade soft)
        // DOMAIN-1.5 Section 7.1: Cascade (soft) — child verifications
        // are soft-deleted when parent Domain is soft-deleted.
        // -------------------------------------------------------------------------
        builder.HasOne(dv => dv.Domain)
            .WithMany(d => d.Verifications)
            .HasForeignKey(dv => dv.DomainId)
            .HasConstraintName("FK_DomainVerifications_DomainId")
            .OnDelete(DeleteBehavior.Cascade);

        // -------------------------------------------------------------------------
        // Indexes (DOMAIN-1.5 Section 10.4, 10.6)
        // -------------------------------------------------------------------------

        // 1. PK_DomainVerifications — clustered PK on Id (handled by HasKey)

        // 2. IX_DomainVerifications_DomainId — child lookup
        builder.HasIndex(dv => dv.DomainId)
            .HasDatabaseName("IX_DomainVerifications_DomainId");

        // 3. IX_DomainVerifications_DomainId_Status — find pending/failed attempts
        builder.HasIndex(dv => new { dv.DomainId, dv.Status })
            .HasDatabaseName("IX_DomainVerifications_DomainId_Status");

        // 4. IX_DomainVerifications_AttemptedAtUtc — background job: cleanup
        builder.HasIndex(dv => dv.AttemptedAtUtc)
            .HasDatabaseName("IX_DomainVerifications_AttemptedAtUtc");

        // 5. IX_DomainVerifications_IsDeleted — filtered index for query filter
        builder.HasIndex(dv => dv.IsDeleted)
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("IX_DomainVerifications_IsDeleted");

        //     UQ_DomainVerifications_DomainId_Method — filtered UNIQUE
        //     WHERE Status = 0 (Pending) — one active verification attempt per method
        builder.HasIndex(dv => new { dv.DomainId, dv.Method })
            .IsUnique()
            .HasFilter("\"Status\" = 0")
            .HasDatabaseName("UQ_DomainVerifications_DomainId_Method");

        builder.HasIndex(dv => new { dv.DomainId, dv.Attempt })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UQ_DomainVerifications_DomainId_Attempt");

    }
}
