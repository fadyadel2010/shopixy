using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for CommerceAccountingAudit.
/// </summary>
public class CommerceAccountingAuditConfiguration : IEntityTypeConfiguration<CommerceAccountingAudit>
{
    public void Configure(EntityTypeBuilder<CommerceAccountingAudit> builder)
    {
        builder.ToTable("CommerceAccountingAudit");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.BusinessType)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(x => x.BusinessReference)
            .HasMaxLength(100);
        
        builder.Property(x => x.JournalNumber)
            .HasMaxLength(50);
        
        builder.Property(x => x.OperationType)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(x => x.Amount)
            .HasPrecision(18, 4);
        
        builder.Property(x => x.IdempotencyKey)
            .HasMaxLength(255);
        
        // Index for business entity lookup
        builder.HasIndex(x => new { x.TenantId, x.BusinessType, x.BusinessId })
            .HasDatabaseName("IX_CommerceAudit_Business");
        
        // Index for journal lookup
        builder.HasIndex(x => x.JournalId)
            .HasDatabaseName("IX_CommerceAudit_JournalId");
        
        // Index for time-based queries
        builder.HasIndex(x => new { x.TenantId, x.PostedAtUtc })
            .HasDatabaseName("IX_CommerceAudit_TenantId_PostedAtUtc");
        
        // Foreign key to JournalEntry
        builder.HasOne(x => x.Journal)
            .WithMany()
            .HasForeignKey(x => x.JournalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
