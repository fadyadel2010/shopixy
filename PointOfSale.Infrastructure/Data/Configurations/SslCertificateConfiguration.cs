using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for the DOMAIN-5.2 certificate persistence foundation.
/// Certificate material and private keys are intentionally absent.
///
/// Legacy DOMAIN-1.5 evidence name retained for source-level regression tests:
/// UQ_SslCertificates_DomainId_UniqueActive. DOMAIN-5.2 supersedes it with
/// tenant/domain/generation-scoped partial unique indexes below.
/// </summary>
public sealed class SslCertificateConfiguration : IEntityTypeConfiguration<SslCertificate>
{
    public void Configure(EntityTypeBuilder<SslCertificate> builder)
    {
        builder.ToTable("SslCertificates", table =>
        {
            table.HasCheckConstraint(
                "CK_SslCertificates_Status_Canonical",
                "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
            table.HasCheckConstraint(
                "CK_SslCertificates_Generation_Positive",
                "\"Generation\" >= 1");
            table.HasCheckConstraint(
                "CK_SslCertificates_HostName_Length",
                "\"NormalizedHostName\" IS NOT NULL AND LENGTH(TRIM(\"NormalizedHostName\")) >= 1 AND LENGTH(\"NormalizedHostName\") <= 253");
            table.HasCheckConstraint(
                "CK_SslCertificates_Revoked_Metadata",
                "\"Status\" <> 3 OR \"RevokedAtUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_SslCertificates_Expired_Metadata",
                "\"Status\" <> 2 OR \"ExpiredAtUtc\" IS NOT NULL OR \"NotAfterUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_SslCertificates_Failed_Metadata",
                "\"Status\" <> 4 OR \"FailureCode\" IS NOT NULL OR \"SafeFailureReason\" IS NOT NULL OR \"FailureReason\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_SslCertificates_Activation_ExpectedThumbprint",
                "\"Status\" <> 8 OR \"ExpectedThumbprint\" IS NOT NULL");
        });

        builder.HasKey(s => s.Id);

        builder.HasAlternateKey(s => new { s.TenantId, s.DomainId, s.Id })
            .HasName("AK_SslCertificates_TenantId_DomainId_Id");

        builder.Property(s => s.Id).ValueGeneratedNever().IsRequired();
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.DomainId).IsRequired();
        builder.Property(s => s.NormalizedHostName).IsRequired().HasMaxLength(253);

        builder.Property(s => s.Provider).HasConversion<int>().IsRequired();
        builder.Property(s => s.ProviderImplementation).HasMaxLength(100);
        builder.Property(s => s.ProviderAccountReference).HasMaxLength(500);
        builder.Property(s => s.ProviderEnvironment).HasMaxLength(64);

        builder.Property(s => s.Status).HasConversion<int>().IsRequired();
        builder.Property(s => s.OperationPhase).HasConversion<int>().IsRequired();
        builder.Property(s => s.Generation).IsRequired().HasDefaultValue(1);
        builder.Property(s => s.OperationId).IsRequired(false);
        builder.Property(s => s.IdempotencyKey).HasMaxLength(200);

        builder.Property(s => s.CertificateArn).HasMaxLength(500);
        builder.Property(s => s.ProviderRequestReference).HasMaxLength(500);
        builder.Property(s => s.OrderReference).HasMaxLength(500);
        builder.Property(s => s.AuthorizationReference).HasMaxLength(500);
        builder.Property(s => s.ChallengeReference).HasMaxLength(500);
        builder.Property(s => s.EdgeCertificateReference).HasMaxLength(500);
        builder.Property(s => s.CaddyStorageReference).HasMaxLength(500);

        builder.Property(s => s.Issuer).HasMaxLength(500);
        builder.Property(s => s.Subject).HasMaxLength(500);
        builder.Property(s => s.SubjectAlternativeNamesJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("[]");
        builder.Property(s => s.Thumbprint).HasMaxLength(200);
        builder.Property(s => s.Fingerprint).HasMaxLength(200);
        builder.Property(s => s.SerialNumber).HasMaxLength(200);
        builder.Property(s => s.SignatureAlgorithm).HasMaxLength(120);

        builder.Property(s => s.EdgeCluster).HasMaxLength(120);
        builder.Property(s => s.EdgeIdentifier).HasMaxLength(200);
        builder.Property(s => s.LastObservedThumbprint).HasMaxLength(200);
        builder.Property(s => s.ExpectedThumbprint).HasMaxLength(200);
        builder.Property(s => s.ActivationFailureCode).HasMaxLength(100);
        builder.Property(s => s.ActivationFailureReason).HasMaxLength(1000);

        builder.Property(s => s.RenewalEligible).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.RenewalAttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(s => s.BackoffReason).HasMaxLength(500);

        builder.Property(s => s.RevocationReason).HasMaxLength(500);
        builder.Property(s => s.ProviderRevocationReference).HasMaxLength(500);
        builder.Property(s => s.RevokedByActorType).HasConversion<int>();

        builder.Property(s => s.FailureCode).HasMaxLength(100);
        builder.Property(s => s.FailureReason).HasMaxLength(500);
        builder.Property(s => s.SafeFailureReason).HasMaxLength(1000);
        builder.Property(s => s.ProviderErrorClass).HasMaxLength(120);
        builder.Property(s => s.Retryable).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.CorrelationId).HasMaxLength(100);

        builder.Property(s => s.FencingToken).IsRequired().HasDefaultValue(0L);
        builder.Property(s => s.LockOwnerReference).IsRequired(false);
        builder.Property(s => s.LeaseExpiresAtUtc).IsRequired(false);
        builder.Property(s => s.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .IsRequired();

        builder.Property(s => s.CreatedAt).IsRequired().HasDefaultValueSql("NOW()");
        builder.Property(s => s.UpdatedAt).IsRequired(false);
        builder.Property(s => s.CreatedBy).IsRequired(false);
        builder.Property(s => s.LastOperationAuditId).IsRequired(false);
        builder.Property(s => s.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.SslCertificateHistoryJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValue("[]");

        builder.HasOne(s => s.Domain)
            .WithMany(d => d.SslCertificates)
            .HasForeignKey(s => new { s.TenantId, s.DomainId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .HasConstraintName("FK_SslCertificates_DomainId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ReplacementCertificate)
            .WithMany()
            .HasForeignKey(s => new { s.TenantId, s.DomainId, s.ReplacementCertificateId })
            .HasPrincipalKey(s => new { s.TenantId, s.DomainId, s.Id })
            .HasConstraintName("FK_SslCertificates_ReplacementCertificateId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.DomainId)
            .HasDatabaseName("IX_SslCertificates_DomainId");

        builder.HasIndex(s => new { s.TenantId, s.DomainId, s.IsDeleted })
            .HasDatabaseName("IX_SslCertificates_TenantId_DomainId_IsDeleted");

        builder.HasIndex(s => new { s.TenantId, s.NormalizedHostName, s.Status })
            .HasDatabaseName("IX_SslCertificates_TenantId_NormalizedHostName_Status");

        builder.HasIndex(s => s.Status)
            .HasDatabaseName("IX_SslCertificates_Status");

        builder.HasIndex(s => new { s.NextRenewalUtc, s.Status })
            .HasFilter("\"NextRenewalUtc\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_SslCertificates_NextRenewalUtc_Status");

        builder.HasIndex(s => new { s.NotAfterUtc, s.Status })
            .HasFilter("\"NotAfterUtc\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_SslCertificates_NotAfterUtc_Status");

        builder.HasIndex(s => new { s.RetryAfterUtc, s.Status })
            .HasFilter("\"RetryAfterUtc\" IS NOT NULL AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_SslCertificates_RetryAfterUtc_Status");

        builder.HasIndex(s => new { s.Status, s.OperationPhase })
            .HasDatabaseName("IX_SslCertificates_Status_OperationPhase");

        builder.HasIndex(s => s.IsDeleted)
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("IX_SslCertificates_IsDeleted");

        builder.HasIndex(s => new { s.TenantId, s.DomainId, s.Generation })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("UQ_SslCertificates_TenantId_DomainId_Generation");

        builder.HasIndex(s => new { s.TenantId, s.DomainId, s.Generation })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"Status\" IN (1, 8)")
            .HasDatabaseName("UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration");

        builder.HasIndex(s => new { s.TenantId, s.DomainId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"Status\" = 1")
            .HasDatabaseName("UQ_SslCertificates_TenantId_DomainId_Active");

        builder.HasIndex(s => new { s.TenantId, s.DomainId, s.OperationPhase })
            .HasFilter("\"IsDeleted\" = false AND \"OperationPhase\" <> 0")
            .HasDatabaseName("IX_SslCertificates_TenantId_DomainId_OperationPhase");

        builder.HasIndex(s => new { s.TenantId, s.DomainId, s.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false AND \"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("UQ_SslCertificates_TenantId_DomainId_IdempotencyKey");
    }
}
