using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class ProviderDeliveryOutboxMessageConfiguration : IEntityTypeConfiguration<ProviderDeliveryOutboxMessage>
{
    public void Configure(EntityTypeBuilder<ProviderDeliveryOutboxMessage> builder)
    {
        builder.ToTable("ProviderDeliveryOutbox");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(message => message.RequestType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(message => message.DeliveryKey)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(message => message.PayloadHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(message => message.PayloadJson)
            .IsRequired();

        builder.Property(message => message.EndpointUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(message => message.HttpMethod)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(message => message.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(message => message.CorrelationId)
            .HasMaxLength(100);

        builder.Property(message => message.FailureClass)
            .HasMaxLength(100);

        builder.Property(message => message.LastError)
            .HasMaxLength(2000);

        builder.Property(message => message.LastResponseBody)
            .HasMaxLength(4000);

        builder.HasIndex(message => new { message.TenantId, message.Provider, message.DeliveryKey })
            .IsUnique()
            .HasDatabaseName("IX_ProviderDelivery_Tenant_Provider_DeliveryKey");

        builder.HasIndex(message => new
            {
                message.DeliveredAtUtc,
                message.IsFailed,
                message.IsDeadLettered,
                message.NextRetryAtUtc,
                message.ClaimedUntilUtc
            })
            .HasDatabaseName("IX_ProviderDelivery_Claimable")
            .HasFilter("\"DeliveredAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeadLettered\" = false");

        builder.HasIndex(message => new { message.TenantId, message.Provider, message.IsDeadLettered, message.FailureClass })
            .HasDatabaseName("IX_ProviderDelivery_DeadLetter_Replay");

        builder.HasIndex(message => message.PayloadHash)
            .HasDatabaseName("IX_ProviderDelivery_PayloadHash");
    }
}
