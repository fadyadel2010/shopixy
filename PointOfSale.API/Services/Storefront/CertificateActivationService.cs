using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed class CertificateActivationService : ICertificateActivationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ActivationLocks = new(StringComparer.Ordinal);

    private readonly ApplicationDbContext _dbContext;
    private readonly ICaddyEdgeClient _edgeClient;
    private readonly IEdgeCertificateProbe _certificateProbe;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CertificateActivationService> _logger;

    public CertificateActivationService(
        ApplicationDbContext dbContext,
        ICaddyEdgeClient edgeClient,
        IEdgeCertificateProbe certificateProbe,
        IEventPublisher eventPublisher,
        ILogger<CertificateActivationService> logger)
    {
        _dbContext = dbContext;
        _edgeClient = edgeClient;
        _certificateProbe = certificateProbe;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<CertificateActivationResponse> ActivateAsync(
        Guid domainId,
        CertificateActivationRequest? request,
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
        var lockKey = $"{tenantId:N}:{domainId:N}:activation";
        var commandLock = ActivationLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await commandLock.WaitAsync(cancellationToken);

        try
        {
            var claim = await ClaimActivationAsync(domainId, request?.CertificateId, tenantId, actorUserId, idempotencyKey, correlationId, cancellationToken);
            if (claim.Outcome is not null)
            {
                return MapResponse(claim.Domain, claim.Certificate, claim.Operation, claim.Outcome, correlationId);
            }

            await PersistActivationStartedAsync(claim.Domain, claim.Certificate, claim.Operation, actorUserId, correlationId, cancellationToken);

            var routeResult = await _edgeClient.EnsureDomainRouteAsync(new CaddyDomainRouteRequest(
                tenantId,
                claim.Domain.Id,
                claim.Certificate.Id,
                claim.Operation.Id,
                claim.Domain.NormalizedHostName,
                correlationId,
                claim.Operation.FencingToken), cancellationToken);

            if (!routeResult.Succeeded)
            {
                await PersistActivationFailureAsync(
                    claim.Domain,
                    claim.Certificate,
                    claim.Operation,
                    routeResult.FailureCode ?? "CADDY_ROUTE_RECONCILIATION_FAILED",
                    routeResult.SafeFailureReason ?? "Caddy route reconciliation failed.",
                    routeResult.Retryable,
                    actorUserId,
                    correlationId,
                    cancellationToken);

                return MapResponse(claim.Domain, claim.Certificate, claim.Operation, "EdgeAdmissionFailed", correlationId);
            }

            await PersistEdgeAdmissionAsync(claim.Domain, claim.Certificate, claim.Operation, routeResult, actorUserId, correlationId, cancellationToken);

            var probeResult = await _certificateProbe.ProbeAsync(new EdgeCertificateProbeRequest(
                tenantId,
                claim.Domain.Id,
                claim.Certificate.Id,
                claim.Operation.Id,
                claim.Domain.NormalizedHostName,
                claim.Certificate.Thumbprint,
                claim.Certificate.Fingerprint,
                claim.Certificate.SerialNumber,
                claim.Certificate.Issuer,
                claim.Certificate.NotAfterUtc,
                correlationId), cancellationToken);

            if (!probeResult.Succeeded || probeResult.Certificate is null)
            {
                await PersistActivationFailureAsync(
                    claim.Domain,
                    claim.Certificate,
                    claim.Operation,
                    probeResult.FailureCode ?? "SNI_PROBE_FAILED",
                    probeResult.SafeFailureReason ?? "External HTTPS/SNI certificate proof failed.",
                    probeResult.Retryable,
                    actorUserId,
                    correlationId,
                    cancellationToken);

                return MapResponse(claim.Domain, claim.Certificate, claim.Operation, "SniProbeFailed", correlationId);
            }

            await PersistActivationSuccessAsync(
                claim.Domain,
                claim.Certificate,
                claim.Operation,
                probeResult.Certificate,
                actorUserId,
                correlationId,
                cancellationToken);

            return MapResponse(claim.Domain, claim.Certificate, claim.Operation, "Activated", correlationId);
        }
        finally
        {
            commandLock.Release();
            if (commandLock.CurrentCount == 1)
            {
                ActivationLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(lockKey, commandLock));
            }
        }
    }

    private async Task<ActivationClaim> ClaimActivationAsync(
        Guid domainId,
        Guid? certificateId,
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

            var certificate = SelectCertificate(domain, certificateId);
            var existingOperation = domain.SslCertificateOperations.SingleOrDefault(o =>
                o.OperationType == CertificateOperationType.Activation &&
                string.Equals(o.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

            if (existingOperation is not null)
            {
                if (existingOperation.CertificateId != certificate.Id)
                {
                    throw new DomainConflictException("Idempotency key was already used for a different certificate activation request.");
                }

                var retryNow = DateTime.UtcNow;
                var replayOutcome = GetReplayOutcome(domain, certificate, existingOperation, retryNow);
                if (replayOutcome is not null)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new ActivationClaim(domain, certificate, existingOperation, replayOutcome);
                }

                ValidateActivationAdmission(domain, certificate, idempotencyKey);
                domain.CertificateOperationFencingToken += 1;
                certificate.FencingToken = domain.CertificateOperationFencingToken;
                existingOperation.Status = CertificateOperationStatus.Pending;
                existingOperation.Phase = CertificateOperationPhase.Admission;
                existingOperation.FencingToken = domain.CertificateOperationFencingToken;
                existingOperation.LockOwnerReference = Guid.NewGuid();
                existingOperation.LeaseExpiresAtUtc = retryNow.AddMinutes(10);
                existingOperation.RetryAfterUtc = null;
                existingOperation.CompletedAtUtc = null;
                existingOperation.FailureCode = null;
                existingOperation.SafeFailureReason = null;
                existingOperation.Retryable = false;
                existingOperation.UpdatedAtUtc = retryNow;

                await _dbContext.SaveChangesAsync(cancellationToken);

                await CommitIfPresentAsync(transaction, cancellationToken);
                return new ActivationClaim(domain, certificate, existingOperation, Outcome: null);
            }

            ValidateActivationAdmission(domain, certificate, idempotencyKey);

            var now = DateTime.UtcNow;
            await SupersedeExpiredOperationsAsync(
                domain,
                CertificateOperationType.Activation,
                now,
                actorUserId,
                correlationId,
                cancellationToken);

            var conflictingOperation = domain.SslCertificateOperations.Any(o =>
                o.OperationType == CertificateOperationType.Activation &&
                o.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running);

            if (conflictingOperation)
            {
                throw new DomainConflictException("A certificate activation operation is already in progress for this domain.");
            }

            domain.CertificateOperationFencingToken += 1;
            certificate.FencingToken = domain.CertificateOperationFencingToken;
            var operationId = Guid.NewGuid();
            var operation = new SslCertificateOperation
            {
                Id = operationId,
                TenantId = tenantId,
                DomainId = domain.Id,
                CertificateId = certificate.Id,
                NormalizedHostName = domain.NormalizedHostName,
                OperationType = CertificateOperationType.Activation,
                Status = CertificateOperationStatus.Pending,
                Phase = CertificateOperationPhase.Admission,
                OperationId = operationId,
                IdempotencyKey = idempotencyKey,
                RequestHash = $"{tenantId:N}:{domain.Id:N}:{certificate.Id:N}:{certificate.Generation}:{idempotencyKey}",
                EdgeCertificateReference = certificate.EdgeCertificateReference,
                FencingToken = domain.CertificateOperationFencingToken,
                LockOwnerReference = operationId,
                LeaseExpiresAtUtc = now.AddMinutes(10),
                AttemptCount = 0,
                Retryable = false,
                CorrelationId = correlationId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsDeleted = false,
            };

            _dbContext.SslCertificateOperations.Add(operation);
            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateEdgeActivationRequested", actorUserId, correlationId, new
            {
                certificate.Id,
                OperationId = operation.Id,
                certificate.Generation,
                CertificateStatus = certificate.Status,
                operation.Phase,
            }));

            await _eventPublisher.PublishAsync(new CertificateEdgeActivationStartedEvent
            {
                TenantId = tenantId,
                DomainId = domain.Id,
                CertificateId = certificate.Id,
                OperationId = operation.Id,
                Generation = certificate.Generation,
                UserId = actorUserId,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfPresentAsync(transaction, cancellationToken);
            return new ActivationClaim(domain, certificate, operation, Outcome: null);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await RollbackIfPresentAsync(transaction, cancellationToken);
            return await ReloadActivationClaimAsync(domainId, certificateId, tenantId, idempotencyKey, cancellationToken);
        }
    }

    private async Task PersistActivationStartedAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (certificate.Status == CertificateStatus.Issued)
        {
            certificate.TransitionTo(CertificateStatus.Activating);
        }

        certificate.OperationPhase = CertificateOperationPhase.ActivationProbe;
        certificate.ActivationStartedAtUtc ??= now;
        certificate.ExpectedThumbprint = certificate.Thumbprint;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Running;
        operation.Phase = CertificateOperationPhase.ActivationProbe;
        operation.AttemptCount += 1;
        operation.LeaseExpiresAtUtc = now.AddMinutes(10);
        operation.UpdatedAtUtc = now;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateEdgeAdmissionStarted", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            certificate.Status,
            operation.Phase,
        }));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistEdgeAdmissionAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        CaddyDomainRouteResult routeResult,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        certificate.EdgeCluster = routeResult.State.EdgeIdentifier;
        certificate.EdgeIdentifier = routeResult.EdgeIdentifier;
        certificate.DeployedGeneration = certificate.Generation;
        certificate.UpdatedAt = now;
        operation.EdgeCertificateReference = routeResult.EdgeIdentifier ?? operation.EdgeCertificateReference;
        operation.UpdatedAtUtc = now;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateEdgeRouteAccepted", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            routeResult.Mutated,
            routeResult.State.MatchesDesiredState,
            routeResult.State.Upstream,
        }));

        await _eventPublisher.PublishAsync(new CertificateEdgeAdmissionSucceededEvent
        {
            TenantId = domain.TenantId,
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            Generation = certificate.Generation,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistActivationSuccessAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        EdgeCertificateIdentity identity,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var wasPrimary = domain.LifecycleState == DomainLifecycleState.Primary;
        var previousActive = domain.SslCertificateId.HasValue
            ? domain.SslCertificates.SingleOrDefault(c =>
                c.Id == domain.SslCertificateId.Value &&
                c.Id != certificate.Id &&
                c.Status == CertificateStatus.Active)
            : null;

        if (previousActive is not null)
        {
            previousActive.TransitionTo(CertificateStatus.Renewing);
            previousActive.OperationPhase = CertificateOperationPhase.MetadataSync;
            previousActive.ReplacementCertificateId = certificate.Id;
            previousActive.RenewalEligible = false;
            previousActive.RetryAfterUtc = null;
            previousActive.BackoffReason = null;
            previousActive.UpdatedAt = now;
            previousActive.TransitionTo(CertificateStatus.Retired);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (certificate.Status == CertificateStatus.Issued)
        {
            certificate.TransitionTo(CertificateStatus.Activating);
        }

        certificate.TransitionTo(CertificateStatus.Active);
        certificate.OperationPhase = CertificateOperationPhase.ActivationProbe;
        certificate.LastEdgeProbeAtUtc = now;
        certificate.LastObservedThumbprint = identity.Thumbprint;
        certificate.ObservedGeneration = certificate.Generation;
        certificate.Issuer = identity.Issuer;
        certificate.Subject = identity.Subject;
        certificate.SubjectAlternativeNamesJson = JsonSerializer.Serialize(identity.SubjectAlternativeNames);
        certificate.Thumbprint = identity.Thumbprint;
        certificate.Fingerprint = identity.Fingerprint;
        certificate.SerialNumber = identity.SerialNumber;
        certificate.SignatureAlgorithm = identity.SignatureAlgorithm;
        certificate.NotBeforeUtc = identity.NotBeforeUtc;
        certificate.NotAfterUtc = identity.NotAfterUtc;
        certificate.ActivatedAtUtc = now;
        certificate.RenewalEligible = true;
        certificate.FailureCode = null;
        certificate.SafeFailureReason = null;
        certificate.ActivationFailureCode = null;
        certificate.ActivationFailureReason = null;
        certificate.ActivationFailedAtUtc = null;
        certificate.Retryable = false;
        certificate.UpdatedAt = now;

        domain.SslCertificateId = certificate.Id;
        if (wasPrimary)
        {
            domain.LifecycleState = DomainLifecycleState.Primary;
            domain.SslActivatedAtUtc = now;
            domain.UpdatedAtUtc = now;
        }
        else
        {
            domain.MarkSslActive();
        }

        operation.Status = CertificateOperationStatus.Succeeded;
        operation.Phase = CertificateOperationPhase.ActivationProbe;
        operation.Retryable = false;
        operation.FailureCode = null;
        operation.SafeFailureReason = null;
        operation.CompletedAtUtc = now;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = null;
        operation.LockOwnerReference = null;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateActivatedAtEdge", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            certificate.Status,
            DomainLifecycleState = domain.LifecycleState,
            PreviousActiveCertificateId = previousActive?.Id,
            identity.Issuer,
            identity.Thumbprint,
            identity.SerialNumber,
            identity.NotAfterUtc,
        }));

        if (previousActive is not null)
        {
            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "old_certificate_retired", actorUserId, correlationId, new
            {
                OldCertificateId = previousActive.Id,
                ReplacementCertificateId = certificate.Id,
                OldGeneration = previousActive.Generation,
                ReplacementGeneration = certificate.Generation,
                PreviousStatus = CertificateStatus.Active,
                RetiredStatus = previousActive.Status,
            }));
        }

        await PublishActivationSuccessEventsAsync(
            domain,
            certificate,
            operation,
            actorUserId,
            correlationId,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await CommitIfPresentAsync(transaction, cancellationToken);
    }

    private async Task PersistActivationFailureAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        string failureCode,
        string safeFailureReason,
        bool retryable,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!retryable)
        {
            certificate.Status = CertificateStatus.Failed;
        }

        certificate.OperationPhase = CertificateOperationPhase.ActivationProbe;
        certificate.ActivationFailureCode = failureCode;
        certificate.ActivationFailureReason = safeFailureReason;
        certificate.ActivationFailedAtUtc = now;
        certificate.LastFailureAtUtc = now;
        certificate.FailureCode = failureCode;
        certificate.SafeFailureReason = safeFailureReason;
        certificate.Retryable = retryable;
        certificate.RetryAfterUtc = retryable ? now.AddMinutes(5) : null;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Failed;
        operation.Phase = retryable ? CertificateOperationPhase.RetryBackoff : CertificateOperationPhase.ActivationProbe;
        operation.FailureCode = failureCode;
        operation.SafeFailureReason = safeFailureReason;
        operation.Retryable = retryable;
        operation.RetryAfterUtc = retryable ? now.AddMinutes(5) : null;
        operation.CompletedAtUtc = retryable ? null : now;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = null;
        operation.LockOwnerReference = null;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "CertificateEdgeActivationFailed", actorUserId, correlationId, new
        {
            certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            failureCode,
            retryable,
        }));

        await _eventPublisher.PublishAsync(new CertificateEdgeActivationFailedEvent
        {
            TenantId = domain.TenantId,
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            Generation = certificate.Generation,
            FailureCode = failureCode,
            Retryable = retryable,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishActivationSuccessEventsAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(new CertificateActivatedEvent
        {
            TenantId = domain.TenantId,
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            Generation = certificate.Generation,
            Thumbprint = certificate.Thumbprint,
            NotAfterUtc = certificate.NotAfterUtc,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);

        await _eventPublisher.PublishAsync(new DomainSslActivatedEvent
        {
            TenantId = domain.TenantId,
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            SslActivatedAtUtc = domain.SslActivatedAtUtc ?? DateTime.UtcNow,
            UserId = actorUserId,
            CorrelationId = correlationId,
            IdempotencyKey = operation.IdempotencyKey,
        }, cancellationToken);
    }

    private static SslCertificate SelectCertificate(Domain domain, Guid? certificateId)
    {
        if (certificateId.HasValue)
        {
            return domain.SslCertificates.SingleOrDefault(c => c.Id == certificateId.Value)
                ?? throw new DomainNotFoundException("The requested certificate was not found.");
        }

        var active = domain.SslCertificateId.HasValue
            ? domain.SslCertificates.SingleOrDefault(c => c.Id == domain.SslCertificateId.Value && c.Status == CertificateStatus.Active)
            : null;

        return active ?? domain.SslCertificates
            .Where(c => c.Status is CertificateStatus.Issued or CertificateStatus.Activating)
            .OrderByDescending(c => c.Generation)
            .FirstOrDefault()
            ?? throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "No issued certificate is available for activation.");
    }

    private static void ValidateActivationAdmission(Domain domain, SslCertificate certificate, string idempotencyKey)
    {
        if (domain.IsDeleted || domain.LifecycleState == DomainLifecycleState.Deleted)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "Deleted domains cannot be activated for SSL.");
        }

        if (domain.VerifiedAtUtc is null ||
            domain.LifecycleState is not (DomainLifecycleState.Verified or DomainLifecycleState.SslActive or DomainLifecycleState.Primary))
        {
            throw new DomainOperationException(DomainOperationErrorCode.NotVerifiedOrSslActive, "Only verified domains are eligible for SSL activation.");
        }

        if (certificate.TenantId != domain.TenantId || certificate.DomainId != domain.Id)
        {
            throw new DomainConflictException("Certificate does not belong to the requested domain.");
        }

        if (certificate.Status is not (CertificateStatus.Issued or CertificateStatus.Activating or CertificateStatus.Active))
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "Certificate must be issued before edge activation.");
        }

        EdgeCertificateHostName.Validate(domain.NormalizedHostName);

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "A valid idempotency key is required.");
        }
    }

    private async Task<ActivationClaim> ReloadActivationClaimAsync(
        Guid domainId,
        Guid? certificateId,
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var domain = await _dbContext.Domains
            .IgnoreQueryFilters()
            .Include(d => d.SslCertificates.Where(c => !c.IsDeleted))
            .Include(d => d.SslCertificateOperations.Where(o => !o.IsDeleted))
            .SingleOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken)
            ?? throw new DomainNotFoundException("The requested domain was not found.");

        var certificate = SelectCertificate(domain, certificateId);
        var operation = domain.SslCertificateOperations.Single(o =>
            o.OperationType == CertificateOperationType.Activation &&
            o.IdempotencyKey == idempotencyKey);

        return new ActivationClaim(
            domain,
            certificate,
            operation,
            GetReplayOutcome(domain, certificate, operation, DateTime.UtcNow) ?? "InProgress");
    }

    private static string? GetReplayOutcome(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        DateTime now)
    {
        if (operation.Status == CertificateOperationStatus.Succeeded &&
            certificate.Status == CertificateStatus.Active &&
            domain.SslCertificateId == certificate.Id)
            return "ReplayedActive";

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

    private static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "A valid idempotency key is required.");
        }

        return idempotencyKey.Trim();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("UQ_", StringComparison.OrdinalIgnoreCase) == true;

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
                domain.SslCertificateId,
                CertificateActivation = safeMetadata,
            }),
            TraceId = correlationId,
            TimestampUtc = DateTime.UtcNow,
        };
    }

    private static CertificateActivationResponse MapResponse(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        string outcome,
        string correlationId)
    {
        return new CertificateActivationResponse
        {
            DomainId = domain.Id,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            OperationType = operation.OperationType,
            OperationStatus = operation.Status,
            OperationPhase = operation.Phase,
            CertificateStatus = certificate.Status,
            DomainLifecycleState = domain.LifecycleState,
            Generation = certificate.Generation,
            NormalizedHostName = domain.NormalizedHostName,
            EdgeIdentifier = certificate.EdgeIdentifier,
            LastObservedThumbprint = certificate.LastObservedThumbprint,
            Retryable = operation.Retryable,
            FailureCode = operation.FailureCode,
            SafeFailureReason = operation.SafeFailureReason,
            NextAction = certificate.Status == CertificateStatus.Active && domain.SslCertificateId == certificate.Id
                ? "NoActionRequired"
                : operation.Status == CertificateOperationStatus.Failed && operation.Retryable
                    ? "RetryActivationLater"
                    : operation.Status == CertificateOperationStatus.Failed
                        ? "ResolveActivationFailure"
                        : "AwaitEdgeActivation",
            Outcome = outcome,
            CorrelationId = correlationId,
        };
    }

    private sealed record ActivationClaim(
        Domain Domain,
        SslCertificate Certificate,
        SslCertificateOperation Operation,
        string? Outcome);
}
