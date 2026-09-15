using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class TreasuryInitialCapitalizationAllocationConfiguration : IEntityTypeConfiguration<TreasuryInitialCapitalizationAllocation>
{
    public void Configure(EntityTypeBuilder<TreasuryInitialCapitalizationAllocation> builder)
    {
        builder.ToTable("TreasuryInitialCapitalizationAllocations", table =>
            table.HasCheckConstraint("CK_TreasuryInitialCapitalizationAllocation_Amount", "\"Amount\" > 0"));
        builder.HasKey(allocation => allocation.Id);
        builder.HasIndex(allocation => new
            {
                allocation.TenantId,
                allocation.CapitalizationId,
                allocation.TreasuryAccountId
            })
            .IsUnique()
            .HasDatabaseName("IX_TreasuryInitialCapitalizationAllocation_Destination");
        builder.Property(allocation => allocation.Amount).HasPrecision(18, 2);
        builder.Property(allocation => allocation.TreasuryAccountNameSnapshot).HasMaxLength(100).IsRequired();
        builder.Property(allocation => allocation.LedgerAccountCodeSnapshot).HasMaxLength(20).IsRequired();
        builder.Property(allocation => allocation.LedgerAccountNameSnapshot).HasMaxLength(200).IsRequired();

        builder.HasOne(allocation => allocation.Capitalization)
            .WithMany(document => document.Allocations)
            .HasForeignKey(allocation => new { allocation.TenantId, allocation.CapitalizationId })
            .HasPrincipalKey(document => new { document.TenantId, document.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TreasuryAccount>()
            .WithMany()
            .HasForeignKey(allocation => new { allocation.TenantId, allocation.TreasuryAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(allocation => new { allocation.TenantId, allocation.LedgerAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
