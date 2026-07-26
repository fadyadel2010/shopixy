using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CommerceHubMerchantLifecycleLedgerEntryConfiguration
    : IEntityTypeConfiguration<CommerceHubMerchantLifecycleLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CommerceHubMerchantLifecycleLedgerEntry> builder)
    {
        builder.ToTable("CommerceHubMerchantLifecycleLedgerEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.EventType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ActorIdentity).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.RequestId).HasMaxLength(200);
        builder.Property(entry => entry.Reason).HasMaxLength(1024);
        builder.Property(entry => entry.DetailsJson).HasColumnType("text");
        builder.HasIndex(entry => new { entry.TenantId, entry.ListingId, entry.OccurredAtUtc }).HasDatabaseName("IX_CHMerchantLifecycle_Tenant_Listing_Time");
        builder.HasIndex(entry => new { entry.ActorUserId, entry.OccurredAtUtc }).HasDatabaseName("IX_CHMerchantLifecycle_Actor_Time");
        builder.HasIndex(entry => entry.CorrelationId).HasDatabaseName("IX_CHMerchantLifecycle_Correlation");
        builder.HasIndex(entry => entry.RequestId).HasDatabaseName("IX_CHMerchantLifecycle_Request");
    }
}
