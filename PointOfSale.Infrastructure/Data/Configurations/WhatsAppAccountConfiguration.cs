using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class WhatsAppAccountConfiguration : IEntityTypeConfiguration<WhatsAppAccount>
{
    public void Configure(EntityTypeBuilder<WhatsAppAccount> builder)
    {
        builder.ToTable("whatsapp_accounts");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        // TenantEntity base configuration (if not handled globally)
        // Usually handled by base class config or conventions, but we'll be explicit for properties
        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20)
            .IsRequired();
            
        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();
            
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .IsRequired();
            
        builder.Property(x => x.Enabled)
            .HasColumnName("enabled")
            .HasDefaultValue(false);
            
        builder.Property(x => x.UseForOrderConfirmation)
            .HasColumnName("use_for_order_confirmation")
            .HasDefaultValue(false);
            
        builder.Property(x => x.UseForOtpVerification)
            .HasColumnName("use_for_otp_verification")
            .HasDefaultValue(false);
            
        builder.Property(x => x.UseForPaymentApproval)
            .HasColumnName("use_for_payment_approval")
            .HasDefaultValue(false);
            
        builder.Property(x => x.UseForAbandonedCartRecovery)
            .HasColumnName("use_for_abandoned_cart_recovery")
            .HasDefaultValue(false);
            
        builder.Property(x => x.LastConnectedAtUtc)
            .HasColumnName("last_connected_at_utc");
            
        builder.Property(x => x.LastDisconnectedAtUtc)
            .HasColumnName("last_disconnected_at_utc");
            
        builder.Property(x => x.StatusReason)
            .HasColumnName("status_reason");
            
        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasDefaultValueSql("NOW()");
            
        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc");

        // Indexes
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_whatsapp_accounts_tenant_id");
            
        builder.HasIndex(x => x.Status)
            .HasFilter("enabled = TRUE")
            .HasDatabaseName("ix_whatsapp_accounts_status");

        // Unique constraint per tenant + phone
        builder.HasIndex(x => new { x.TenantId, x.PhoneNumber })
            .IsUnique()
            .HasDatabaseName("uq_whatsapp_account_phone_tenant");
            
        // Relationships
        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
