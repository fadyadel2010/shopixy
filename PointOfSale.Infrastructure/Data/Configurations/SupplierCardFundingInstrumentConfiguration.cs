using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class SupplierCardFundingInstrumentConfiguration : IEntityTypeConfiguration<SupplierCardFundingInstrument>
{
    public void Configure(EntityTypeBuilder<SupplierCardFundingInstrument> builder)
    {
        builder.ToTable("SupplierCardFundingInstruments", table =>
        {
            table.HasCheckConstraint("CK_SupplierCardFundingInstrument_AuthorityVersion", "\"AuthorityVersion\" > 0");
        });
        builder.HasKey(instrument => instrument.Id);
        builder.HasAlternateKey(instrument => new { instrument.TenantId, instrument.Id })
            .HasName("AK_SupplierCardFundingInstruments_TenantId_Id");
        builder.HasIndex(instrument => new { instrument.TenantId, instrument.LedgerAccountId }).IsUnique();
        builder.HasIndex(instrument => new { instrument.TenantId, instrument.IsActive });
        builder.Property(instrument => instrument.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(instrument => instrument.Issuer).HasMaxLength(100);
        builder.Property(instrument => instrument.Last4).HasMaxLength(4);
        builder.Property(instrument => instrument.AuthorityVersion).HasDefaultValue(1);
        builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasOne(instrument => instrument.LedgerAccount)
            .WithMany()
            .HasForeignKey(instrument => new { instrument.TenantId, instrument.LedgerAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(instrument => instrument.FundingSourceTreasuryAccount)
            .WithMany()
            .HasForeignKey(instrument => new { instrument.TenantId, instrument.FundingSourceTreasuryAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
