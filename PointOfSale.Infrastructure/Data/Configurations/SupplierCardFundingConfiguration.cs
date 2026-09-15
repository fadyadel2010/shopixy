using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class SupplierCardFundingConfiguration : IEntityTypeConfiguration<SupplierCardFunding>
{
    public void Configure(EntityTypeBuilder<SupplierCardFunding> builder)
    {
        builder.ToTable("SupplierCardFundings", table =>
            table.HasCheckConstraint("CK_SupplierCardFunding_Amount", "\"Amount\" > 0"));
        builder.HasKey(funding => funding.Id);
        builder.HasIndex(funding => new { funding.TenantId, funding.IdempotencyKey }).IsUnique();
        builder.HasIndex(funding => new { funding.TenantId, funding.FundingInstrumentId, funding.FundingDate });
        builder.Property(funding => funding.Amount).HasPrecision(18, 2);
        builder.Property(funding => funding.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(funding => funding.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(funding => funding.ReferenceNumber).HasMaxLength(100);
        builder.Property(funding => funding.Notes).HasMaxLength(1000);
        builder.HasOne(funding => funding.FundingInstrument)
            .WithMany()
            .HasForeignKey(funding => new { funding.TenantId, funding.FundingInstrumentId })
            .HasPrincipalKey(instrument => new { instrument.TenantId, instrument.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(funding => new { funding.TenantId, funding.FundingLedgerAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TreasuryAccount>()
            .WithMany()
            .HasForeignKey(funding => new { funding.TenantId, funding.FundingSourceTreasuryAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(funding => new { funding.TenantId, funding.FundingSourceLedgerAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
