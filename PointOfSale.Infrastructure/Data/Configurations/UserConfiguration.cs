using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        // Users Management (Wave 1): server-authoritative canonical identity.
        // Uniqueness is enforced on the normalized columns only; the legacy
        // case-sensitive Username/Email unique indexes are replaced.
        builder.Property(u => u.NormalizedUsername)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(200);

        // Optimistic concurrency for User.RowVersion is mapped in
        // ApplicationDbContext.OnModelCreating per provider: PostgreSQL uses
        // the xmin system column (StoreConfig precedent); other providers
        // (SQLite/test) use a regular client-bumped concurrency column
        // because xmin value generation is PostgreSQL-only.

        builder.Property(u => u.DisableReason)
            .HasMaxLength(500);

        // InvitedBy/DisabledBy are plain actor-attribution scalars (no FK):
        // historical actor deletion must not cascade or null security evidence.

        // The single current POS refresh-session binding is rotated atomically.
        // Competing refresh/login/logout writes must not silently resurrect a session.
        builder.Property(u => u.RefreshToken)
            .IsConcurrencyToken();

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Phone)
            .HasMaxLength(50);

        builder.Ignore(u => u.FullName);

        // Tenant ownership: restrictive FK. Orphan users are a preflight
        // blocker, never silently repaired or deleted.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.NormalizedUsername)
            .IsUnique()
            .HasDatabaseName("IX_Users_NormalizedUsername");

        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("IX_Users_NormalizedEmail");

        // Keep the pre-existing tenant lookup index represented in EF metadata
        // as well as in the deployed schema. A distinct model index name is
        // required because the filtered sole-Owner index uses the same column.
        builder.HasIndex(u => u.TenantId, "IX_Users_TenantId")
            .HasDatabaseName("IX_Users_TenantId");

        // Tenant list/summary queries filter on tenant plus soft-delete,
        // active, and role flags.
        builder.HasIndex(u => new { u.TenantId, u.IsDeleted, u.IsActive, u.Role })
            .HasDatabaseName("IX_Users_TenantList");

        // At most one non-deleted Owner per tenant at the database level.
        // Service transactions enforce at least one (sole-owner invariant).
        // UserRole.Owner is stored as integer 0 (first enum member).
        builder.HasIndex(u => u.TenantId, "IX_Users_TenantId_SoleOwner")
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE AND \"Role\" = 0")
            .HasDatabaseName("IX_Users_TenantId_SoleOwner");
    }
}
