using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed class CertificateProvisioningService : ICertificateProvisioningService
{
    private const string DefaultProviderRequestPrefix = "cert-provision";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProvisioningLocks = new(StringComparer.Ordinal);

    private static readonly HashSet<string> ReservedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "localhost.localdomain",
        "ip.local",
        "example.com",
        "example.org",
        "example.net",
        "test",
        "test.local",
        "staging",
        "staging.local",
        "dev",
        "dev.local",
        "admin",
        "admin.local",
        "shop",
        "shop.local",
        "cdn",
        "cdn.local",
        "api",
        "api.local",
        "app",
        "app.local",
        "www",
        "m",
        "mail",
        "ftp",
        "smtp",
        "pop",
        "imap",
        "ns",
        "dns",
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ICertificateProvider _provider;
    private readonly ICertificateProviderAccountSelector _accountSelector;
    private readonly IOptionsMonitor<CertificateProviderOptions> _providerOptions;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CertificateProvisioningService> _logger;

    public CertificateProvisioningService(
        ApplicationDbContext dbContext,
        ICertificateProvider provider,
        ICertificateProviderAccountSelector accountSelector,
        IOptionsMonitor<CertificateProviderOptions> providerOptions,
        IEventPublisher eventPublisher,
        ILogger<CertificateProvisioningService> logger)
    {
        _dbContext = dbContext;
        _provider = provider;
        _accountSelector = accountSelector;
        _providerOptions = providerOptions;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<CertificateProvisioningResponse> ProvisionAsync(
        Guid domainId,
        CertificateProvisioningRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired, "Tenant ID is required.");
        }

        if (domainId == Guid.Empty)
        {
            throw new DomainNotFoundException("The requested domain was not found.");
        }

        var idempotencyKey = NormalizeIdempotencyKey(request?.IdempotencyKey);
        var lockKey = $"{tenantId:N}:{domainId:N}";
        var commandLock = ProvisioningLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await commandLock.WaitAsync(cancellationToken);

        try
        {
            var claim = await ClaimOperationAsync(
                domainId,
                tenantId,
                actorUserId,
                idempotencyKey,
                correlationId,
                cancellationToken);

            if (claim.Outcome is not null)
            {
                return MapResponse(claim.Domain, claim.Certificate, claim.Operation, claim.Outcome, correlationId);
            }

            return await ExecuteProviderWorkflowAsync(
                claim.Domain,
                claim.Certificate,
                claim.Operation,
                actorUserId,
                correlationId,
                cancellationToken);
        }
        finally
        {
            commandLock.Release();
            if (commandLock.CurrentCount == 1)
            {
                ProvisioningLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(lockKey, commandLock));
            }
        }
    }

    private async Task<ProvisioningClaim> ClaimOperationAsync(
        Guid domainId,
        Guid tenantId,
        Guid? actorUserId,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);

        try
        {
            var domain = await _dbContext.Domains
                .IgnoreQueryFilters()
                .Include(d => d.SslCertificates.Where(c => !c.IsDeleted))
                .Include(d => d.SslCertificateOperations.Where(o => !o.IsDeleted))
                .SingleOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken);

            if (domain is null)
            {
                throw new DomainNotFoundException("The requested domain was not found.");
            }

            var existingOperation = domain.SslCertificateOperations
                .Where(o => o.OperationType == CertificateOperationType.Provisioning)
                .SingleOrDefault(o => string.Equals(o.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

            if (existingOperation is not null)
            {
                var existingCertificate = existingOperation.CertificateId.HasValue
                    ? domain.SslCertificates.SingleOrDefault(c => c.Id == existingOperation.CertificateId.Value)
                    : null;

                if (existingCertificate is null)
                {
                    throw new DomainConflictException("A certificate provisioning operation exists without a certificate correlation.");
                }

                var expectedHash = HashProvisioningRequest(
                    tenantId,
                    domain.Id,
                    domain.NormalizedHostName,
                    existingCertificate.Generation,
                    idempotencyKey);
                if (!string.Equals(existingOperation.RequestHash, expectedHash, StringComparison.Ordinal))
                    throw new DomainConflictException("Idempotency key was already used for a different certificate provisioning request.");

                var retryNow = DateTime.UtcNow;
                var replayOutcome = GetReplayOutcome(existingOperation, retryNow);
                if (replayOutcome is not null)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new ProvisioningClaim(domain, existingCertificate, existingOperation, replayOutcome);
                }

                ValidateAdmission(domain, idempotencyKey);
                ValidateProviderConfiguration(domain);
                domain.CertificateOperationFencingToken += 1;
                existingCertificate.FencingToken = domain.CertificateOperationFencingToken;
                existingOperation.Status = CertificateOperationStatus.Pending;
                existingOperation.Phase = CertificateOperationPhase.Admission;
                existingOperation.FencingToken = domain.CertificateOperationFencingToken;
                existingOperation.LockOwnerReference = Guid.NewGuid();
                existingOperation.LeaseExpiresAtUtc = retryNow.AddMinutes(15);
                existingOperation.RetryAfterUtc = null;
                existingOperation.CompletedAtUtc = null;
                existingOperation.FailureCode = null;
                existingOperation.SafeFailureReason = null;
                existingOperation.Retryable = false;
                existingOperation.UpdatedAtUtc = retryNow;
                if (existingCertificate.Status == CertificateStatus.Failed && existingCertificate.Retryable)
                {
                    existingCertificate.Status = CertificateStatus.Pending;
                    existingCertificate.OperationPhase = CertificateOperationPhase.Admission;
                    existingCertificate.FailureCode = null;
                    existingCertificate.SafeFailureReason = null;
                    existingCertificate.RetryAfterUtc = null;
                    existingCertificate.Retryable = false;
                    existingCertificate.UpdatedAt = retryNow;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                await CommitIfPresentAsync(transaction, cancellationToken);
                return new ProvisioningClaim(domain, existingCertificate, existingOperation, Outcome: null);
            }

            ValidateAdmission(domain, idempotencyKey);
            ValidateProviderConfiguration(domain);

            var now = DateTime.UtcNow;
            await SupersedeExpiredOperationsAsync(
                domain,
                CertificateOperationType.Provisioning,
                now,
                actorUserId,
                correlationId,
                cancellationToken);

            var conflictingOperation = domain.SslCertificateOperations.Any(o =>
                o.OperationType == CertificateOperationType.Provisioning &&
                o.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running);

            if (conflictingOperation)
            {
                throw new DomainConflictException("A certificate provisioning operation is already in progress for this domain.");
            }

            var generation = domain.SslCertificates.Count == 0
                ? 1
                : domain.SslCertificates.Max(c => c.Generation) + 1;

            var operationId = Guid.NewGuid();
            var certificateId = Guid.NewGuid();
            var providerRequestReference = $"{DefaultProviderRequestPrefix}:{operationId:N}";
            domain.CertificateOperationFencingToken += 1;
            var requestHash = HashProvisioningRequest(tenantId, domain.Id, domain.NormalizedHostName, generation, idempotencyKey);

            var certificate = new SslCertificate
            {
                Id = certificateId,
                TenantId = tenantId,
                DomainId = domain.Id,
                NormalizedHostName = domain.NormalizedHostName,
                Provider = _providerOptions.CurrentValue.Provider,
                ProviderImplementation = "CertificateProvisioningService",
                ProviderAccountReference = _providerOptions.CurrentValue.AccountReference,
                ProviderEnvironment = _providerOptions.CurrentValue.Environment.ToString(),
                Status = CertificateStatus.Pending,
                OperationPhase = CertificateOperationPhase.Admission,
                Generation = generation,
                OperationId = operationId,
                IdempotencyKey = idempotencyKey,
                ProviderRequestReference = providerRequestReference,
                FencingToken = domain.CertificateOperationFencingToken,
                SubjectAlternativeNamesJson = JsonSerializer.Serialize(new[] { domain.NormalizedHostName }),
                RenewalEligible = false,
                Retryable = false,
                CorrelationId = correlationId,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorUserId,
                IsDeleted = false,
            };

            var operation = new SslCertificateOperation
            {
                Id = operationId,
                TenantId = tenantId,
                DomainId = domain.Id,
                CertificateId = certificate.Id,
                NormalizedHostName = domain.NormalizedHostName,
                OperationType = CertificateOperationType.Provisioning,
                Status = CertificateOperationStatus.Pending,
                Phase = CertificateOperationPhase.Admission,
                OperationId = operationId,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash,
                ProviderRequestReference = providerRequestReference,
                FencingToken = domain.CertificateOperationFencingToken,
                LockOwnerReference = operationId,
                LeaseExpiresAtUtc = now.AddMinutes(15),
                AttemptCount = 0,
                Retryable = false,
                CorrelationId = correlationId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsDeleted = false,
            };

            _dbContext.SslCertificates.Add(certificate);
            _dbContext.SslCertificateOperations.Add(operation);
            _dbContext.DomainAuditEntries.Add(CreateAudit(
                domain,
                "CertificateProvisioningOperationClaimed",
                actorUserId,
                correlationId,
                new
                {
                    certificate.Id,
                    OperationId = operation.Id,
                    operation.OperationType,
                    certificate.Generation,
                    certificate.Status,
                    operation.Phase,
                    operation.ProviderRequestReference,
                }));

            var startedEvent = new CertificateProvisioningStartedEvent
            {
                DomainId = domain.Id,
                CertificateId = certificate.Id,
                OperationId = operation.Id,
                OperationType = CertificateOperationType.Provisioning,
                Generation = generation,
                TenantId = tenantId,
                UserId = actorUserId,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
            };
            await _eventPublisher.PublishAsync(startedEvent, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfPresentAsync(transaction, cancellationToken);

            return new ProvisioningClaim(domain, certificate, operation, Outcome: null);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await RollbackIfPresentAsync(transaction, cancellationToken);
            return await ReloadClaimAsync(domainId, tenantId, idempotencyKey, cancellationToken);
        }
    }

    private async Task<CertificateProvisioningResponse> ExecuteProviderWorkflowAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var options = _providerOptions.CurrentValue;
        var context = new CertificateTenantOperationContext(
            domain.TenantId,
            domain.Id,
            certificate.Id,
            operation.Id,
            CertificateOperationType.Provisioning,
            operation.IdempotencyKey,
            certificate.Generation,
            operation.FencingToken);

        var accountSelection = _accountSelector.Select(new CertificateProviderAccountSelectionRequest(
            options.Provider,
            options.Environment,
            options.AccountReference,
            options.ConfiguredIssuer,
            domain.TenantId,
            domain.Id));

        var accountResult = await _provider.EnsureAccountAsync(new AcmeAccountRequest(
            context,
            accountSelection.Provider,
            accountSelection.Environment,
            accountSelection.AccountReference,
            options.ContactEmail,
            accountSelection.ConfiguredIssuer,
            correlationId), cancellationToken);

        if (!accountResult.Succeeded || accountResult.Account is null)
        {
            await PersistFailureAsync(domain, certificate, operation, accountResult.Error!, actorUserId, correlationId, cancellationToken);
            return MapResponse(domain, certificate, operation, "ProviderAccountFailed", correlationId);
        }

        CertificateProviderOperationMetadata? providerMetadata = null;
        CertificateProviderError? providerError = null;

        if (!string.IsNullOrWhiteSpace(operation.EdgeCertificateReference))
        {
            var statusResult = await _provider.GetOperationStatusAsync(new CertificateProviderOperationQuery(
                context,
                options.Provider,
                options.Environment,
                operation.EdgeCertificateReference,
                correlationId), cancellationToken);

            providerMetadata = statusResult.Operation;
            providerError = statusResult.Error;
        }
        else
        {
            var orderResult = await _provider.CreateOrderAsync(new CertificateIssuanceRequest(
                context,
                domain.NormalizedHostName,
                Array.AsReadOnly(new[] { domain.NormalizedHostName }),
                accountResult.Account,
                CertificateChallengePolicy.Default,
                operation.ProviderRequestReference,
                correlationId), cancellationToken);

            if (orderResult.Succeeded && orderResult.Order is not null)
            {
                providerMetadata = orderResult.Operation;
                await PersistProviderOrderAsync(
                    domain,
                    certificate,
                    operation,
                    orderResult.Order,
                    providerMetadata,
                    actorUserId,
                    correlationId,
                    cancellationToken);
            }
            else
            {
                providerError = orderResult.Error;
            }
        }

        if (providerError is not null)
        {
            await PersistFailureAsync(domain, certificate, operation, providerError, actorUserId, correlationId, cancellationToken);
            return MapResponse(domain, certificate, operation, "ProviderFailed", correlationId);
        }

        if (providerMetadata?.ProviderStatus == PointOfSale.Core.Certificates.CertificateProviderOperationStatus.Succeeded
            && providerMetadata.CertificateMetadata is not null)
        {
            await PersistIssuedMetadataAsync(domain, certificate, operation, providerMetadata, actorUserId, correlationId, cancellationToken);
            return MapResponse(domain, certificate, operation, "IssuedPendingActivation", correlationId);
        }

        await PersistDeferredAsync(domain, certificate, operation, providerMetadata, actorUserId, correlationId, cancellationToken);
        return MapResponse(domain, certificate, operation, "Deferred", correlationId);
    }

    private async Task PersistProviderOrderAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        CertificateOrder order,
        CertificateProviderOperationMetadata? metadata,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        certificate.TransitionTo(CertificateStatus.Requested);
        certificate.OperationPhase = CertificateOperationPhase.ProviderRequest;
        certificate.ProviderRequestReference = metadata?.ProviderRequestReference ?? operation.ProviderRequestReference;
        certificate.OrderReference = order.ProviderOrderReference;
        certificate.AuthorizationReference = string.Join(',', order.AuthorizationReferences);
        certificate.EdgeCertificateReference = metadata?.ProviderOperationReference;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Running;
        operation.Phase = CertificateOperationPhase.ProviderRequest;
        operation.EdgeCertificateReference = metadata?.ProviderOperationReference;
        operation.UpdatedAtUtc = now;
        operation.AttemptCount += 1;
        operation.LeaseExpiresAtUtc = now.AddMinutes(15);

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateProviderOrderCreated", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            certificate.OrderReference,
            certificate.AuthorizationReference,
            operation.EdgeCertificateReference,
            certificate.Status,
            operation.Phase,
        }));

        await _eventPublisher.PublishAsync(new CertificateAuthorizationStartedEvent
        {
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            Generation = certificate.Generation,
            TenantId = domain.TenantId,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistIssuedMetadataAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        CertificateProviderOperationMetadata metadata,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (certificate.Status == CertificateStatus.Pending)
        {
            certificate.TransitionTo(CertificateStatus.Requested);
        }

        if (certificate.Status == CertificateStatus.Requested)
        {
            certificate.TransitionTo(CertificateStatus.Authorizing);
        }

        if (certificate.Status == CertificateStatus.Authorizing)
        {
            certificate.TransitionTo(CertificateStatus.Issued);
        }

        var certificateMetadata = metadata.CertificateMetadata!;
        certificate.OperationPhase = CertificateOperationPhase.MetadataSync;
        certificate.EdgeCertificateReference = metadata.ProviderOperationReference ?? certificate.EdgeCertificateReference;
        certificate.OrderReference = metadata.OrderReference ?? certificate.OrderReference;
        certificate.AuthorizationReference = string.Join(',', metadata.AuthorizationReferences);
        certificate.Issuer = certificateMetadata.Issuer;
        certificate.Subject = certificateMetadata.Subject;
        certificate.SubjectAlternativeNamesJson = JsonSerializer.Serialize(certificateMetadata.SubjectAlternativeNames);
        certificate.Thumbprint = certificateMetadata.Thumbprint;
        certificate.Fingerprint = certificateMetadata.Fingerprint;
        certificate.NotBeforeUtc = ToDateTime(certificateMetadata.NotBeforeUtc);
        certificate.NotAfterUtc = ToDateTime(certificateMetadata.NotAfterUtc);
        certificate.IssuedAtUtc = now;
        certificate.SerialNumber = certificateMetadata.SerialNumber;
        certificate.SignatureAlgorithm = certificateMetadata.SignatureAlgorithm;
        certificate.RenewalEligible = false;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Succeeded;
        operation.Phase = CertificateOperationPhase.MetadataSync;
        operation.EdgeCertificateReference = metadata.ProviderOperationReference ?? operation.EdgeCertificateReference;
        operation.CompletedAtUtc = now;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = null;
        operation.LockOwnerReference = null;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateIssuedPendingActivation", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            CertificateStatus = certificate.Status,
            OperationStatus = operation.Status,
            OperationPhase = operation.Phase,
            certificate.Issuer,
            certificate.Thumbprint,
            certificate.SerialNumber,
            certificate.NotAfterUtc,
        }));

        await _eventPublisher.PublishAsync(new CertificateIssuedEvent
        {
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            Generation = certificate.Generation,
            Thumbprint = certificate.Thumbprint,
            NotAfterUtc = certificate.NotAfterUtc,
            TenantId = domain.TenantId,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);

        await _eventPublisher.PublishAsync(new CertificatePendingActivationEvent
        {
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            Generation = certificate.Generation,
            TenantId = domain.TenantId,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistDeferredAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        CertificateProviderOperationMetadata? metadata,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (certificate.Status == CertificateStatus.Pending)
        {
            certificate.TransitionTo(CertificateStatus.Requested);
        }

        certificate.OperationPhase = CertificateOperationPhase.Authorization;
        certificate.EdgeCertificateReference = metadata?.ProviderOperationReference ?? certificate.EdgeCertificateReference;
        certificate.OrderReference = metadata?.OrderReference ?? certificate.OrderReference;
        certificate.AuthorizationReference = metadata?.AuthorizationReferences.Count > 0
            ? string.Join(',', metadata.AuthorizationReferences)
            : certificate.AuthorizationReference;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Running;
        operation.Phase = CertificateOperationPhase.Authorization;
        operation.EdgeCertificateReference = metadata?.ProviderOperationReference ?? operation.EdgeCertificateReference;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = now.AddMinutes(15);

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateProvisioningDeferred", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            CertificateStatus = certificate.Status,
            OperationStatus = operation.Status,
            OperationPhase = operation.Phase,
            operation.EdgeCertificateReference,
        }));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistFailureAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        CertificateProviderError error,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        certificate.Status = CertificateStatus.Failed;
        certificate.OperationPhase = CertificateOperationPhase.RetryBackoff;
        certificate.FailureCode = error.Code;
        certificate.SafeFailureReason = error.SafeMessage;
        certificate.ProviderErrorClass = error.Category.ToString();
        certificate.Retryable = error.Retryable;
        var providerRetryAfterUtc = error.RetryAfterUtc?.UtcDateTime;
        certificate.RetryAfterUtc = error.Retryable ? providerRetryAfterUtc ?? now.AddMinutes(5) : null;
        certificate.LastFailureAtUtc = now;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Failed;
        operation.Phase = CertificateOperationPhase.RetryBackoff;
        operation.FailureCode = error.Code;
        operation.SafeFailureReason = error.SafeMessage;
        operation.Retryable = error.Retryable;
        operation.RetryAfterUtc = certificate.RetryAfterUtc;
        operation.CompletedAtUtc = error.Retryable ? null : now;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = null;
        operation.LockOwnerReference = null;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateProvisioningFailed", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            CertificateStatus = certificate.Status,
            OperationStatus = operation.Status,
            error.Code,
            ErrorCategory = error.Category,
            error.Retryable,
        }));

        await _eventPublisher.PublishAsync(new CertificateProvisioningFailedEvent
        {
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            Generation = certificate.Generation,
            FailureCode = error.Code,
            Retryable = error.Retryable,
            TenantId = domain.TenantId,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateAdmission(Domain domain, string idempotencyKey)
    {
        if (domain.IsDeleted || domain.LifecycleState == DomainLifecycleState.Deleted)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "Deleted domains cannot be provisioned for SSL.");
        }

        if (domain.VerifiedAtUtc is null || domain.LifecycleState != DomainLifecycleState.Verified)
        {
            throw new DomainOperationException(DomainOperationErrorCode.NotVerifiedOrSslActive, "Only verified domains are eligible for SSL provisioning.");
        }

        if (string.IsNullOrWhiteSpace(domain.NormalizedHostName) || domain.NormalizedHostName.Length > 253)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidHostFormat, "Domain hostname is not valid for SSL provisioning.");
        }

        if (ReservedHosts.Contains(domain.NormalizedHostName) || domain.NormalizedHostName.StartsWith("*."))
        {
            throw new DomainOperationException(DomainOperationErrorCode.ReservedHost, "This hostname is reserved and cannot be provisioned for tenant SSL.");
        }

        if (domain.DomainType is not (DomainType.Alias or DomainType.Redirect))
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidDomainType, "Only Alias and Redirect domains are eligible for tenant SSL provisioning.");
        }

        if (domain.SslCertificates.Any(c => c.Status == CertificateStatus.Active))
        {
            throw new DomainConflictException("This domain already has an active certificate.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "A valid idempotency key is required.");
        }
    }

    private void ValidateProviderConfiguration(Domain domain)
    {
        var options = _providerOptions.CurrentValue;
        if (!options.Enabled ||
            string.IsNullOrWhiteSpace(options.AccountReference) ||
            options.DirectoryUrl is null ||
            !options.DirectoryUrl.IsAbsoluteUri)
        {
            throw new DomainOperationException(
                DomainOperationErrorCode.InvalidStateTransition,
                "Certificate provider configuration is not valid for provisioning.");
        }

        _ = _accountSelector.Select(new CertificateProviderAccountSelectionRequest(
            options.Provider,
            options.Environment,
            options.AccountReference,
            options.ConfiguredIssuer,
            domain.TenantId,
            domain.Id));

        CertificateChallengePolicy.Default.Validate();
    }

    private static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "A valid idempotency key is required.");
        }

        return idempotencyKey.Trim();
    }

    private async Task<ProvisioningClaim> ReloadClaimAsync(
        Guid domainId,
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var domain = await _dbContext.Domains
            .IgnoreQueryFilters()
            .Include(d => d.SslCertificates.Where(c => !c.IsDeleted))
            .Include(d => d.SslCertificateOperations.Where(o => !o.IsDeleted))
            .SingleOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken);

        if (domain is null)
        {
            throw new DomainNotFoundException("The requested domain was not found.");
        }

        var operation = domain.SslCertificateOperations.Single(o =>
            o.OperationType == CertificateOperationType.Provisioning &&
            o.IdempotencyKey == idempotencyKey);

        var certificate = domain.SslCertificates.Single(c => c.Id == operation.CertificateId);
        return new ProvisioningClaim(
            domain,
            certificate,
            operation,
            GetReplayOutcome(operation, DateTime.UtcNow) ?? "InProgress");
    }

    private static string? GetReplayOutcome(SslCertificateOperation operation, DateTime now)
    {
        if (operation.Status == CertificateOperationStatus.Succeeded)
            return "Replayed";

        if (operation.Status == CertificateOperationStatus.Superseded)
            return "Superseded";

        if (operation.Status == CertificateOperationStatus.Failed &&
            (!operation.Retryable || operation.RetryAfterUtc > now))
            return "ReplayedFailure";

        if (operation.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running &&
            (!operation.LeaseExpiresAtUtc.HasValue || operation.LeaseExpiresAtUtc.Value > now))
            return "InProgress";

        return null;
    }

    private async Task SupersedeExpiredOperationsAsync(
        Domain domain,
        CertificateOperationType operationType,
        DateTime now,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var staleOperations = domain.SslCertificateOperations
            .Where(o => o.OperationType == operationType &&
                        o.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running &&
                        o.LeaseExpiresAtUtc.HasValue &&
                        o.LeaseExpiresAtUtc.Value <= now)
            .ToArray();

        foreach (var stale in staleOperations)
        {
            stale.Status = CertificateOperationStatus.Superseded;
            stale.Phase = CertificateOperationPhase.RetryBackoff;
            stale.FailureCode = "STALE_OPERATION_SUPERSEDED";
            stale.SafeFailureReason = "The expired operation lease was superseded by a newer command.";
            stale.Retryable = false;
            stale.CompletedAtUtc = now;
            stale.UpdatedAtUtc = now;
            stale.LeaseExpiresAtUtc = null;
            stale.LockOwnerReference = null;

            var staleCertificate = stale.CertificateId.HasValue
                ? domain.SslCertificates.SingleOrDefault(c => c.Id == stale.CertificateId.Value)
                : null;
            if (staleCertificate is not null &&
                staleCertificate.Status is CertificateStatus.Pending or CertificateStatus.Requested or CertificateStatus.Authorizing)
            {
                staleCertificate.Status = CertificateStatus.Failed;
                staleCertificate.OperationPhase = CertificateOperationPhase.RetryBackoff;
                staleCertificate.FailureCode = stale.FailureCode;
                staleCertificate.SafeFailureReason = stale.SafeFailureReason;
                staleCertificate.Retryable = false;
                staleCertificate.UpdatedAt = now;
            }

            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateOperationSuperseded", actorUserId, correlationId, new
            {
                OperationId = stale.Id,
                stale.CertificateId,
                stale.OperationType,
                stale.FencingToken,
            }));
        }

        if (staleOperations.Length > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("Transactions are not supported", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    private static Task CommitIfPresentAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private static Task RollbackIfPresentAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.RollbackAsync(cancellationToken);

    private static DomainAuditEntry CreateAudit(
        Domain domain,
        string action,
        Guid? actorUserId,
        string correlationId,
        object safeMetadata)
    {
        return new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = domain.TenantId,
            Action = action,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            BeforeSnapshotJson = null,
            AfterSnapshotJson = JsonSerializer.Serialize(new
            {
                domain.Id,
                domain.TenantId,
                domain.HostName,
                domain.NormalizedHostName,
                domain.DomainType,
                domain.LifecycleState,
                domain.VerifiedAtUtc,
                SslCertificateId = domain.SslCertificateId,
                CertificateProvisioning = safeMetadata,
            }),
            TraceId = correlationId,
            TimestampUtc = DateTime.UtcNow,
        };
    }

    private static CertificateProvisioningResponse MapResponse(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        string outcome,
        string correlationId)
    {
        return new CertificateProvisioningResponse
        {
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            OperationType = operation.OperationType,
            OperationStatus = operation.Status,
            OperationPhase = operation.Phase,
            CertificateStatus = certificate.Status,
            Generation = certificate.Generation,
            NormalizedHostName = domain.NormalizedHostName,
            Provider = certificate.Provider,
            ProviderEnvironment = certificate.ProviderEnvironment,
            ProviderRequestReference = certificate.ProviderRequestReference,
            OrderReference = certificate.OrderReference,
            Retryable = operation.Retryable,
            FailureCode = operation.FailureCode,
            SafeFailureReason = operation.SafeFailureReason,
            NextAction = certificate.Status == CertificateStatus.Issued
                ? "PendingEdgeActivation"
                : operation.Status == CertificateOperationStatus.Failed && operation.Retryable
                    ? "RetryLater"
                    : operation.Status == CertificateOperationStatus.Failed
                        ? "ResolveFailure"
                        : "AwaitProviderProgress",
            Outcome = outcome,
            CorrelationId = correlationId,
        };
    }

    private static DateTime? ToDateTime(DateTimeOffset? value) =>
        value?.UtcDateTime;

    private static string HashProvisioningRequest(Guid tenantId, Guid domainId, string host, int generation, string idempotencyKey)
    {
        var value = $"{tenantId:N}|{domainId:N}|{host}|{generation}|{idempotencyKey}|http-01,tls-alpn-01";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("UQ_", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record ProvisioningClaim(
        Domain Domain,
        SslCertificate Certificate,
        SslCertificateOperation Operation,
        string? Outcome);
}
