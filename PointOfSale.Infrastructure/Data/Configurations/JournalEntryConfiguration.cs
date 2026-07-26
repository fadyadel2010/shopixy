using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for JournalEntry entity.
/// </summary>
public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("LedgerJournalEntries");
        
        builder.HasKey(e => e.Id);
        
        // CRITICAL: Unique constraint on IdempotencyKey per tenant
        builder.HasIndex(e => new { e.TenantId, e.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("IX_LedgerJournal_Idempotency");
        
        // Unique constraint on JournalNumber per tenant
        builder.HasIndex(e => new { e.TenantId, e.JournalNumber })
            .IsUnique()
            .HasDatabaseName("IX_LedgerJournal_Number");
        
        // Indexes for common queries
        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_LedgerJournal_Tenant");
        
        builder.HasIndex(e => new { e.TenantId, e.JournalDate })
            .HasDatabaseName("IX_LedgerJournal_Date");
        
        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId })
            .HasDatabaseName("IX_LedgerJournal_Source");
        
        builder.HasIndex(e => new { e.TenantId, e.Status })
            .HasDatabaseName("IX_LedgerJournal_Status");
        
        builder.HasIndex(e => e.PostedAtUtc)
            .HasDatabaseName("IX_LedgerJournal_Posted");
        
        // Properties
        builder.Property(e => e.JournalNumber)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(e => e.JournalDate)
            .IsRequired();
        
        builder.Property(e => e.PostedAtUtc)
            .IsRequired();
        
        builder.Property(e => e.SourceType)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(e => e.SourceId)
            .IsRequired();
        
        builder.Property(e => e.SourceReference)
            .HasMaxLength(100);
        
        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(500);
        
        builder.Property(e => e.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255);
        
        builder.Property(e => e.Status)
            .IsRequired();
        
        builder.Property(e => e.ReversalReason)
            .HasMaxLength(500);
        
        // Self-referencing for reversals
        builder.HasOne<JournalEntry>()
            .WithOne()
            .HasForeignKey<JournalEntry>(e => e.ReversedByJournalId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // Navigation to ledger entries
        builder.HasMany(e => e.LedgerEntries)
            .WithOne(e => e.JournalEntry)
            .HasForeignKey(e => e.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
