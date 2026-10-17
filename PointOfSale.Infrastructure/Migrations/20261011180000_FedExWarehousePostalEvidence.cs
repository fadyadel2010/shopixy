using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Migrations;

// DbContextAttribute is inherited from the preceding generated migration.
// Repeating it would give EF two attributes during migration discovery.
[Migration("20261011180000_FedExWarehousePostalEvidence")]
public sealed class FedExWarehousePostalEvidence : ShipmentParcelRequestEvidence
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "PostalCode", table: "AddressAdmissionEvidence", type: "character varying(32)", maxLength: 32, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "PostalCode", table: "AddressAdmissionEvidence");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // Reuse the immediately preceding immutable generated target, rather than
        // copying 26,000 unchanged lines or depending on the mutable current snapshot.
        base.BuildTargetModel(modelBuilder);
        modelBuilder.Entity("PointOfSale.Core.Entities.Geographic.AddressAdmissionEvidence")
            .Property<string>("PostalCode").HasMaxLength(32).HasColumnType("character varying(32)");
    }
}
