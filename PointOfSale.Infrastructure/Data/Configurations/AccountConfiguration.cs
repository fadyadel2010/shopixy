using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for Account entity.
/// </summary>
public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("LedgerAccounts");
        
        builder.HasKey(e => e.Id);

        builder.HasAlternateKey(e => new { e.TenantId, e.Id })
            .HasName("AK_LedgerAccounts_TenantId_Id");
        
        // Unique constraint on TenantId + AccountCode
        builder.HasIndex(e => new { e.TenantId, e.AccountCode })
            .IsUnique()
            .HasDatabaseName("IX_LedgerAcct_TenantCode");
        
        // Indexes for common queries
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_LedgerAcct_Tenant");
        
        builder.HasIndex(e => e.ParentAccountId)
            .HasDatabaseName("IX_LedgerAcct_Parent");
        
        builder.HasIndex(e => new { e.TenantId, e.IsClearing })
            .HasFilter("\"IsClearing\" = true")
            .HasDatabaseName("IX_LedgerAcct_Clearing");
        
        builder.HasIndex(e => new { e.TenantId, e.AccountType })
            .HasDatabaseName("IX_LedgerAcct_TenantType");
        
        // Properties
        builder.Property(e => e.AccountCode)
            .IsRequired()
            .HasMaxLength(20);
        
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(e => e.NameAr)
            .HasMaxLength(200);
        
        builder.Property(e => e.Description)
            .HasMaxLength(500);
        
        builder.Property(e => e.AccountType)
            .IsRequired();
        
        builder.Property(e => e.CurrentBalance)
            .HasPrecision(18, 4)
            .IsRequired();
        
        builder.Property(e => e.Level)
            .IsRequired()
            .HasDefaultValue(1);
        
        builder.Property(e => e.IsSystem)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(e => e.IsClearing)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(e => e.PreventNegative)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsSupplierCardFunding)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
        
        builder.Property(e => e.ClearingSourceType)
            .HasMaxLength(50);
        
        builder.Property(e => e.ClearingSourceCode)
            .HasMaxLength(50);
        
        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();
        
        // Optimistic concurrency using PostgreSQL's xmin system column
        // This tells EF Core to use the xmin column for concurrency checks
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
        
        // Self-referencing hierarchy
        builder.HasOne(e => e.ParentAccount)
            .WithMany(e => e.SubAccounts)
            .HasForeignKey(e => e.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
