using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public sealed class SslCertificateOperationConfiguration : IEntityTypeConfiguration<SslCertificateOperation>
{
    public void Configure(EntityTypeBuilder<SslCertificateOperation> builder)
    {
        builder.ToTable("SslCertificateOperations", table =>
        {
            table.HasCheckConstraint(
                "CK_SslCertificateOperations_HostName_Length",
                "\"NormalizedHostName\" IS NOT NULL AND LENGTH(TRIM(\"NormalizedHostName\")) >= 1 AND LENGTH(\"NormalizedHostName\") <= 253");
            table.HasCheckConstraint(
                "CK_SslCertificateOperations_IdempotencyKey_NotBlank",
                "\"IdempotencyKey\" IS NOT NULL AND LENGTH(TRIM(\"IdempotencyKey\")) >= 1");
            table.HasCheckConstraint(
                "CK_SslCertificateOperations_Fencing_NonNegative",
                "\"FencingToken\" >= 0");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever().IsRequired();
        builder.Property(o => o.TenantId).IsRequired();
        builder.Property(o => o.DomainId).IsRequired();
        builder.Property(o => o.CertificateId).IsRequired(false);
        builder.Property(o => o.NormalizedHostName).IsRequired().HasMaxLength(253);
        builder.Property(o => o.OperationType).HasConversion<int>().IsRequired();
        builder.Property(o => o.Status).HasConversion<int>().IsRequired();
        builder.Property(o => o.Phase).HasConversion<int>().IsRequired();
        builder.Property(o => o.OperationId).IsRequired();
        builder.Property(o => o.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(o => o.RequestHash).HasMaxLength(128);
        builder.Property(o => o.ProviderRequestReference).HasMaxLength(500);
        builder.Property(o => o.EdgeCertificateReference).HasMaxLength(500);
        builder.Property(o => o.FencingToken).IsRequired().HasDefaultValue(0L);
        builder.Property(o => o.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(o => o.FailureCode).HasMaxLength(100);
        builder.Property(o => o.SafeFailureReason).HasMaxLength(1000);
        builder.Property(o => o.Retryable).IsRequired().HasDefaultValue(false);
        builder.Property(o => o.CorrelationId).HasMaxLength(100);
        builder.Property(o => o.CreatedAtUtc).IsRequired().HasDefaultValueSql("NOW()");
        builder.Property(o => o.UpdatedAtUtc).IsRequired(false);
        builder.Property(o => o.CompletedAtUtc).IsRequired(false);
        builder.Property(o => o.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(o => o.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .IsRequired();

        builder.HasOne(o => o.Domain)
            .WithMany(d => d.SslCertificateOperations)
            .HasForeignKey(o => new { o.TenantId, o.DomainId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .HasConstraintName("FK_SslCertificateOperations_DomainId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Certificate)
            .WithMany(c => c.Operations)
            .HasForeignKey(o => new { o.TenantId, o.DomainId, o.CertificateId })
            .HasPrincipalKey(c => new { c.TenantId, c.DomainId, c.Id })
            .HasConstraintName("FK_SslCertificateOperations_CertificateId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => new { o.TenantId, o.DomainId, o.OperationType, o.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey");

        builder.HasIndex(o => new { o.TenantId, o.DomainId, o.OperationType })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"Status\" IN (0, 1)")
            .HasDatabaseName("UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress");

        builder.HasIndex(o => new { o.TenantId, o.DomainId, o.CertificateId })
            .HasFilter("\"IsDeleted\" = false AND \"CertificateId\" IS NOT NULL")
            .HasDatabaseName("IX_SslCertificateOperations_Tenant_Domain_Certificate");

        builder.HasIndex(o => new { o.LeaseExpiresAtUtc, o.Status })
            .HasFilter("\"LeaseExpiresAtUtc\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_SslCertificateOperations_LeaseExpiresAtUtc_Status");

        builder.HasIndex(o => new { o.RetryAfterUtc, o.Status })
            .HasFilter("\"RetryAfterUtc\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_SslCertificateOperations_RetryAfterUtc_Status");

        builder.HasIndex(o => new { o.TenantId, o.DomainId, o.FencingToken })
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("IX_SslCertificateOperations_Tenant_Domain_FencingToken");
    }
}
