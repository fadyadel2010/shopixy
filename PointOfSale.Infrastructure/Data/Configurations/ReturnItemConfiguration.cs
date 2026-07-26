using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class ReturnItemConfiguration : IEntityTypeConfiguration<ReturnItem>
{
    public void Configure(EntityTypeBuilder<ReturnItem> builder)
    {
        builder.Property(item => item.CostPriceSnapshot)
            .HasPrecision(18, 6);

        builder.Property(item => item.CostAmountSnapshot)
            .HasPrecision(18, 2);
    }
}
