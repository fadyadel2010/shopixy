using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubFinancialTransactionConfiguration : IEntityTypeConfiguration<CommerceHubFinancialTransaction>
{
    public void Configure(EntityTypeBuilder<CommerceHubFinancialTransaction> builder)
    {
        builder.ToTable("CommerceHubFinancialTransactions");
        builder.HasKey(entry => entry.Id);

        builder.HasIndex(entry => new { entry.EntityId, entry.EntityType })
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_EntityId_EntityType");

        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CHFinTx_EntityType_EntityId_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId, entry.TransactionType, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_EntityType_EntityId_TransactionType_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.EntityId, entry.CreatedAtUtc })
            .HasFilter("\"EntityType\" = 1 AND \"EntityId\" IS NOT NULL")
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_EntityId_CreatedAtUtc_Vendor");

        builder.HasIndex(entry => entry.ReferenceId)
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_ReferenceId");

        builder.HasIndex(entry => new { entry.ReferenceType, entry.ReferenceId })
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_ReferenceType_ReferenceId");

        builder.HasIndex(entry => new { entry.ReferenceType, entry.TransactionType, entry.Direction, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_ReferenceType_TransactionType_Direction_CreatedAtUtc");

        builder.HasIndex(entry => entry.CorrelationId)
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_CorrelationId");

        builder.HasIndex(entry => entry.TransactionType)
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_TransactionType");

        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId, entry.CreatedBy, entry.CreatedAtUtc })
            .HasFilter("\"EntityType\" = 1 AND \"EntityId\" IS NOT NULL AND \"CreatedBy\" IS NOT NULL")
            .HasDatabaseName("IX_CHFinTx_EntityType_EntityId_CreatedBy_CreatedAtUtc");

        builder.HasIndex(entry => new { entry.EntityType, entry.FinancialBucket, entry.CreatedAtUtc })
            .HasDatabaseName("IX_CHFinancialTx_EntityType_Bucket_CreatedAtUtc");

        builder.HasIndex(entry => entry.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_CommerceHubFinancialTransactions_IdempotencyKey");

        builder.HasIndex(entry => new
            {
                entry.ReferenceType,
                entry.ReferenceId,
                entry.TransactionType,
                entry.EntityId,
                entry.Direction,
                entry.FinancialBucket,
                entry.Amount,
                entry.Currency
            })
            .IsUnique()
            .HasFilter("\"EntityId\" IS NOT NULL")
            .HasDatabaseName("UX_CHFinancialTx_Fingerprint_Vendor");

        builder.HasIndex(entry => new
            {
                entry.ReferenceType,
                entry.ReferenceId,
                entry.TransactionType,
                entry.Direction,
                entry.FinancialBucket,
                entry.Amount,
                entry.Currency
            })
            .IsUnique()
            .HasFilter("\"EntityId\" IS NULL")
            .HasDatabaseName("UX_CHFinancialTx_Fingerprint_Platform");

        builder.HasIndex(entry => entry.CreatedAtUtc)
            .HasDatabaseName("IX_CommerceHubFinancialTransactions_CreatedAtUtc");

        builder.Property(entry => entry.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(entry => entry.FinancialBucket)
            .IsRequired();

        builder.Property(entry => entry.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(entry => entry.Description)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasColumnType("text");

        builder.Property(entry => entry.IdempotencyKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(entry => entry.CorrelationId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(entry => entry.CreatedBy);

        builder.Property(entry => entry.CreatedAtUtc)
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubFinancialTransactions_Amount",
                "\"Amount\" > 0");

            table.HasCheckConstraint(
                "CK_CommerceHubFinancialTransactions_Currency_EGP",
                "\"Currency\" = 'EGP'");

            table.HasCheckConstraint(
                "CK_CommerceHubFinancialTransactions_EntityScope",
                "(\"EntityType\" = 0 AND \"EntityId\" IS NULL) OR (\"EntityType\" = 1 AND \"EntityId\" IS NOT NULL)");

            table.HasCheckConstraint(
                "CK_CommerceHubFinancialTransactions_TransactionType",
                "\"TransactionType\" IN (0, 1, 2, 3, 4, 5, 6)");

            table.HasCheckConstraint(
                "CK_CommerceHubFinancialTransactions_Direction",
                "\"Direction\" IN (0, 1)");

            table.HasCheckConstraint(
                "CK_CommerceHubFinancialTransactions_FinancialBucket",
                "\"FinancialBucket\" IN (0, 1, 2)");

            table.HasCheckConstraint(
                "CK_CommerceHubFinancialTransactions_ReferenceType",
                "\"ReferenceType\" IN (0, 1, 2, 3, 4)");
        });
    }
}
