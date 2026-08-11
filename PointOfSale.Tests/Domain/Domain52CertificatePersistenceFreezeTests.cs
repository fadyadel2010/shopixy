using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Tests.Architecture;

namespace PointOfSale.Tests.CustomDomains;

public sealed class Domain52CertificatePersistenceFreezeTests
{
    private const string MigrationPath =
        "PointOfSale.Infrastructure/Migrations/20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze.cs";

    [Fact]
    public void CertificateStatus_RepresentsEveryDomain51CanonicalState()
    {
        var statuses = Enum.GetNames<CertificateStatus>();

        Assert.Contains(nameof(CertificateStatus.Pending), statuses);
        Assert.Contains(nameof(CertificateStatus.Requested), statuses);
        Assert.Contains(nameof(CertificateStatus.Authorizing), statuses);
        Assert.Contains(nameof(CertificateStatus.Issued), statuses);
        Assert.Contains(nameof(CertificateStatus.Activating), statuses);
        Assert.Contains(nameof(CertificateStatus.Active), statuses);
        Assert.Contains(nameof(CertificateStatus.Renewing), statuses);
        Assert.Contains(nameof(CertificateStatus.Revoked), statuses);
        Assert.Contains(nameof(CertificateStatus.Expired), statuses);
        Assert.Contains(nameof(CertificateStatus.Failed), statuses);
    }

    [Fact]
    public void CertificateTerminalStates_CannotReturnToActive()
    {
        var revoked = new SslCertificate { Status = CertificateStatus.Revoked };
        var expired = new SslCertificate { Status = CertificateStatus.Expired };

        Assert.False(revoked.CanTransitionTo(CertificateStatus.Active));
        Assert.False(expired.CanTransitionTo(CertificateStatus.Active));
        Assert.Throws<InvalidOperationException>(() => revoked.TransitionTo(CertificateStatus.Active));
        Assert.Throws<InvalidOperationException>(() => expired.TransitionTo(CertificateStatus.Active));
    }

    [Fact]
    public void FailedCertificate_RequiresRetryablePolicyBeforeReentry()
    {
        var terminalFailure = new SslCertificate
        {
            Status = CertificateStatus.Failed,
            Retryable = false,
        };
        var retryableFailure = new SslCertificate
        {
            Status = CertificateStatus.Failed,
            Retryable = true,
        };

        Assert.False(terminalFailure.CanTransitionTo(CertificateStatus.Active));
        Assert.True(retryableFailure.CanTransitionTo(CertificateStatus.Pending));
    }

    [Fact]
    public void SslCertificateEntity_OwnsTenantDomainHostGenerationAndSafeMetadata()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/SslCertificate.cs");

        Assert.Contains("public Guid TenantId", entity);
        Assert.Contains("public Guid DomainId", entity);
        Assert.Contains("public string NormalizedHostName", entity);
        Assert.Contains("public int Generation", entity);
        Assert.Contains("public Guid? OperationId", entity);
        Assert.Contains("public string? IdempotencyKey", entity);
        Assert.Contains("public long FencingToken", entity);
        Assert.Contains("public Guid? LockOwnerReference", entity);
        Assert.Contains("public DateTime? LeaseExpiresAtUtc", entity);
        Assert.Contains("public uint RowVersion", entity);
        Assert.Contains("public string SubjectAlternativeNamesJson", entity);
        Assert.Contains("public string? EdgeCertificateReference", entity);
        Assert.Contains("public string? CaddyStorageReference", entity);
        Assert.Contains("public Guid? ReplacementCertificateId", entity);
    }

    [Fact]
    public void CertificateTenantDomainBinding_IsDatabaseEnforcedWithCompositeDomainKey()
    {
        var domainConfig = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Configurations/DomainConfiguration.cs");
        var certConfig = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs");
        var operationConfig = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Configurations/SslCertificateOperationConfiguration.cs");
        var migration = CertificationSource.ReadRepoFile(MigrationPath);

        Assert.Contains("AK_Domains_TenantId_Id", domainConfig);
        Assert.Contains("new { s.TenantId, s.DomainId }", domainConfig);
        Assert.Contains("HasPrincipalKey(d => new { d.TenantId, d.Id })", domainConfig);
        Assert.Contains("new { o.TenantId, o.DomainId }", domainConfig);
        Assert.Contains("TenantId == CurrentTenantId", CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/ApplicationDbContext.cs"));
        Assert.Contains("FK_SslCertificates_DomainId", certConfig);
        Assert.Contains("FK_SslCertificateOperations_DomainId", operationConfig);
        Assert.Contains("principalColumns: new[] { \"TenantId\", \"Id\" }", migration);
    }

    [Fact]
    public void CertificatePersistence_EnforcesGenerationIdempotencyFencingAndPartialIndexes()
    {
        var config = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Configurations/SslCertificateConfiguration.cs");
        var operationConfig = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/Configurations/SslCertificateOperationConfiguration.cs");
        var migration = CertificationSource.ReadRepoFile(MigrationPath);

        Assert.Contains("UQ_SslCertificates_TenantId_DomainId_Generation", config);
        Assert.Contains("UQ_SslCertificates_TenantId_DomainId_Active", config);
        Assert.Contains("UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration", config);
        Assert.Contains("UQ_SslCertificates_TenantId_DomainId_IdempotencyKey", config);
        Assert.Contains("IX_SslCertificates_NextRenewalUtc_Status", config);
        Assert.Contains("IX_SslCertificates_NotAfterUtc_Status", config);
        Assert.Contains("IX_SslCertificates_RetryAfterUtc_Status", config);
        Assert.Contains("IX_SslCertificates_Status_OperationPhase", config);

        Assert.Contains("UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey", operationConfig);
        Assert.Contains("UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress", operationConfig);
        Assert.Contains("IX_SslCertificateOperations_Tenant_Domain_FencingToken", operationConfig);
        Assert.Contains("LeaseExpiresAtUtc", operationConfig);

        Assert.Contains("ROW_NUMBER() OVER", migration);
        Assert.Contains("UPDATE \"SslCertificates\" AS c", migration);
        Assert.DoesNotContain("DomainVerifications", migration);
    }

    [Fact]
    public void CertificateOperations_AreScopedToTenantDomainAndOperation()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/SslCertificateOperation.cs");

        Assert.Contains("public Guid TenantId", entity);
        Assert.Contains("public Guid DomainId", entity);
        Assert.Contains("public Guid? CertificateId", entity);
        Assert.Contains("public CertificateOperationType OperationType", entity);
        Assert.Contains("public Guid OperationId", entity);
        Assert.Contains("public string IdempotencyKey", entity);
        Assert.Contains("public long FencingToken", entity);
        Assert.Contains("public Guid? LockOwnerReference", entity);
        Assert.Contains("public DateTime? LeaseExpiresAtUtc", entity);
        Assert.Contains("public uint RowVersion", entity);
    }

    [Fact]
    public void CertificatePersistence_DoesNotExposePrivateMaterialProperties()
    {
        var checkedFiles = new[]
        {
            "PointOfSale.Core/Entities/SslCertificate.cs",
            "PointOfSale.Core/Entities/SslCertificateOperation.cs",
            "PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs",
            "PointOfSale.Core/Events/Domain/DomainEvents.cs",
            "PointOfSale.Core/Entities/DomainAuditEntry.cs",
        };

        var forbiddenMembers = new[]
        {
            "PrivateKey",
            "CertificatePem",
            "PrivateKeyPem",
            "Pfx",
            "Pkcs12",
            "AcmeAccountSecret",
            "AcmeSecret",
            "ChallengeCredential",
            "DnsProviderCredential",
            "CertificateBlob",
            "SerializedCertificate",
        };

        foreach (var path in checkedFiles)
        {
            var source = CertificationSource.ReadRepoFile(path);
            foreach (var forbidden in forbiddenMembers)
            {
                Assert.DoesNotContain($"public string? {forbidden}", source);
                Assert.DoesNotContain($"public string {forbidden}", source);
                Assert.DoesNotContain($"public byte[]? {forbidden}", source);
                Assert.DoesNotContain($"public byte[] {forbidden}", source);
            }
        }
    }

    [Fact]
    public void Migration_IsCertificateScopedAndContainsExpectedSchemaGuards()
    {
        var migration = CertificationSource.ReadRepoFile(MigrationPath);

        Assert.Contains("name: \"TenantId\"", migration);
        Assert.Contains("name: \"NormalizedHostName\"", migration);
        Assert.Contains("name: \"SslCertificateOperations\"", migration);
        Assert.Contains("CK_SslCertificates_Status_Canonical", migration);
        Assert.Contains("CK_SslCertificates_HostName_Length", migration);
        Assert.Contains("CK_SslCertificates_Revoked_Metadata", migration);
        Assert.Contains("CK_SslCertificates_Failed_Metadata", migration);
        Assert.Contains("UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey", migration);
        Assert.Contains("UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress", migration);
        Assert.DoesNotContain("name: \"AccountingOutboxMessages\"", migration);
        Assert.DoesNotContain("name: \"AffiliateAccounts\"", migration);
    }
}
