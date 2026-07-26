using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CommerceHubDailyFinancialAggregateConfiguration : IEntityTypeConfiguration<CommerceHubDailyFinancialAggregate>
{
    public void Configure(EntityTypeBuilder<CommerceHubDailyFinancialAggregate> builder)
    {
        builder.ToTable("CommerceHubDailyFinancialAggregates");
        builder.HasKey(aggregate => new { aggregate.DateUtc, aggregate.CurrencyCode });

        builder.Property(aggregate => aggregate.DateUtc)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(aggregate => aggregate.CurrencyCode)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(aggregate => aggregate.BaseCurrencyCode)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(aggregate => aggregate.ExchangeRateToBase)
            .HasPrecision(18, 8);

        builder.Property(aggregate => aggregate.GrossSales)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(aggregate => aggregate.GrossCommission)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(aggregate => aggregate.PromoCost)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(aggregate => aggregate.PromoReversalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(aggregate => aggregate.RefundAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(aggregate => aggregate.RefundImpactOnRevenue)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(aggregate => aggregate.NetRevenue)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(aggregate => aggregate.GrossSalesBase)
            .HasPrecision(18, 2);

        builder.Property(aggregate => aggregate.GrossCommissionBase)
            .HasPrecision(18, 2);

        builder.Property(aggregate => aggregate.PromoCostBase)
            .HasPrecision(18, 2);

        builder.Property(aggregate => aggregate.PromoReversalAmountBase)
            .HasPrecision(18, 2);

        builder.Property(aggregate => aggregate.RefundAmountBase)
            .HasPrecision(18, 2);

        builder.Property(aggregate => aggregate.RefundImpactOnRevenueBase)
            .HasPrecision(18, 2);

        builder.Property(aggregate => aggregate.NetRevenueBase)
            .HasPrecision(18, 2);

        builder.Property(aggregate => aggregate.OrdersCount)
            .IsRequired();

        builder.Property(aggregate => aggregate.RefundCount)
            .IsRequired();

        builder.Property(aggregate => aggregate.RefundedOrdersCount)
            .IsRequired();

        builder.Property(aggregate => aggregate.IsValid)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(aggregate => aggregate.LastValidatedAtUtc);

        builder.Property(aggregate => aggregate.ValidationError)
            .HasColumnType("text");

        builder.Property(aggregate => aggregate.AggregatedThroughUtc);

        builder.Property(aggregate => aggregate.CalculatedAtUtc)
            .IsRequired();

        builder.HasIndex(aggregate => aggregate.CalculatedAtUtc)
            .HasDatabaseName("IX_CommerceHubDailyFinancialAggregates_CalculatedAtUtc");

        builder.HasIndex(aggregate => new { aggregate.CurrencyCode, aggregate.DateUtc })
            .HasDatabaseName("IX_CommerceHubDailyFinancialAggregates_CurrencyCode_DateUtc");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_GrossSales",
                "\"GrossSales\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_GrossCommission",
                "\"GrossCommission\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_PromoCost",
                "\"PromoCost\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_PromoReversalAmount",
                "\"PromoReversalAmount\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_RefundAmount",
                "\"RefundAmount\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_RefundImpactOnRevenue",
                "\"RefundImpactOnRevenue\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_OrdersCount",
                "\"OrdersCount\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_RefundCount",
                "\"RefundCount\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_RefundedOrdersCount",
                "\"RefundedOrdersCount\" >= 0");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_IsValid",
                "\"IsValid\" IN (TRUE, FALSE)");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_CurrencyCode_EGP",
                "\"CurrencyCode\" = 'EGP'");

            table.HasCheckConstraint(
                "CK_CommerceHubDailyFinancialAggregates_BaseCurrencyCode_EGP",
                "\"BaseCurrencyCode\" = 'EGP'");
        });
    }
}
