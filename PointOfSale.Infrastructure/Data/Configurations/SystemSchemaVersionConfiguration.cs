using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class SystemSchemaVersionConfiguration : IEntityTypeConfiguration<SystemSchemaVersion>
{
    public void Configure(EntityTypeBuilder<SystemSchemaVersion> builder)
    {
        builder.ToTable("SystemSchemaVersions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Component)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Version)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.AppliedAt)
            .IsRequired();

        builder.Property(x => x.DetailsJson)
            .HasColumnType("text");

        builder.HasIndex(x => x.Component)
            .IsUnique()
            .HasDatabaseName("UQ_SystemSchemaVersions_Component");
    }
}
