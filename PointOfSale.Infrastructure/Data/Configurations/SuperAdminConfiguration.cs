using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.SuperAdmin.Models;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class SuperAdminConfiguration : IEntityTypeConfiguration<SuperAdminUser>
{
    public void Configure(EntityTypeBuilder<SuperAdminUser> builder)
    {
        builder.ToTable("SuperAdmins");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(x => x.PasswordHash)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.RefreshTokenHash)
            .HasMaxLength(256);

        builder.Property(x => x.TokenVersion)
            .HasDefaultValue(0);

        builder.HasIndex(x => x.Email)
            .IsUnique();

        builder.HasIndex(x => x.IsSystem)
            .HasFilter("\"IsSystem\" = TRUE")
            .IsUnique();
    }
}
