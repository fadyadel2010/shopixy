using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class WhatsAppMessageTemplateConfiguration : IEntityTypeConfiguration<WhatsAppMessageTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessageTemplate> builder)
    {
        builder.ToTable("whatsapp_message_templates");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Type).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Language).IsRequired().HasMaxLength(10);
        builder.Property(x => x.Content).IsRequired();
        
        // =====================================================================
        // PHASE: WhatsApp Final Hardening
        // UNIQUE constraint prevents race conditions creating duplicate templates
        // Previously this was enforced only at service level which was vulnerable
        // =====================================================================
        builder.HasIndex(x => new { x.TenantId, x.Type, x.Language })
            .IsUnique()
            .HasDatabaseName("uq_whatsapp_template_tenant_type_language");
        
        // Audit timestamps
        builder.Property(x => x.CreatedAtUtc)
            .HasDefaultValueSql("NOW()");
        
        builder.Property(x => x.UpdatedAtUtc);
    }
}
