using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for LedgerEntry entity.
/// </summary>
public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerLines");
        
        builder.HasKey(e => e.Id);
        
        // Indexes for common queries
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_LedgerLines_Tenant");
        
        builder.HasIndex(e => e.JournalEntryId)
            .HasDatabaseName("IX_LedgerLines_Journal");
        
        builder.HasIndex(e => new { e.AccountId, e.CreatedAtUtc })
            .HasDatabaseName("IX_LedgerLines_AcctDate");
        
        builder.HasIndex(e => new { e.TenantId, e.AccountId })
            .HasDatabaseName("IX_LedgerLines_TenantAcct");
        
        // Properties
        builder.Property(e => e.DebitAmount)
            .HasPrecision(18, 4)
            .IsRequired();
        
        builder.Property(e => e.CreditAmount)
            .HasPrecision(18, 4)
            .IsRequired();
        
        builder.Property(e => e.BalanceAfter)
            .HasPrecision(18, 4)
            .IsRequired();
        
        builder.Property(e => e.LineNumber)
            .IsRequired();
        
        builder.Property(e => e.LineDescription)
            .HasMaxLength(200);
        
        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();
        
        // Check constraint: exactly one side must be > 0
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_LedgerLines_SingleSide",
            "(\"DebitAmount\" > 0 AND \"CreditAmount\" = 0) OR (\"DebitAmount\" = 0 AND \"CreditAmount\" > 0)"));
        
        // Check constraint: amounts must be non-negative
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_LedgerLines_PositiveAmounts",
            "\"DebitAmount\" >= 0 AND \"CreditAmount\" >= 0"));
        
        // Relationships
        builder.HasOne(e => e.JournalEntry)
            .WithMany(e => e.LedgerEntries)
            .HasForeignKey(e => e.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
