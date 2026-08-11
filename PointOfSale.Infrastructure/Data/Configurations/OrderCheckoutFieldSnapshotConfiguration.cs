using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class OrderCheckoutFieldSnapshotConfiguration : IEntityTypeConfiguration<OrderCheckoutFieldSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderCheckoutFieldSnapshot> builder)
    {
        builder.ToTable("OrderCheckoutFieldSnapshots", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderCheckoutFieldSnapshots_SystemFieldKey",
                "(\"FieldKind\" = 'System' AND \"SystemFieldKey\" IS NOT NULL AND LENGTH(TRIM(\"SystemFieldKey\")) > 0) OR " +
                "(\"FieldKind\" = 'Custom' AND \"SystemFieldKey\" IS NULL)");

            table.HasCheckConstraint(
                "CK_OrderCheckoutFieldSnapshots_ConfigurationSchemaVersion_Positive",
                "\"ConfigurationSchemaVersion\" > 0");
        });

        builder.HasKey(snapshot => snapshot.Id);

        builder.Property(snapshot => snapshot.FieldKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(snapshot => snapshot.FieldType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(snapshot => snapshot.SystemFieldKey)
            .HasMaxLength(64);

        builder.Property(snapshot => snapshot.Label)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(snapshot => snapshot.HelpText)
            .HasMaxLength(500);

        builder.Property(snapshot => snapshot.ConfigurationJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("{}");

        builder.Property(snapshot => snapshot.ValueJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("null");

        builder.Property(snapshot => snapshot.CreatedAt)
            .IsRequired();

        builder.HasOne(snapshot => snapshot.Order)
            .WithMany(order => order.CheckoutFieldSnapshots)
            .HasForeignKey(snapshot => new { snapshot.TenantId, snapshot.OrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(snapshot => snapshot.OrderId)
            .HasDatabaseName("IX_OrderCheckoutFieldSnapshots_OrderId");
        builder.HasIndex(snapshot => new { snapshot.TenantId, snapshot.OrderId })
            .HasDatabaseName("IX_OrderCheckoutFieldSnapshots_TenantId_OrderId");
        builder.HasIndex(snapshot => new { snapshot.TenantId, snapshot.OrderId, snapshot.DisplayOrder })
            .HasDatabaseName("IX_OrderCheckoutFieldSnapshots_TenantId_OrderId_DisplayOrder");
        builder.HasIndex(snapshot => new { snapshot.TenantId, snapshot.OrderId, snapshot.CheckoutFieldDefinitionId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UX_OrderCheckoutFieldSnapshots_Tenant_Order_FieldDefinition_Active");
    }
}
