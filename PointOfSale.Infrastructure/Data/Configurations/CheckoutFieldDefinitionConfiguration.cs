using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CheckoutFieldDefinitionConfiguration : IEntityTypeConfiguration<CheckoutFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CheckoutFieldDefinition> builder)
    {
        builder.ToTable("CheckoutFieldDefinitions", table =>
        {
            table.HasCheckConstraint(
                "CK_CheckoutFieldDefinitions_SystemFieldKey",
                "(\"FieldKind\" = 'System' AND \"SystemFieldKey\" IS NOT NULL AND LENGTH(TRIM(\"SystemFieldKey\")) > 0) OR " +
                "(\"FieldKind\" = 'Custom' AND \"SystemFieldKey\" IS NULL)");

            table.HasCheckConstraint(
                "CK_CheckoutFieldDefinitions_RequiredEnabled",
                "NOT \"IsRequired\" OR \"IsEnabled\"");
        });

        builder.HasKey(field => field.Id);

        builder.Property(field => field.FieldKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(field => field.FieldType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(field => field.SystemFieldKey)
            .HasMaxLength(64);

        builder.Property(field => field.Label)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(field => field.HelpText)
            .HasMaxLength(500);

        builder.Property(field => field.DisplayOrder)
            .IsRequired();
        builder.Property(field => field.ConfigurationSchemaVersion)
            .IsRequired()
            .HasDefaultValue(1);
        builder.Property(field => field.ConfigurationJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("{}");
        builder.Property(field => field.CreatedAt)
            .IsRequired();

        builder.HasOne(field => field.Tenant)
            .WithMany()
            .HasForeignKey(field => field.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(field => field.TenantId)
            .HasDatabaseName("IX_CheckoutFieldDefinitions_TenantId");
        builder.HasIndex(field => new { field.TenantId, field.DisplayOrder })
            .HasDatabaseName("IX_CheckoutFieldDefinitions_TenantId_DisplayOrder");
        builder.HasIndex(field => new { field.TenantId, field.IsDeleted })
            .HasDatabaseName("IX_CheckoutFieldDefinitions_TenantId_IsDeleted");
        builder.HasIndex(field => new { field.TenantId, field.SystemFieldKey })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"SystemFieldKey\" IS NOT NULL")
            .HasDatabaseName("UX_CheckoutFieldDefinitions_Tenant_SystemFieldKey_Active");
    }
}
