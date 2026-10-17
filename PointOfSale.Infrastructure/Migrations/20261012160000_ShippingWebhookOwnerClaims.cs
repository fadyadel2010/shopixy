using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PointOfSale.Infrastructure.Migrations;

// Reuse the immutable parcel target plus its following postal delta, not the mutable snapshot.
[Migration("20261012160000_ShippingWebhookOwnerClaims")]
public sealed class ShippingWebhookOwnerClaims : ShipmentParcelRequestEvidence
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ShippingWebhookClaims", columns: table => new
        {
            LogId = table.Column<Guid>(type: "uuid", nullable: false),
            Generation = table.Column<long>(type: "bigint", nullable: false),
            TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            Owner = table.Column<Guid>(type: "uuid", nullable: false),
            AcquiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ShippingWebhookClaims", x => new { x.LogId, x.Generation });
            table.ForeignKey("FK_ShippingWebhookClaims_ShippingWebhookLogs_LogId", x => x.LogId,
                "ShippingWebhookLogs", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_ShippingWebhookClaims_Generation", "\"Generation\" > 0");
            table.CheckConstraint("CK_ShippingWebhookClaims_Outcome", "\"Outcome\" IN ('Processing','Completed','Failed') AND ((\"Outcome\" = 'Processing' AND \"FinishedAt\" IS NULL) OR (\"Outcome\" <> 'Processing' AND \"FinishedAt\" IS NOT NULL))");
        });
        migrationBuilder.CreateIndex("IX_ShippingWebhookClaims_LogId_Owner", "ShippingWebhookClaims", new[] { "LogId", "Owner" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("ShippingWebhookClaims");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        base.BuildTargetModel(modelBuilder);
        modelBuilder.Entity("PointOfSale.Core.Entities.Geographic.AddressAdmissionEvidence")
            .Property<string>("PostalCode").HasMaxLength(32).HasColumnType("character varying(32)");
        AddClaimTarget(modelBuilder);
    }

    internal static void AddClaimTarget(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("PointOfSale.Core.Entities.ShippingWebhookClaim", b =>
        {
            b.Property<Guid>("LogId").HasColumnType("uuid");
            b.Property<long>("Generation").HasColumnType("bigint");
            b.Property<Guid>("TenantId").HasColumnType("uuid");
            b.Property<Guid>("Owner").HasColumnType("uuid");
            b.Property<DateTime>("AcquiredAt").HasColumnType("timestamp with time zone");
            b.Property<DateTime>("LeaseUntil").HasColumnType("timestamp with time zone");
            b.Property<DateTime?>("FinishedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Outcome").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Error").HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.HasKey("LogId", "Generation");
            b.HasIndex("LogId", "Owner").IsUnique();
            b.ToTable("ShippingWebhookClaims", table =>
            {
                table.HasCheckConstraint("CK_ShippingWebhookClaims_Generation", "\"Generation\" > 0");
                table.HasCheckConstraint("CK_ShippingWebhookClaims_Outcome", "\"Outcome\" IN ('Processing','Completed','Failed') AND ((\"Outcome\" = 'Processing' AND \"FinishedAt\" IS NULL) OR (\"Outcome\" <> 'Processing' AND \"FinishedAt\" IS NOT NULL))");
            });
            b.HasOne("PointOfSale.Core.Entities.ShippingWebhookLog", null).WithMany().HasForeignKey("LogId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
