using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CanonicalIdentityLinkConfiguration : IEntityTypeConfiguration<CanonicalIdentityLink>
{
    public void Configure(EntityTypeBuilder<CanonicalIdentityLink> builder)
    {
        builder.ToTable("CanonicalIdentityLinks");
        builder.HasKey(link => link.Id);

        builder.Property(link => link.CanonicalIdentityId)
            .IsRequired();

        builder.Property(link => link.SubjectType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(link => link.SubjectId)
            .IsRequired();

        builder.Property(link => link.SourceSystem)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(link => link.DisplayKey)
            .HasMaxLength(255);

        builder.Property(link => link.LinkedAtUtc)
            .IsRequired();

        builder.HasIndex(link => link.CanonicalIdentityId)
            .HasDatabaseName("IX_CanonicalIdentityLinks_CanonicalIdentityId");

        // Filtered unique indexes are defined in migrations and filters are applied in ApplicationDbContext.OnModelCreating for provider-agnostic support.
        builder.HasIndex(link => new { link.TenantId, link.SubjectType, link.SubjectId })
            .IsUnique()
            .HasDatabaseName("UX_CanonicalIdentityLinks_Tenant_Subject_Active");

        builder.HasIndex(link => new { link.SubjectType, link.SubjectId })
            .IsUnique()
            .HasDatabaseName("UX_CanonicalIdentityLinks_Global_Subject_Active");

        builder.HasIndex(link => new { link.TenantId, link.SubjectType, link.SubjectId })
            .HasDatabaseName("IX_CanonicalIdentityLinks_Tenant_Subject");

        builder.HasIndex(link => new { link.SourceSystem, link.DisplayKey })
            .HasDatabaseName("IX_CanonicalIdentityLinks_Source_DisplayKey");
    }
}
