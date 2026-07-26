using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CommerceHubFinancialReconciliationReportConfiguration
    : IEntityTypeConfiguration<CommerceHubFinancialReconciliationReport>
{
    public void Configure(EntityTypeBuilder<CommerceHubFinancialReconciliationReport> builder)
    {
        builder.ToTable("CommerceHubFinancialReconciliationReports");
        builder.HasKey(report => report.Id);

        builder.HasIndex(report => report.TimestampUtc)
            .HasDatabaseName("IX_CommerceHubFinancialReconciliationReports_TimestampUtc");

        builder.HasIndex(report => new { report.Status, report.TimestampUtc })
            .HasDatabaseName("IX_CommerceHubFinancialReconciliationReports_Status_TimestampUtc");

        builder.Property(report => report.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(report => report.MismatchCount)
            .IsRequired();

        builder.Property(report => report.DetailsJson)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(report => report.TimestampUtc)
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubFinancialReconciliationReports_MismatchCount",
                "\"MismatchCount\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubFinancialReconciliationReports_Status",
                "\"Status\" IN ('Healthy', 'Warning', 'Critical')");
        });
    }
}
