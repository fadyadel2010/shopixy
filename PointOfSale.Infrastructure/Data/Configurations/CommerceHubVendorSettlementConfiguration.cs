using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubVendorSettlementConfiguration : IEntityTypeConfiguration<CommerceHubVendorSettlement>
{
    public void Configure(EntityTypeBuilder<CommerceHubVendorSettlement> builder)
    {
        builder.ToTable("CommerceHubVendorSettlements");
        builder.HasKey(entry => entry.Id);

        builder.HasIndex(entry => new { entry.VendorId, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CommerceHubVendorSettlements_VendorId_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.SettlementType, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CommerceHubVendorSettlements_SettlementType_CreatedAtUtc");

        builder.HasIndex(entry => entry.CreatedAtUtc)
            .HasDatabaseName("IX_CommerceHubVendorSettlements_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.VendorId, entry.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey");

        builder.HasIndex(entry => entry.ExternalReference)
            .IsUnique()
            .HasFilter("\"ExternalReference\" IS NOT NULL AND \"ExternalReference\" <> '' AND \"ExternalReference\" NOT LIKE 'legacy:%'")
            .HasDatabaseName("UX_CommerceHubVendorSettlements_ExternalReference");

        builder.Property(entry => entry.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(entry => entry.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(entry => entry.IdempotencyKey)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entry => entry.ExternalReference)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(entry => entry.Notes)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(entry => entry.RecordedByUserEmail)
            .HasMaxLength(320);

        builder.Property(entry => entry.CreatedAtUtc)
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubVendorSettlements_Amount",
                "\"Amount\" > 0");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorSettlements_Currency_EGP",
                "\"Currency\" = 'EGP'");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorSettlements_SettlementType",
                "\"SettlementType\" IN (0, 1, 2)");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorSettlements_Direction",
                "\"Direction\" IN (0, 1)");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank",
                "NULLIF(btrim(\"IdempotencyKey\"), '') IS NOT NULL");

            table.HasCheckConstraint(
                "CK_CommerceHubVendorSettlements_ExternalReference_NotBlank",
                "NULLIF(btrim(\"ExternalReference\"), '') IS NOT NULL");
        });
    }
}
