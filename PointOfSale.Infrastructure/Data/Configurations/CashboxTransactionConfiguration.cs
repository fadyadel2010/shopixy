using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CashboxTransactionConfiguration : IEntityTypeConfiguration<CashboxTransaction>
{
    public void Configure(EntityTypeBuilder<CashboxTransaction> builder)
    {
        builder.ToTable("CashboxTransactions");

        builder.HasKey(ct => ct.Id);
        
        builder.Property(ct => ct.Amount)
            .HasPrecision(18, 2);
        
        builder.Property(ct => ct.BalanceAfter)
            .HasPrecision(18, 2);
        
        builder.Property(ct => ct.Description)
            .IsRequired()
            .HasMaxLength(500);
        
        builder.Property(ct => ct.ReferenceNumber)
            .HasMaxLength(100);
        
        builder.Property(ct => ct.Notes)
            .HasMaxLength(1000);
        
        builder.HasOne(ct => ct.Cashbox)
            .WithMany(c => c.Transactions)
            .HasForeignKey(ct => ct.CashboxId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(ct => ct.User)
            .WithMany()
            .HasForeignKey(ct => ct.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasIndex(ct => ct.CashboxId);
        builder.HasIndex(ct => ct.TransactionDate);
        builder.HasIndex(ct => ct.TransactionType);
        builder.HasIndex(ct => ct.ReferenceId);
        builder.HasIndex(ct => new { ct.CashboxId, ct.TransactionDate });
    }
}
