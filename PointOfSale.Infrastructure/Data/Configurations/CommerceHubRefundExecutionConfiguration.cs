using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubRefundExecutionConfiguration : IEntityTypeConfiguration<CommerceHubRefundExecution>
{
    public void Configure(EntityTypeBuilder<CommerceHubRefundExecution> builder)
    {
        builder.ToTable("CommerceHubRefundExecutions", table =>
            table.HasCheckConstraint(
                "CK_CommerceHubRefundExecutions_Currency_EGP",
                "\"Currency\" = 'EGP'"));

        builder.HasKey(execution => execution.Id);

        builder.Property(execution => execution.AccountSnapshot)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(execution => execution.TransactionReference)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(execution => execution.Amount)
            .HasPrecision(18, 2);

        builder.Property(execution => execution.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(execution => execution.Notes)
            .HasMaxLength(1000);

        builder.Property(execution => execution.FailureReason)
            .HasMaxLength(2000);

        builder.HasIndex(execution => execution.RefundId)
            .IsUnique()
            .HasDatabaseName("IX_CommerceHubRefundExecutions_RefundId");

        builder.HasIndex(execution => execution.ExecutedAt)
            .HasDatabaseName("IX_CommerceHubRefundExecutions_ExecutedAt");

        builder.HasOne(execution => execution.Refund)
            .WithMany()
            .HasForeignKey(execution => execution.RefundId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
