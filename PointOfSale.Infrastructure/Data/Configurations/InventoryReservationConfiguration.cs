using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("InventoryReservations", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryReservations_ReservedQuantity_NonNegative",
                "\"ReservedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_InventoryReservations_CommittedQuantity_NonNegative",
                "\"CommittedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_InventoryReservations_ReleasedQuantity_NonNegative",
                "\"ReleasedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_InventoryReservations_ReturnedQuantity_NonNegative",
                "\"ReturnedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_InventoryReservations_DamagedReturnedQuantity_NonNegative",
                "\"DamagedReturnedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_InventoryReservations_ReservedFlow_Valid",
                "\"CommittedQuantity\" + \"ReleasedQuantity\" <= \"ReservedQuantity\"");
            table.HasCheckConstraint(
                "CK_InventoryReservations_Returns_DoNotExceedCommitted",
                "\"ReturnedQuantity\" + \"DamagedReturnedQuantity\" <= \"CommittedQuantity\"");
        });

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.ReservedQuantity)
            .HasPrecision(18, 2);

        builder.Property(reservation => reservation.CommittedQuantity)
            .HasPrecision(18, 2);

        builder.Property(reservation => reservation.ReleasedQuantity)
            .HasPrecision(18, 2);

        builder.Property(reservation => reservation.ReturnedQuantity)
            .HasPrecision(18, 2);

        builder.Property(reservation => reservation.DamagedReturnedQuantity)
            .HasPrecision(18, 2);

        builder.HasIndex(reservation => new
            {
                reservation.TenantId,
                reservation.OrderItemId,
                reservation.VariantId,
                reservation.WarehouseId
            })
            .HasDatabaseName("IX_InventoryReservations_TenantId_OrderItemId_VariantId_WarehouseId");

        builder.HasIndex(reservation => new
            {
                reservation.TenantId,
                reservation.Status,
                reservation.ExpiresAtUtc
            })
            .HasDatabaseName("IX_InventoryReservations_TenantId_Status_ExpiresAtUtc");
    }
}
