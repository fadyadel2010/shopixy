using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class CommerceHubRecoveryOperationLedgerEntryConfiguration
    : IEntityTypeConfiguration<CommerceHubRecoveryOperationLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CommerceHubRecoveryOperationLedgerEntry> builder)
    {
        builder.ToTable("CommerceHubRecoveryOperationLedgerEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.OperationType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ActorIdentity).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.Scope).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.Outcome).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.RequestId).HasMaxLength(200);
        builder.Property(entry => entry.Reason).HasMaxLength(1024);
        builder.Property(entry => entry.DetailsJson).HasColumnType("text");
        builder.HasIndex(entry => new { entry.OperationId, entry.StartedAtUtc }).HasDatabaseName("IX_CHRecovery_Operation_Time");
        builder.HasIndex(entry => new { entry.TenantId, entry.ListingId, entry.StartedAtUtc }).HasDatabaseName("IX_CHRecovery_Tenant_Listing_Time");
        builder.HasIndex(entry => new { entry.ActorUserId, entry.StartedAtUtc }).HasDatabaseName("IX_CHRecovery_Actor_Time");
        builder.HasIndex(entry => entry.CorrelationId).HasDatabaseName("IX_CHRecovery_Correlation");
        builder.HasIndex(entry => entry.RequestId).HasDatabaseName("IX_CHRecovery_Request");
    }
}
