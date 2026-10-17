using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class ShippingWebhookClaimConfiguration : IEntityTypeConfiguration<ShippingWebhookClaim>
{
    public void Configure(EntityTypeBuilder<ShippingWebhookClaim> entity)
    {
        entity.ToTable("ShippingWebhookClaims", table =>
        {
            table.HasCheckConstraint("CK_ShippingWebhookClaims_Generation", "\"Generation\" > 0");
            table.HasCheckConstraint("CK_ShippingWebhookClaims_Outcome",
                "\"Outcome\" IN ('Processing','Completed','Failed') AND ((\"Outcome\" = 'Processing' AND \"FinishedAt\" IS NULL) OR (\"Outcome\" <> 'Processing' AND \"FinishedAt\" IS NOT NULL))");
        });
        entity.HasKey(e => new { e.LogId, e.Generation });
        entity.HasIndex(e => new { e.LogId, e.Owner }).IsUnique();
        entity.HasOne<ShippingWebhookLog>().WithMany().HasForeignKey(e => e.LogId).OnDelete(DeleteBehavior.Restrict);
        entity.Property(e => e.Outcome).HasMaxLength(20).IsRequired();
        entity.Property(e => e.Error).HasMaxLength(1000);
    }
}
