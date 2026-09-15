using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class TreasuryInitialCapitalizationConfiguration : IEntityTypeConfiguration<TreasuryInitialCapitalization>
{
    public void Configure(EntityTypeBuilder<TreasuryInitialCapitalization> builder)
    {
        builder.ToTable("TreasuryInitialCapitalizations", table =>
        {
            table.HasCheckConstraint("CK_TreasuryInitialCapitalization_Total", "\"TotalAmount\" > 0");
            table.HasCheckConstraint("CK_TreasuryInitialCapitalization_MutationVersion", "\"MutationVersion\" > 0");
            table.HasCheckConstraint(
                "CK_TreasuryInitialCapitalization_OpeningEquity",
                "\"OpeningEquityAccountCodeSnapshot\" = '3010'");
        });

        builder.HasKey(document => document.Id);
        builder.HasAlternateKey(document => new { document.TenantId, document.Id })
            .HasName("AK_TreasuryInitialCapitalizations_TenantId_Id");
        builder.HasIndex(document => new { document.TenantId, document.OperationId })
            .IsUnique()
            .HasDatabaseName("IX_TreasuryInitialCapitalization_Tenant_Operation");
        builder.HasIndex(document => new { document.TenantId, document.DocumentNumber })
            .IsUnique()
            .HasDatabaseName("IX_TreasuryInitialCapitalization_Tenant_Number");
        builder.HasIndex(document => document.TenantId)
            .IsUnique()
            .HasFilter($"\"Status\" IN ({(int)TreasuryInitialCapitalizationStatus.Draft}, {(int)TreasuryInitialCapitalizationStatus.Posted}) AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_TreasuryInitialCapitalization_OneActivePerTenant");
        builder.HasIndex(document => new { document.TenantId, document.PredecessorCapitalizationId })
            .IsUnique()
            .HasFilter("\"PredecessorCapitalizationId\" IS NOT NULL")
            .HasDatabaseName("IX_TreasuryInitialCapitalization_Predecessor");

        builder.Property(document => document.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(document => document.TotalAmount).HasPrecision(18, 2);
        builder.Property(document => document.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(document => document.DeclarationVersion).HasMaxLength(64).IsRequired();
        builder.Property(document => document.EvidenceReference).HasMaxLength(200).IsRequired();
        builder.Property(document => document.Notes).HasMaxLength(1000);
        builder.Property(document => document.OpeningEquityAccountCodeSnapshot).HasMaxLength(20).IsRequired();
        builder.Property(document => document.OpeningEquityAccountNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(document => document.ReversalRequestHash).HasMaxLength(64);
        builder.Property(document => document.ReversalReason).HasMaxLength(500);
        builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(document => new { document.TenantId, document.OpeningEquityLedgerAccountId })
            .HasPrincipalKey(account => new { account.TenantId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TreasuryInitialCapitalization>()
            .WithMany()
            .HasForeignKey(document => new { document.TenantId, document.PredecessorCapitalizationId })
            .HasPrincipalKey(document => new { document.TenantId, document.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
