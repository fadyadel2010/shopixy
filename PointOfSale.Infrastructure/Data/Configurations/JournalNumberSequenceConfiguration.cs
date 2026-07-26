using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for JournalNumberSequence entity.
/// </summary>
public class JournalNumberSequenceConfiguration : IEntityTypeConfiguration<JournalNumberSequence>
{
    public void Configure(EntityTypeBuilder<JournalNumberSequence> builder)
    {
        builder.ToTable("LedgerJournalNumberSequences");
        
        builder.HasKey(e => e.TenantId);
        
        builder.Property(e => e.CurrentYear)
            .IsRequired();
        
        builder.Property(e => e.NextNumber)
            .IsRequired()
            .HasDefaultValue(1);
    }
}
