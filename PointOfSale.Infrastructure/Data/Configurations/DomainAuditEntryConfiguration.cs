using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the DomainAuditEntry entity.
///
/// DOMAIN-1.5 Section 4.4 — Persistence Freeze Specification.
/// Configuration MUST match DOMAIN-1.5 exactly. No redesign.
///
/// Grounded In:
///   - DOMAIN-1.5 Section 10 (Index Strategy): audit indexes
///   - DOMAIN-1.5 Section 4.4 (Entity fields)
///   - DOMAIN-1.5 CPD-4: Implements ITenantOwned
///   - DOMAIN-1.5 CPD-13: Written in same transaction as Domain state change
///   - Mirrors StoreConfigAuditEntry pattern (StoreConfigAuditEntry.cs:14-81)
///   - DOMAIN-1.5 Section 12.3: Append-only (no UPDATE, no DELETE)
/// </summary>
public sealed class DomainAuditEntryConfiguration : IEntityTypeConfiguration<DomainAuditEntry>
{
    public void Configure(EntityTypeBuilder<DomainAuditEntry> builder)
    {
        builder.ToTable("DomainAuditEntries");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(a => a.DomainId)
            .IsRequired();

        // CPD-4: Denormalized TenantId for query performance
        builder.Property(a => a.TenantId)
            .IsRequired();

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.ActorType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.ActorUserId)
            .IsRequired(false);

        // CPD-6: jsonb for snapshot columns
        builder.Property(a => a.BeforeSnapshotJson)
            .HasColumnType("jsonb")
            .IsRequired(false);

        // AfterSnapshotJson is unique per state transition (DOMAIN-1.5 Section 4.4)
        builder.Property(a => a.AfterSnapshotJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(a => a.TraceId)
            .IsRequired(false)
            .HasMaxLength(128);

        builder.Property(a => a.IpAddress)
            .IsRequired(false)
            .HasMaxLength(45);

        builder.Property(a => a.UserAgent)
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(a => a.TimestampUtc)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        // -------------------------------------------------------------------------
        // Relationship: DomainAuditEntry → Domain (many-to-one, No-Op/Preserve)
        // DOMAIN-1.5 Section 7.1: Audit entries are NEVER deleted when
        // Domain is deleted. DomainId FK is retained (not nullable).
        // -------------------------------------------------------------------------
        builder.HasOne(a => a.Domain)
            .WithMany(d => d.AuditEntries)
            .HasForeignKey(a => a.DomainId)
            .HasConstraintName("FK_DomainAuditEntries_DomainId")
            .OnDelete(DeleteBehavior.Restrict);

        // -------------------------------------------------------------------------
        // Indexes (DOMAIN-1.5 Section 10.2, 10.5)
        // -------------------------------------------------------------------------

        // 1. PK_DomainAuditEntries — clustered PK on Id (handled by HasKey)

        // 2. IX_DomainAuditEntries_DomainId — audit trail lookup
        builder.HasIndex(a => a.DomainId)
            .HasDatabaseName("IX_DomainAuditEntries_DomainId");

        // 3. IX_DomainAuditEntries_TenantId_TimestampUtc — tenant-scoped audit queries
        builder.HasIndex(a => new { a.TenantId, a.TimestampUtc })
            .HasDatabaseName("IX_DomainAuditEntries_TenantId_TimestampUtc");

        // 4. IX_DomainAuditEntries_TimestampUtc — background: retention GC
        builder.HasIndex(a => a.TimestampUtc)
            .HasDatabaseName("IX_DomainAuditEntries_TimestampUtc");

        // 5. IX_DomainAuditEntries_TraceId — correlation tracing
        builder.HasIndex(a => a.TraceId)
            .HasDatabaseName("IX_DomainAuditEntries_TraceId");

        // 6. IX_DomainAuditEntries_Action — action-type filtering
        builder.HasIndex(a => a.Action)
            .HasDatabaseName("IX_DomainAuditEntries_Action");

        // -------------------------------------------------------------------------
        // Append-only enforcement (DOMAIN-1.5 Section 12.3)
        // No UPDATE or DELETE permitted. ApplicationDbContext blocks EF mutations
        // via GuardAppendOnlyEvidence<DomainAuditEntry>(); the DOMAIN-2C migration
        // installs a PostgreSQL trigger to block direct UPDATE/DELETE as well.
        // -------------------------------------------------------------------------
    }
}
