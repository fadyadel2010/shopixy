using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed class CertificateRevocationService : ICertificateRevocationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICertificateProvider _provider;
    private readonly ICertificateProviderAccountSelector _accountSelector;
    private readonly IOptionsMonitor<CertificateProviderOptions> _providerOptions;
    private readonly IOptions<CertificateRenewalSchedulerOptions> _renewalOptions;
    private readonly ICertificateRenewalService _renewalService;
    private readonly ICaddyEdgeClient _edgeClient;
    private readonly IEventPublisher _eventPublisher;
    private readonly CertificateRenewalTelemetry _telemetry;
    private readonly ILogger<CertificateRevocationService> _logger;

    public CertificateRevocationService(
        ApplicationDbContext dbContext,
        ICertificateProvider provider,
        ICertificateProviderAccountSelector accountSelector,
        IOptionsMonitor<CertificateProviderOptions> providerOptions,
        IOptions<CertificateRenewalSchedulerOptions> renewalOptions,
        ICertificateRenewalService renewalService,
        ICaddyEdgeClient edgeClient,
        IEventPublisher eventPublisher,
        CertificateRenewalTelemetry telemetry,
        ILogger<CertificateRevocationService> logger)
    {
        _dbContext = dbContext;
        _provider = provider;
        _accountSelector = accountSelector;
        _providerOptions = providerOptions;
        _renewalOptions = renewalOptions;
        _renewalService = renewalService;
        _edgeClient = edgeClient;
        _eventPublisher = eventPublisher;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<CertificateRevocationServiceResult> RevokeAsync(
        Guid domainId,
        CertificateRevocationRequest request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired, "Tenant ID is required.");
        }

        if (domainId == Guid.Empty || request.CertificateId == Guid.Empty)
        {
            throw new DomainNotFoundException("The requested certificate was not found.");
        }

        var reasonCode = CertificateRevocationReasonCodes.Normalize(request.ReasonCode);
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var claim = await ClaimRevocationAsync(domainId, request.CertificateId, tenantId, reasonCode, idempotencyKey, actorUserId, correlationId, cancellationToken);

        if (claim.Outcome is not null)
        {
            var replay = MapResult(
                claim,
                reasonCode,
                claim.Outcome,
                succeeded: claim.Outcome is "Replayed" or "AlreadyRevoked");
            if (replay.Succeeded)
                _telemetry.RecordRevocationSuccess(replay);
            else
                _telemetry.RecordRevocationFailure(replay);
            return replay;
        }

        if (claim.Operation.Status == CertificateOperationStatus.Succeeded)
        {
            var result = MapResult(claim, reasonCode, "Replayed", succeeded: true);
            _telemetry.RecordRevocationSuccess(result);
            return result;
        }

        if (claim.Operation.Status == CertificateOperationStatus.Failed && !claim.Operation.Retryable)
        {
            var result = MapResult(claim, reasonCode, "TerminalFailure", succeeded: false);
            _telemetry.RecordRevocationFailure(result);
            return result;
        }

        _telemetry.RecordRevocationStarted(MapResult(claim, reasonCode, "Started", succeeded: false));

        if (claim.Certificate.Status == CertificateStatus.Revoked)
        {
            await PersistAlreadyRevokedAsync(claim, reasonCode, actorUserId, correlationId, cancellationToken);
            var result = MapResult(claim, reasonCode, "AlreadyRevoked", succeeded: true);
            _telemetry.RecordRevocationSuccess(result);
            return result;
        }

        var now = DateTime.UtcNow;
        var currentAuthority = claim.Domain.SslCertificateId == claim.Certificate.Id;
        var currentlyValid = claim.Certificate.Status == CertificateStatus.Active &&
            (!claim.Certificate.NotBeforeUtc.HasValue || claim.Certificate.NotBeforeUtc.Value <= now) &&
            claim.Certificate.NotAfterUtc.HasValue &&
            claim.Certificate.NotAfterUtc.Value > now;

        if (currentAuthority && currentlyValid && !request.ImmediateRevocation)
        {
            await MarkRevocationRequiresReplacementAsync(claim, reasonCode, actorUserId, correlationId, cancellationToken);
            var renewal = await _renewalService.RenewAsync(
                new CertificateRenewalSchedulerCandidate(
                    claim.Certificate.TenantId,
                    claim.Domain.Id,
                    claim.Certificate.Id,
                    claim.Certificate.Provider,
                    claim.Certificate.Generation,
                    claim.Domain.NormalizedHostName,
                    claim.Certificate.NextRenewalUtc,
                    claim.Certificate.NotAfterUtc,
                    CertificateRevocationReasonCodes.IsCompromise(reasonCode) ? "compromise_replacement" : "revocation_replacement"),
                correlationId,
                cancellationToken);

            if (!renewal.Succeeded)
            {
                await PersistReplacementFailureAsync(claim.Operation.Id, renewal.FailureCode ?? "REPLACEMENT_FAILED", renewal.Retryable, renewal.RetryAfterUtc, correlationId, cancellationToken);
                await ReloadClaimStateAsync(claim, cancellationToken);
                var result = MapResult(claim, reasonCode, "ReplacementFailedOldCertificatePreserved", succeeded: false);
                _telemetry.RecordRevocationFailure(result);
                return result;
            }

            await ReloadClaimStateAsync(claim, cancellationToken);
            if (claim.Domain.SslCertificateId == claim.Certificate.Id &&
                claim.Certificate.Status == CertificateStatus.Active)
            {
                await PersistReplacementFailureAsync(claim.Operation.Id, "REPLACEMENT_NOT_AUTHORITATIVE", retryable: true, DateTime.UtcNow.Add(ComputeBackoff(claim.Operation.AttemptCount)), correlationId, cancellationToken);
                await ReloadClaimStateAsync(claim, cancellationToken);
                var result = MapResult(claim, reasonCode, "ReplacementDidNotBecomeAuthoritative", succeeded: false);
                _telemetry.RecordRevocationFailure(result);
                return result;
            }
        }

        var degradedCurrentAuthority = claim.Domain.SslCertificateId == claim.Certificate.Id &&
            (!currentlyValid || request.ImmediateRevocation);
        var requiresEdgeRemovalRecovery = string.Equals(
            claim.Operation.FailureCode,
            "CADDY_ROUTE_REMOVAL_FAILED",
            StringComparison.OrdinalIgnoreCase);
        if (degradedCurrentAuthority)
        {
            DegradeDomainSsl(claim.Domain, claim.Certificate, now);
        }

        if (degradedCurrentAuthority || requiresEdgeRemovalRecovery)
        {
            var removal = await _edgeClient.RemoveDomainRouteAsync(new CaddyDomainRouteQuery(
                claim.Domain.TenantId,
                claim.Domain.Id,
                claim.Domain.NormalizedHostName,
                correlationId,
                claim.Operation.FencingToken,
                claim.Certificate.Id,
                claim.Operation.Id), cancellationToken);
            if (!removal.Succeeded)
            {
                await PersistProviderFailureAsync(
                    claim,
                    CertificateProviderError.Create(
                        CertificateProviderErrorCategory.ProviderUnavailable,
                        removal.FailureCode ?? "CADDY_ROUTE_REMOVAL_FAILED",
                        removal.Retryable,
                        removal.SafeFailureReason ?? "The compromised certificate route could not be removed from the edge.",
                        correlationId: correlationId),
                    actorUserId,
                    correlationId,
                    cancellationToken);
                var result = MapResult(claim, reasonCode, "EdgeRemovalFailed", succeeded: false);
                _telemetry.RecordRevocationFailure(result);
                return result;
            }
        }

        var providerResult = await RevokeAtProviderAsync(claim, reasonCode, correlationId, cancellationToken);
        if (!providerResult.Succeeded)
        {
            await PersistProviderFailureAsync(claim, providerResult.Error!, actorUserId, correlationId, cancellationToken);
            var result = MapResult(claim, reasonCode, "ProviderRevocationFailed", succeeded: false);
            _telemetry.RecordRevocationFailure(result);
            return result;
        }

        await PersistRevocationSuccessAsync(claim, reasonCode, providerResult.ProviderRevocationReference, actorUserId, correlationId, cancellationToken);
        var revoked = MapResult(claim, reasonCode, "Revoked", succeeded: true);
        _telemetry.RecordRevocationSuccess(revoked);
        return revoked;
    }

    private async Task<RevocationClaim> ClaimRevocationAsync(
        Guid domainId,
        Guid certificateId,
        Guid tenantId,
        string reasonCode,
        string idempotencyKey,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;

        try
        {
            var domain = await _dbContext.Domains
                .IgnoreQueryFilters()
                .Include(d => d.SslCertificates.Where(c => !c.IsDeleted))
                .Include(d => d.SslCertificateOperations.Where(o => !o.IsDeleted))
                .SingleOrDefaultAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken)
                ?? throw new DomainNotFoundException("The requested domain was not found.");

            var certificate = domain.SslCertificates.SingleOrDefault(c =>
                    c.Id == certificateId &&
                    c.TenantId == tenantId &&
                    c.DomainId == domain.Id)
                ?? throw new DomainNotFoundException("The requested certificate was not found.");

            ValidateRevocationAdmission(domain, certificate, idempotencyKey);

            var operation = domain.SslCertificateOperations.SingleOrDefault(o =>
                o.OperationType == CertificateOperationType.Revocation &&
                string.Equals(o.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

            if (operation is not null)
            {
                var requestHash = HashRevocationRequest(tenantId, domain.Id, certificate.Id, certificate.Generation, reasonCode, idempotencyKey);
                if (operation.CertificateId != certificate.Id ||
                    !string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new DomainConflictException("Idempotency key was already used for a different certificate revocation request.");
                }

                if (operation.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running &&
                    (!operation.LeaseExpiresAtUtc.HasValue || operation.LeaseExpiresAtUtc.Value > now))
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RevocationClaim(domain, certificate, operation, "InProgress");
                }


                if (operation.Status == CertificateOperationStatus.Succeeded)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RevocationClaim(domain, certificate, operation, "Replayed");
                }

                if (operation.Status == CertificateOperationStatus.Superseded)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RevocationClaim(domain, certificate, operation, "Superseded");
                }

                if (operation.Status == CertificateOperationStatus.Failed && !operation.Retryable)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RevocationClaim(domain, certificate, operation, "TerminalFailure");
                }

                if (operation.Status == CertificateOperationStatus.Failed && operation.RetryAfterUtc > now)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RevocationClaim(domain, certificate, operation, "RetryDeferred");
                }

                if (operation.Status != CertificateOperationStatus.Succeeded)
                {
                    domain.CertificateOperationFencingToken += 1;
                    certificate.FencingToken = domain.CertificateOperationFencingToken;
                    operation.Status = CertificateOperationStatus.Running;
                    operation.Phase = CertificateOperationPhase.Revocation;
                    operation.AttemptCount += 1;
                    operation.FencingToken = domain.CertificateOperationFencingToken;
                    operation.LockOwnerReference = Guid.NewGuid();
                    operation.LeaseExpiresAtUtc = now.Add(_renewalOptions.Value.StaleOperationTimeout);
                    operation.RetryAfterUtc = null;
                    operation.UpdatedAtUtc = now;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await CommitIfPresentAsync(transaction, cancellationToken);
                return new RevocationClaim(domain, certificate, operation, Outcome: null);
            }

            await SupersedeExpiredOperationsAsync(
                domain,
                CertificateOperationType.Revocation,
                now,
                actorUserId,
                correlationId,
                cancellationToken);

            var conflictingOperation = domain.SslCertificateOperations.Any(o =>
                o.OperationType == CertificateOperationType.Revocation &&
                o.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running &&
                (!o.LeaseExpiresAtUtc.HasValue || o.LeaseExpiresAtUtc.Value > now));

            if (conflictingOperation)
            {
                throw new DomainConflictException("A certificate revocation operation is already in progress for this domain.");
            }

            domain.CertificateOperationFencingToken += 1;
            certificate.FencingToken = domain.CertificateOperationFencingToken;
            var operationId = Guid.NewGuid();
            operation = new SslCertificateOperation
            {
                Id = operationId,
                TenantId = tenantId,
                DomainId = domain.Id,
                CertificateId = certificate.Id,
                NormalizedHostName = domain.NormalizedHostName,
                OperationType = CertificateOperationType.Revocation,
                Status = CertificateOperationStatus.Running,
                Phase = CertificateOperationPhase.Revocation,
                OperationId = operationId,
                IdempotencyKey = idempotencyKey,
                RequestHash = HashRevocationRequest(tenantId, domain.Id, certificate.Id, certificate.Generation, reasonCode, idempotencyKey),
                ProviderRequestReference = $"cert-revocation:{operationId:N}",
                EdgeCertificateReference = certificate.EdgeCertificateReference,
                FencingToken = domain.CertificateOperationFencingToken,
                LockOwnerReference = operationId,
                LeaseExpiresAtUtc = now.Add(_renewalOptions.Value.StaleOperationTimeout),
                AttemptCount = 1,
                Retryable = false,
                CorrelationId = correlationId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsDeleted = false,
            };

            certificate.RevocationReason = reasonCode;
            certificate.OperationPhase = CertificateOperationPhase.Revocation;
            certificate.FailureCode = CertificateRevocationReasonCodes.IsCompromise(reasonCode) ? reasonCode : certificate.FailureCode;
            certificate.SafeFailureReason = CertificateRevocationReasonCodes.IsCompromise(reasonCode)
                ? "Certificate compromise requires emergency replacement and revocation."
                : certificate.SafeFailureReason;
            certificate.UpdatedAt = now;

            _dbContext.SslCertificateOperations.Add(operation);
            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "revocation_requested", actorUserId, correlationId, new
            {
                CertificateId = certificate.Id,
                OperationId = operation.Id,
                certificate.Generation,
                Reason = reasonCode,
                ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
                Outcome = "claimed",
            }));

            await _eventPublisher.PublishAsync(new CertificateRevocationRequestedEvent
            {
                TenantId = tenantId,
                DomainId = domain.Id,
                CertificateId = certificate.Id,
                OperationId = operation.Id,
                Generation = certificate.Generation,
                ReasonCode = reasonCode,
                UserId = actorUserId,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfPresentAsync(transaction, cancellationToken);
            return new RevocationClaim(domain, certificate, operation, Outcome: null);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await RollbackIfPresentAsync(transaction, cancellationToken);
            return await ReloadClaimAsync(domainId, certificateId, tenantId, idempotencyKey, cancellationToken);
        }
    }

    private async Task<CertificateProviderRevocationResult> RevokeAtProviderAsync(
        RevocationClaim claim,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var options = _providerOptions.CurrentValue;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.AccountReference) || options.DirectoryUrl is null || !options.DirectoryUrl.IsAbsoluteUri)
        {
            return CertificateProviderRevocationResult.Failure(CertificateProviderError.Create(
                CertificateProviderErrorCategory.ConfigurationError,
                "CERTIFICATE_PROVIDER_CONFIGURATION_INVALID",
                false,
                "Certificate provider configuration is invalid for revocation.",
                correlationId: correlationId));
        }

        _ = _accountSelector.Select(new CertificateProviderAccountSelectionRequest(
            options.Provider,
            options.Environment,
            options.AccountReference,
            options.ConfiguredIssuer,
            claim.Domain.TenantId,
            claim.Domain.Id));

        var context = new CertificateTenantOperationContext(
            claim.Domain.TenantId,
            claim.Domain.Id,
            claim.Certificate.Id,
            claim.Operation.Id,
            CertificateOperationType.Revocation,
            claim.Operation.IdempotencyKey,
            claim.Certificate.Generation,
            claim.Operation.FencingToken);

        return await _provider.RevokeCertificateAsync(new CertificateRevocationProviderRequest(
            context,
            options.Provider,
            options.Environment,
            claim.Certificate.ProviderRequestReference ?? claim.Certificate.CertificateArn,
            claim.Certificate.EdgeCertificateReference,
            reasonCode,
            correlationId,
            new CertificateRevocationCertificateIdentity(
                claim.Certificate.Generation,
                claim.Certificate.NormalizedHostName,
                claim.Certificate.Thumbprint,
                claim.Certificate.Fingerprint,
                claim.Certificate.SerialNumber,
                claim.Certificate.Issuer,
                claim.Certificate.Subject,
                claim.Certificate.NotBeforeUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(claim.Certificate.NotBeforeUtc.Value, DateTimeKind.Utc)),
                claim.Certificate.NotAfterUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(claim.Certificate.NotAfterUtc.Value, DateTimeKind.Utc)))), cancellationToken);
    }

    private async Task PersistAlreadyRevokedAsync(
        RevocationClaim claim,
        string reasonCode,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        claim.Operation.Status = CertificateOperationStatus.Succeeded;
        claim.Operation.Phase = CertificateOperationPhase.Revocation;
        claim.Operation.Retryable = false;
        claim.Operation.CompletedAtUtc = now;
        claim.Operation.UpdatedAtUtc = now;
        claim.Operation.LeaseExpiresAtUtc = null;
        claim.Operation.LockOwnerReference = null;
        claim.Certificate.RevocationReason ??= reasonCode;
        claim.Certificate.RevokedAtUtc ??= now;
        claim.Certificate.UpdatedAt = now;
        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "revocation_succeeded", actorUserId, correlationId, new
        {
            CertificateId = claim.Certificate.Id,
            OperationId = claim.Operation.Id,
            claim.Certificate.Generation,
            Reason = reasonCode,
            Outcome = "already_revoked",
        }));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkRevocationRequiresReplacementAsync(
        RevocationClaim claim,
        string reasonCode,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        claim.Certificate.RevocationReason = reasonCode;
        claim.Certificate.OperationPhase = CertificateOperationPhase.Revocation;
        claim.Certificate.FailureCode = reasonCode;
        claim.Certificate.SafeFailureReason = CertificateRevocationReasonCodes.IsCompromise(reasonCode)
            ? "Certificate compromise requires replacement before the current valid generation is revoked."
            : "Current valid certificate will remain authoritative until a replacement is proven.";
        claim.Certificate.LastFailureAtUtc = now;
        claim.Certificate.UpdatedAt = now;
        claim.Operation.Phase = CertificateOperationPhase.Revocation;
        claim.Operation.UpdatedAtUtc = now;
        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain,
            CertificateRevocationReasonCodes.IsCompromise(reasonCode) ? "certificate_compromised" : "recovery_started",
            actorUserId,
            correlationId,
            new
            {
                CertificateId = claim.Certificate.Id,
                OperationId = claim.Operation.Id,
                claim.Certificate.Generation,
                Reason = reasonCode,
                Outcome = "replacement_required_before_revocation",
            }));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistReplacementFailureAsync(
        Guid operationId,
        string failureCode,
        bool retryable,
        DateTime? retryAfterUtc,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var operation = await _dbContext.SslCertificateOperations
            .IgnoreQueryFilters()
            .SingleAsync(o => o.Id == operationId, cancellationToken);
        var now = DateTime.UtcNow;
        operation.Status = CertificateOperationStatus.Failed;
        operation.Phase = retryable ? CertificateOperationPhase.RetryBackoff : CertificateOperationPhase.Revocation;
        operation.FailureCode = failureCode;
        operation.SafeFailureReason = "Replacement certificate could not be proven before revocation.";
        operation.Retryable = retryable;
        operation.RetryAfterUtc = retryAfterUtc;
        operation.CompletedAtUtc = retryable ? null : now;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = null;
        operation.LockOwnerReference = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistProviderFailureAsync(
        RevocationClaim claim,
        CertificateProviderError error,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        DateTime? retryAfterUtc = error.Retryable
            ? error.RetryAfterUtc?.UtcDateTime ?? now.Add(ComputeBackoff(claim.Operation.AttemptCount))
            : null;
        claim.Certificate.FailureCode = error.Code;
        claim.Certificate.SafeFailureReason = error.SafeMessage;
        claim.Certificate.Retryable = error.Retryable;
        claim.Certificate.RetryAfterUtc = retryAfterUtc;
        claim.Certificate.LastFailureAtUtc = now;
        claim.Certificate.UpdatedAt = now;
        claim.Operation.Status = CertificateOperationStatus.Failed;
        claim.Operation.Phase = error.Retryable ? CertificateOperationPhase.RetryBackoff : CertificateOperationPhase.Revocation;
        claim.Operation.FailureCode = error.Code;
        claim.Operation.SafeFailureReason = error.SafeMessage;
        claim.Operation.Retryable = error.Retryable;
        claim.Operation.RetryAfterUtc = retryAfterUtc;
        claim.Operation.CompletedAtUtc = error.Retryable ? null : now;
        claim.Operation.UpdatedAtUtc = now;
        claim.Operation.LeaseExpiresAtUtc = null;
        claim.Operation.LockOwnerReference = null;
        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "revocation_failed", actorUserId, correlationId, new
        {
            CertificateId = claim.Certificate.Id,
            OperationId = claim.Operation.Id,
            claim.Certificate.Generation,
            Reason = claim.Certificate.RevocationReason,
            FailureCode = error.Code,
            error.Retryable,
            Outcome = "provider_failure",
        }));
        await _eventPublisher.PublishAsync(new CertificateRevocationFailedEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            CertificateId = claim.Certificate.Id,
            OperationId = claim.Operation.Id,
            Generation = claim.Certificate.Generation,
            ReasonCode = claim.Certificate.RevocationReason ?? string.Empty,
            FailureCode = error.Code,
            Retryable = error.Retryable,
            RetryAfterUtc = retryAfterUtc,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistRevocationSuccessAsync(
        RevocationClaim claim,
        string reasonCode,
        string? providerRevocationReference,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (claim.Certificate.Status != CertificateStatus.Expired)
        {
            claim.Certificate.TransitionTo(CertificateStatus.Revoked);
        }

        claim.Certificate.OperationPhase = CertificateOperationPhase.Revocation;
        claim.Certificate.RevokedAtUtc ??= now;
        claim.Certificate.RevokedByActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System;
        claim.Certificate.RevokedByActorId = actorUserId;
        claim.Certificate.RevocationReason = reasonCode;
        claim.Certificate.ProviderRevocationReference = providerRevocationReference;
        claim.Certificate.RenewalEligible = false;
        claim.Certificate.Retryable = false;
        claim.Certificate.RetryAfterUtc = null;
        claim.Certificate.UpdatedAt = now;
        claim.Operation.Status = CertificateOperationStatus.Succeeded;
        claim.Operation.Phase = CertificateOperationPhase.Revocation;
        claim.Operation.Retryable = false;
        claim.Operation.FailureCode = null;
        claim.Operation.SafeFailureReason = null;
        claim.Operation.CompletedAtUtc = now;
        claim.Operation.UpdatedAtUtc = now;
        claim.Operation.LeaseExpiresAtUtc = null;
        claim.Operation.LockOwnerReference = null;
        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "revocation_succeeded", actorUserId, correlationId, new
        {
            CertificateId = claim.Certificate.Id,
            OperationId = claim.Operation.Id,
            claim.Certificate.Generation,
            Reason = reasonCode,
            ProviderRevocationReference = providerRevocationReference,
            Outcome = "revoked",
        }));
        await _eventPublisher.PublishAsync(new CertificateRevokedEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            CertificateId = claim.Certificate.Id,
            OperationId = claim.Operation.Id,
            Generation = claim.Certificate.Generation,
            ReasonCode = reasonCode,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void DegradeDomainSsl(Domain domain, SslCertificate certificate, DateTime now)
    {
        certificate.RenewalEligible = false;
        certificate.Retryable = false;
        certificate.UpdatedAt = now;

        if (domain.LifecycleState is DomainLifecycleState.SslActive or DomainLifecycleState.Primary &&
            domain.SslCertificateId == certificate.Id)
        {
            domain.LifecycleState = DomainLifecycleState.Verified;
            domain.DomainType = DomainType.Alias;
            domain.SslCertificateId = null;
            domain.UpdatedAtUtc = now;
        }
    }

    private async Task ReloadClaimStateAsync(RevocationClaim claim, CancellationToken cancellationToken)
    {
        await _dbContext.Entry(claim.Domain).ReloadAsync(cancellationToken);
        await _dbContext.Entry(claim.Certificate).ReloadAsync(cancellationToken);
        await _dbContext.Entry(claim.Operation).ReloadAsync(cancellationToken);
    }

    private static void ValidateRevocationAdmission(Domain domain, SslCertificate certificate, string idempotencyKey)
    {
        if (domain.IsDeleted || domain.LifecycleState == DomainLifecycleState.Deleted || certificate.IsDeleted)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "Deleted domains or certificates cannot be revoked through this command.");
        }

        if (certificate.TenantId != domain.TenantId || certificate.DomainId != domain.Id)
        {
            throw new DomainConflictException("Certificate does not belong to the requested domain.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "A valid idempotency key is required.");
        }
    }

    private static string NormalizeIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "A valid idempotency key is required.");
        }

        return idempotencyKey.Trim();
    }

    private TimeSpan ComputeBackoff(int attemptCount)
    {
        var options = _renewalOptions.Value;
        var exponent = Math.Max(0, attemptCount - 1);
        var seconds = options.RetryBackoff.TotalSeconds * Math.Pow(2, Math.Min(exponent, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, options.MaxRetryDelay.TotalSeconds));
    }

    private async Task<RevocationClaim> ReloadClaimAsync(Guid domainId, Guid certificateId, Guid tenantId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var domain = await _dbContext.Domains
            .IgnoreQueryFilters()
            .Include(d => d.SslCertificates.Where(c => !c.IsDeleted))
            .Include(d => d.SslCertificateOperations.Where(o => !o.IsDeleted))
            .SingleAsync(d => d.Id == domainId && d.TenantId == tenantId, cancellationToken);
        var certificate = domain.SslCertificates.Single(c => c.Id == certificateId && c.TenantId == tenantId);
        var operation = domain.SslCertificateOperations.Single(o =>
            o.OperationType == CertificateOperationType.Revocation &&
            o.IdempotencyKey == idempotencyKey);
        var outcome = operation.Status switch
        {
            CertificateOperationStatus.Succeeded => "Replayed",
            CertificateOperationStatus.Superseded => "Superseded",
            CertificateOperationStatus.Failed when !operation.Retryable => "TerminalFailure",
            CertificateOperationStatus.Failed when operation.RetryAfterUtc > DateTime.UtcNow => "RetryDeferred",
            _ => "InProgress",
        };
        return new RevocationClaim(domain, certificate, operation, outcome);
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
            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "revocation_operation_superseded", actorUserId, correlationId, new
            {
                OperationId = stale.Id,
                stale.CertificateId,
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("UQ_", StringComparison.OrdinalIgnoreCase) == true;

    private static string HashRevocationRequest(Guid tenantId, Guid domainId, Guid certificateId, int generation, string reasonCode, string idempotencyKey)
    {
        var value = $"{tenantId:N}|{domainId:N}|{certificateId:N}|{generation}|{reasonCode}|{idempotencyKey}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static DomainAuditEntry CreateAudit(
        Domain domain,
        string action,
        Guid? actorUserId,
        string correlationId,
        object safeMetadata) =>
        new()
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
                domain.SslCertificateId,
                CertificateRevocation = safeMetadata,
            }),
            TraceId = correlationId,
            TimestampUtc = DateTime.UtcNow,
        };

    private static CertificateRevocationServiceResult MapResult(
        RevocationClaim claim,
        string reasonCode,
        string outcome,
        bool succeeded) =>
        new(
            claim.Domain.TenantId,
            claim.Domain.Id,
            claim.Certificate.Id,
            claim.Operation.Id,
            claim.Certificate.Provider,
            claim.Certificate.Generation,
            claim.Operation.Status,
            claim.Operation.Phase,
            claim.Certificate.Status,
            reasonCode,
            outcome,
            succeeded,
            claim.Operation.Retryable,
            claim.Operation.FailureCode,
            claim.Operation.RetryAfterUtc);

    private sealed record RevocationClaim(
        Domain Domain,
        SslCertificate Certificate,
        SslCertificateOperation Operation,
        string? Outcome);
}
