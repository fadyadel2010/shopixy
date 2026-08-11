using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed class CertificateRenewalService : ICertificateRenewalService
{
    private const string RenewalProviderRequestPrefix = "cert-renewal";

    private readonly ApplicationDbContext _dbContext;
    private readonly ICertificateProvider _provider;
    private readonly ICertificateProviderAccountSelector _accountSelector;
    private readonly IOptionsMonitor<CertificateProviderOptions> _providerOptions;
    private readonly IOptions<CertificateRenewalSchedulerOptions> _renewalOptions;
    private readonly ICaddyEdgeClient _edgeClient;
    private readonly IEdgeCertificateProbe _certificateProbe;
    private readonly ICertificateActivationService _activationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CertificateRenewalService> _logger;

    public CertificateRenewalService(
        ApplicationDbContext dbContext,
        ICertificateProvider provider,
        ICertificateProviderAccountSelector accountSelector,
        IOptionsMonitor<CertificateProviderOptions> providerOptions,
        IOptions<CertificateRenewalSchedulerOptions> renewalOptions,
        ICaddyEdgeClient edgeClient,
        IEdgeCertificateProbe certificateProbe,
        ICertificateActivationService activationService,
        IEventPublisher eventPublisher,
        ILogger<CertificateRenewalService> logger)
    {
        _dbContext = dbContext;
        _provider = provider;
        _accountSelector = accountSelector;
        _providerOptions = providerOptions;
        _renewalOptions = renewalOptions;
        _edgeClient = edgeClient;
        _certificateProbe = certificateProbe;
        _activationService = activationService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<CertificateRenewalServiceResult> RenewAsync(
        CertificateRenewalSchedulerCandidate candidate,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var options = _renewalOptions.Value;
        var idempotencyKey = BuildRenewalIdempotencyKey(candidate);
        var claim = await ClaimRenewalAsync(candidate, idempotencyKey, correlationId, cancellationToken);

        if (claim.Outcome is not null)
        {
            return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, claim.Outcome, claim.Operation.Retryable, claim.Operation.FailureCode, claim.Operation.RetryAfterUtc);
        }

        if (claim.Operation.AttemptCount > options.MaxAttempts)
        {
            await PersistFailureAsync(claim, CertificateRevocationReasonCodes.RenewalExhausted, "Certificate renewal attempt limit was reached.", retryable: false, correlationId, cancellationToken);
            return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "RenewalExhausted", false, CertificateRevocationReasonCodes.RenewalExhausted, null);
        }

        if (claim.ActiveCertificate.NotAfterUtc.HasValue &&
            claim.ActiveCertificate.NotAfterUtc.Value <= DateTime.UtcNow &&
            claim.ActiveCertificate.Status != CertificateStatus.Expired)
        {
            await PersistExpiredAndDegradedAsync(claim, correlationId, cancellationToken);
        }

        if (await IsProviderRateLimitedAsync(candidate, claim.Operation.Id, options, cancellationToken))
        {
            var retryAfter = DateTime.UtcNow.Add(options.ProviderRateLimitWindow);
            await PersistFailureAsync(claim, "RATE_LIMITED", "Certificate renewal provider attempt budget is exhausted for the configured window.", retryable: true, correlationId, cancellationToken, retryAfter);
            return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "RateLimited", true, "RATE_LIMITED", retryAfter);
        }

        var routeState = await _edgeClient.GetDomainRouteStateAsync(new CaddyDomainRouteQuery(
            candidate.TenantId,
            candidate.DomainId,
            candidate.NormalizedHostName,
            correlationId), cancellationToken);

        if (!routeState.Exists || !routeState.MatchesDesiredState)
        {
            await PersistFailureAsync(claim, "EDGE_STATE_FAILURE", "Caddy edge route is not present for this active SSL domain.", retryable: true, correlationId, cancellationToken);
            return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "EdgeStateFailure", true, "EDGE_STATE_FAILURE", claim.Operation.RetryAfterUtc);
        }

        var observed = await _certificateProbe.ProbeAsync(new EdgeCertificateProbeRequest(
            candidate.TenantId,
            candidate.DomainId,
            candidate.CertificateId,
            claim.Operation.Id,
            candidate.NormalizedHostName,
            ExpectedThumbprint: null,
            ExpectedFingerprint: null,
            ExpectedSerialNumber: null,
            ExpectedIssuer: null,
            ExpectedNotAfterUtc: null,
            correlationId), cancellationToken);

        if (!observed.Succeeded || observed.Certificate is null)
        {
            await PersistFailureAsync(claim, observed.FailureCode ?? "SNI_FAILURE", observed.SafeFailureReason ?? "External SNI proof failed while reconciling renewal.", observed.Retryable, correlationId, cancellationToken);
            return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "SniFailure", observed.Retryable, observed.FailureCode ?? "SNI_FAILURE", claim.Operation.RetryAfterUtc);
        }

        if (IsReplacementObserved(claim.ActiveCertificate, observed.Certificate))
        {
            await PersistReplacementIssuedAsync(claim, observed.Certificate, providerReference: "caddy-observed-renewal", correlationId, cancellationToken);
        }
        else
        {
            var providerResult = await RequestProviderRenewalAsync(claim, correlationId, cancellationToken);
            if (!providerResult.Succeeded)
            {
                var error = providerResult.Error!;
                await PersistFailureAsync(
                    claim,
                    error.Code,
                    error.SafeMessage,
                    error.Retryable,
                    correlationId,
                    cancellationToken,
                    error.RetryAfterUtc?.UtcDateTime);
                return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "ProviderFailure", error.Retryable, error.Code, claim.Operation.RetryAfterUtc);
            }

            if (providerResult.Operation?.ProviderStatus != CertificateProviderOperationStatus.Succeeded ||
                providerResult.Operation.CertificateMetadata is null)
            {
                await PersistDeferredAsync(claim, providerResult.Operation, correlationId, cancellationToken);
                return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "Deferred", true, null, claim.Operation.RetryAfterUtc);
            }

            var providerIdentity = ToEdgeIdentity(
                candidate.NormalizedHostName,
                providerResult.Operation.CertificateMetadata);
            if (!IsReplacementObserved(claim.ActiveCertificate, providerIdentity))
            {
                await PersistDeferredAsync(claim, providerResult.Operation, correlationId, cancellationToken);
                return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "Deferred", true, "REPLACEMENT_NOT_OBSERVED", claim.Operation.RetryAfterUtc);
            }

            await PersistReplacementIssuedAsync(
                claim,
                providerIdentity,
                providerResult.Operation.ProviderOperationReference,
                correlationId,
                cancellationToken);
        }

        var activation = await _activationService.ActivateAsync(
            candidate.DomainId,
            new CertificateActivationRequest
            {
                CertificateId = claim.ReplacementCertificate.Id,
                IdempotencyKey = $"renewal-activation:{claim.Operation.Id:N}",
            },
            candidate.TenantId,
            actorUserId: null,
            correlationId,
            cancellationToken);

        if (activation.CertificateStatus != CertificateStatus.Active || activation.OperationStatus != CertificateOperationStatus.Succeeded)
        {
            await PersistFailureAsync(claim, activation.FailureCode ?? "ACTIVATION_FAILURE", activation.SafeFailureReason ?? "Replacement activation did not complete.", activation.Retryable, correlationId, cancellationToken);
            return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "ActivationFailure", activation.Retryable, activation.FailureCode ?? "ACTIVATION_FAILURE", claim.Operation.RetryAfterUtc);
        }

        await PersistRenewalSuccessAsync(claim, activation, correlationId, cancellationToken);
        return MapResult(candidate, claim.ActiveCertificate, claim.ReplacementCertificate, claim.Operation, "Renewed", false, null, null);
    }

    private async Task<RenewalClaim> ClaimRenewalAsync(
        CertificateRenewalSchedulerCandidate candidate,
        string idempotencyKey,
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
                .SingleOrDefaultAsync(d => d.Id == candidate.DomainId && d.TenantId == candidate.TenantId, cancellationToken)
                ?? throw new DomainNotFoundException("The requested domain was not found.");

            var active = domain.SslCertificates.SingleOrDefault(c => c.Id == candidate.CertificateId && c.TenantId == candidate.TenantId)
                ?? throw new DomainNotFoundException("The requested certificate was not found.");

            ValidateRenewalAdmission(domain, active, candidate, now);

            var operation = domain.SslCertificateOperations.SingleOrDefault(o =>
                o.OperationType == CertificateOperationType.Renewal &&
                string.Equals(o.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

            if (operation is not null)
            {
                var replacement = operation.EdgeCertificateReference is not null || active.ReplacementCertificateId.HasValue
                    ? domain.SslCertificates.SingleOrDefault(c => c.OperationId == operation.Id || c.Id == active.ReplacementCertificateId)
                    : null;

                if (operation.Status == CertificateOperationStatus.Succeeded)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RenewalClaim(domain, active, replacement ?? active, operation, "AlreadyRenewed");
                }

                if (operation.Status == CertificateOperationStatus.Superseded)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RenewalClaim(domain, active, replacement ?? active, operation, "Superseded");
                }

                if (operation.Status == CertificateOperationStatus.Failed && !operation.Retryable)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RenewalClaim(domain, active, replacement ?? active, operation,
                        string.Equals(operation.FailureCode, CertificateRevocationReasonCodes.RenewalExhausted, StringComparison.OrdinalIgnoreCase)
                            ? "RenewalExhausted"
                            : "TerminalFailure");
                }

                if (operation.Status == CertificateOperationStatus.Failed &&
                    operation.Retryable &&
                    operation.RetryAfterUtc.HasValue &&
                    operation.RetryAfterUtc.Value > now)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RenewalClaim(domain, active, replacement ?? active, operation, "RetryDeferred");
                }

                if (operation.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running &&
                    operation.LeaseExpiresAtUtc.HasValue &&
                    operation.LeaseExpiresAtUtc.Value > now)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RenewalClaim(domain, active, replacement ?? active, operation, "InProgress");
                }

                domain.CertificateOperationFencingToken += 1;
                active.FencingToken = domain.CertificateOperationFencingToken;
                operation.Status = CertificateOperationStatus.Running;
                operation.Phase = CertificateOperationPhase.RenewalWindow;
                operation.AttemptCount += 1;
                operation.FencingToken = domain.CertificateOperationFencingToken;
                operation.LockOwnerReference = operation.Id;
                operation.LeaseExpiresAtUtc = now.Add(_renewalOptions.Value.StaleOperationTimeout);
                operation.RetryAfterUtc = null;
                operation.UpdatedAtUtc = now;

                replacement ??= CreateReplacementCertificate(domain, active, operation, idempotencyKey, correlationId, now);
                replacement.FencingToken = domain.CertificateOperationFencingToken;
                if (replacement.Status == CertificateStatus.Failed && replacement.Retryable)
                {
                    replacement.Status = CertificateStatus.Pending;
                    replacement.OperationPhase = CertificateOperationPhase.RenewalWindow;
                    replacement.RetryAfterUtc = null;
                    replacement.FailureCode = null;
                    replacement.SafeFailureReason = null;
                    replacement.UpdatedAt = now;
                }

                if (!_dbContext.SslCertificates.Local.Any(c => c.Id == replacement.Id) &&
                    !domain.SslCertificates.Any(c => c.Id == replacement.Id))
                {
                    _dbContext.SslCertificates.Add(replacement);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await CommitIfPresentAsync(transaction, cancellationToken);
                return new RenewalClaim(domain, active, replacement, operation, null);
            }

            await SupersedeExpiredOperationsAsync(
                domain,
                CertificateOperationType.Renewal,
                now,
                correlationId,
                cancellationToken);

            var conflictingOperation = domain.SslCertificateOperations.Any(o =>
                o.OperationType == CertificateOperationType.Renewal &&
                o.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running &&
                (!o.LeaseExpiresAtUtc.HasValue || o.LeaseExpiresAtUtc.Value > now));

            if (conflictingOperation)
            {
                throw new DomainConflictException("A certificate renewal operation is already in progress for this domain.");
            }

            domain.CertificateOperationFencingToken += 1;
            active.FencingToken = domain.CertificateOperationFencingToken;
            var operationId = Guid.NewGuid();
            operation = new SslCertificateOperation
            {
                Id = operationId,
                TenantId = candidate.TenantId,
                DomainId = candidate.DomainId,
                CertificateId = active.Id,
                NormalizedHostName = candidate.NormalizedHostName,
                OperationType = CertificateOperationType.Renewal,
                Status = CertificateOperationStatus.Running,
                Phase = CertificateOperationPhase.RenewalWindow,
                OperationId = operationId,
                IdempotencyKey = idempotencyKey,
                RequestHash = HashRenewalRequest(candidate, idempotencyKey),
                ProviderRequestReference = $"{RenewalProviderRequestPrefix}:{operationId:N}",
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

            var replacementCertificate = CreateReplacementCertificate(domain, active, operation, idempotencyKey, correlationId, now);
            replacementCertificate.FencingToken = domain.CertificateOperationFencingToken;
            active.OperationPhase = CertificateOperationPhase.RenewalWindow;
            active.LastRenewalAttemptUtc = now;
            active.RenewalAttemptCount += 1;
            active.ReplacementCertificateId = replacementCertificate.Id;
            active.UpdatedAt = now;

            _dbContext.SslCertificateOperations.Add(operation);
            _dbContext.SslCertificates.Add(replacementCertificate);
            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "certificate_renewal_started", correlationId, new
            {
                ActiveCertificateId = active.Id,
                ReplacementCertificateId = replacementCertificate.Id,
                OperationId = operation.Id,
                active.Generation,
                ReplacementGeneration = replacementCertificate.Generation,
            }));

            await _eventPublisher.PublishAsync(new CertificateRenewalStartedEvent
            {
                TenantId = candidate.TenantId,
                DomainId = candidate.DomainId,
                CertificateId = active.Id,
                OperationId = operation.Id,
                Generation = active.Generation,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfPresentAsync(transaction, cancellationToken);
            return new RenewalClaim(domain, active, replacementCertificate, operation, null);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await RollbackIfPresentAsync(transaction, cancellationToken);
            return await ReloadClaimAsync(candidate, idempotencyKey, cancellationToken);
        }
    }

    private async Task<CertificateProviderOrderResult> RequestProviderRenewalAsync(
        RenewalClaim claim,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var options = _providerOptions.CurrentValue;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.AccountReference) || options.DirectoryUrl is null || !options.DirectoryUrl.IsAbsoluteUri)
        {
            return CertificateProviderOrderResult.Failure(CertificateProviderError.Create(
                CertificateProviderErrorCategory.ConfigurationError,
                "CONFIGURATION_FAILURE",
                false,
                "Certificate provider configuration is invalid for renewal.",
                correlationId: correlationId));
        }

        var context = new CertificateTenantOperationContext(
            claim.Domain.TenantId,
            claim.Domain.Id,
            claim.ReplacementCertificate.Id,
            claim.Operation.Id,
            CertificateOperationType.Renewal,
            claim.Operation.IdempotencyKey,
            claim.ReplacementCertificate.Generation,
            claim.Operation.FencingToken);

        var accountSelection = _accountSelector.Select(new CertificateProviderAccountSelectionRequest(
            options.Provider,
            options.Environment,
            options.AccountReference,
            options.ConfiguredIssuer,
            claim.Domain.TenantId,
            claim.Domain.Id));

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
            return CertificateProviderOrderResult.Failure(accountResult.Error!);
        }

        return await _provider.CreateOrderAsync(new CertificateIssuanceRequest(
            context,
            claim.Domain.NormalizedHostName,
            Array.AsReadOnly(new[] { claim.Domain.NormalizedHostName }),
            accountResult.Account,
            CertificateChallengePolicy.Default,
            claim.Operation.ProviderRequestReference,
            correlationId), cancellationToken);
    }

    private async Task PersistReplacementIssuedAsync(
        RenewalClaim claim,
        EdgeCertificateIdentity identity,
        string? providerReference,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var replacement = claim.ReplacementCertificate;
        if (replacement.Status == CertificateStatus.Pending)
        {
            replacement.TransitionTo(CertificateStatus.Requested);
        }

        if (replacement.Status == CertificateStatus.Requested)
        {
            replacement.TransitionTo(CertificateStatus.Authorizing);
        }

        if (replacement.Status == CertificateStatus.Authorizing || replacement.Status == CertificateStatus.Renewing)
        {
            replacement.TransitionTo(CertificateStatus.Issued);
        }

        replacement.OperationPhase = CertificateOperationPhase.MetadataSync;
        replacement.Issuer = identity.Issuer;
        replacement.Subject = identity.Subject;
        replacement.SubjectAlternativeNamesJson = JsonSerializer.Serialize(identity.SubjectAlternativeNames);
        replacement.Thumbprint = identity.Thumbprint;
        replacement.Fingerprint = identity.Fingerprint;
        replacement.SerialNumber = identity.SerialNumber;
        replacement.SignatureAlgorithm = identity.SignatureAlgorithm;
        replacement.NotBeforeUtc = identity.NotBeforeUtc;
        replacement.NotAfterUtc = identity.NotAfterUtc;
        replacement.IssuedAtUtc = now;
        replacement.EdgeCertificateReference = providerReference ?? replacement.EdgeCertificateReference;
        replacement.UpdatedAt = now;

        claim.Operation.Phase = CertificateOperationPhase.MetadataSync;
        claim.Operation.EdgeCertificateReference = providerReference ?? claim.Operation.EdgeCertificateReference;
        claim.Operation.UpdatedAtUtc = now;
        claim.Operation.LeaseExpiresAtUtc = now.Add(_renewalOptions.Value.StaleOperationTimeout);

        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "replacement_issued", correlationId, new
        {
            ActiveCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = replacement.Id,
            OperationId = claim.Operation.Id,
            replacement.Generation,
            replacement.Thumbprint,
            replacement.NotAfterUtc,
        }));

        await _eventPublisher.PublishAsync(new CertificateReplacementIssuedEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            OldCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = replacement.Id,
            OperationId = claim.Operation.Id,
            Generation = replacement.Generation,
            Thumbprint = replacement.Thumbprint,
            NotAfterUtc = replacement.NotAfterUtc,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistDeferredAsync(
        RenewalClaim claim,
        CertificateProviderOperationMetadata? metadata,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        claim.ReplacementCertificate.Status = CertificateStatus.Requested;
        claim.ReplacementCertificate.OperationPhase = CertificateOperationPhase.Authorization;
        claim.ReplacementCertificate.EdgeCertificateReference = metadata?.ProviderOperationReference ?? claim.ReplacementCertificate.EdgeCertificateReference;
        claim.ReplacementCertificate.UpdatedAt = now;
        claim.Operation.Status = CertificateOperationStatus.Running;
        claim.Operation.Phase = CertificateOperationPhase.Authorization;
        claim.Operation.EdgeCertificateReference = metadata?.ProviderOperationReference ?? claim.Operation.EdgeCertificateReference;
        claim.Operation.Retryable = true;
        claim.Operation.RetryAfterUtc = now.Add(ComputeBackoff(claim.Operation.AttemptCount));
        claim.Operation.UpdatedAtUtc = now;
        claim.Operation.LeaseExpiresAtUtc = now.Add(_renewalOptions.Value.StaleOperationTimeout);
        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "renewal_deferred", correlationId, new
        {
            ActiveCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = claim.ReplacementCertificate.Id,
            OperationId = claim.Operation.Id,
            claim.Operation.RetryAfterUtc,
        }));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistFailureAsync(
        RenewalClaim claim,
        string failureCode,
        string safeReason,
        bool retryable,
        string correlationId,
        CancellationToken cancellationToken,
        DateTime? retryAfterUtc = null)
    {
        var now = DateTime.UtcNow;
        retryAfterUtc ??= retryable ? now.Add(ComputeBackoff(claim.Operation.AttemptCount)) : null;

        claim.ActiveCertificate.Retryable = retryable;
        claim.ActiveCertificate.FailureCode = failureCode;
        claim.ActiveCertificate.SafeFailureReason = safeReason;
        claim.ActiveCertificate.LastFailureAtUtc = now;
        claim.ActiveCertificate.RetryAfterUtc = retryAfterUtc;
        claim.ActiveCertificate.BackoffReason = retryable ? failureCode : null;
        claim.ActiveCertificate.UpdatedAt = now;

        claim.ReplacementCertificate.FailureCode = failureCode;
        claim.ReplacementCertificate.SafeFailureReason = safeReason;
        claim.ReplacementCertificate.Retryable = retryable;
        claim.ReplacementCertificate.RetryAfterUtc = retryAfterUtc;
        claim.ReplacementCertificate.LastFailureAtUtc = now;
        claim.ReplacementCertificate.OperationPhase = retryable ? CertificateOperationPhase.RetryBackoff : claim.Operation.Phase;
        if (!retryable)
        {
            claim.ReplacementCertificate.Status = CertificateStatus.Failed;
        }

        claim.Operation.Status = CertificateOperationStatus.Failed;
        claim.Operation.Phase = retryable ? CertificateOperationPhase.RetryBackoff : claim.Operation.Phase;
        claim.Operation.FailureCode = failureCode;
        claim.Operation.SafeFailureReason = safeReason;
        claim.Operation.Retryable = retryable;
        claim.Operation.RetryAfterUtc = retryAfterUtc;
        claim.Operation.CompletedAtUtc = retryable ? null : now;
        claim.Operation.UpdatedAtUtc = now;
        claim.Operation.LeaseExpiresAtUtc = null;
        claim.Operation.LockOwnerReference = null;

        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, retryable ? "renewal_deferred" : "renewal_failed", correlationId, new
        {
            ActiveCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = claim.ReplacementCertificate.Id,
            OperationId = claim.Operation.Id,
            failureCode,
            retryable,
            retryAfterUtc,
        }));

        await _eventPublisher.PublishAsync(new CertificateRenewalFailedEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            CertificateId = claim.ActiveCertificate.Id,
            OperationId = claim.Operation.Id,
            Generation = claim.ActiveCertificate.Generation,
            FailureCode = failureCode,
            Retryable = retryable,
            RetryAfterUtc = retryAfterUtc,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistExpiredAndDegradedAsync(
        RenewalClaim claim,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        claim.ActiveCertificate.TransitionTo(CertificateStatus.Expired);
        claim.ActiveCertificate.OperationPhase = CertificateOperationPhase.RetryBackoff;
        claim.ActiveCertificate.FailureCode = CertificateRevocationReasonCodes.Expired;
        claim.ActiveCertificate.SafeFailureReason = "The authoritative certificate has expired and SSL recovery is required.";
        claim.ActiveCertificate.RenewalEligible = false;
        claim.ActiveCertificate.Retryable = true;
        claim.ActiveCertificate.LastFailureAtUtc = now;
        claim.ActiveCertificate.UpdatedAt = now;

        var degraded = false;
        if (claim.Domain.LifecycleState is DomainLifecycleState.SslActive or DomainLifecycleState.Primary &&
            claim.Domain.SslCertificateId == claim.ActiveCertificate.Id)
        {
            claim.Domain.LifecycleState = DomainLifecycleState.Verified;
            claim.Domain.DomainType = DomainType.Alias;
            claim.Domain.SslCertificateId = null;
            claim.Domain.UpdatedAtUtc = now;
            degraded = true;
        }

        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "certificate_expired", correlationId, new
        {
            ActiveCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = claim.ReplacementCertificate.Id,
            OperationId = claim.Operation.Id,
            claim.ActiveCertificate.Generation,
            Reason = CertificateRevocationReasonCodes.Expired,
            claim.ActiveCertificate.NotAfterUtc,
        }));

        if (degraded)
        {
            _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "ssl_degraded", correlationId, new
            {
                ActiveCertificateId = claim.ActiveCertificate.Id,
                OperationId = claim.Operation.Id,
                Reason = CertificateRevocationReasonCodes.Expired,
                DomainLifecycleState = claim.Domain.LifecycleState,
            }));
        }

        await _eventPublisher.PublishAsync(new CertificateExpiredEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            CertificateId = claim.ActiveCertificate.Id,
            OperationId = claim.Operation.Id,
            Generation = claim.ActiveCertificate.Generation,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);

        if (degraded)
        {
            await _eventPublisher.PublishAsync(new DomainSslDegradedEvent
            {
                TenantId = claim.Domain.TenantId,
                DomainId = claim.Domain.Id,
                CertificateId = claim.ActiveCertificate.Id,
                OperationId = claim.Operation.Id,
                ReasonCode = CertificateRevocationReasonCodes.Expired,
                CorrelationId = correlationId,
                IdempotencyKey = claim.Operation.IdempotencyKey,
            }, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistRenewalSuccessAsync(
        RenewalClaim claim,
        CertificateActivationResponse activation,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        claim.Operation.Status = CertificateOperationStatus.Succeeded;
        claim.Operation.Phase = CertificateOperationPhase.ActivationProbe;
        claim.Operation.Retryable = false;
        claim.Operation.RetryAfterUtc = null;
        claim.Operation.FailureCode = null;
        claim.Operation.SafeFailureReason = null;
        claim.Operation.CompletedAtUtc = now;
        claim.Operation.UpdatedAtUtc = now;
        claim.Operation.LeaseExpiresAtUtc = null;
        claim.Operation.LockOwnerReference = null;

        claim.ActiveCertificate.RenewalEligible = false;
        claim.ActiveCertificate.RetryAfterUtc = null;
        claim.ActiveCertificate.BackoffReason = null;
        claim.ActiveCertificate.UpdatedAt = now;
        claim.ReplacementCertificate.NextRenewalUtc = CalculateNextRenewalUtc(claim.ReplacementCertificate.NotAfterUtc);
        claim.ReplacementCertificate.RenewalEligible = true;
        claim.ReplacementCertificate.UpdatedAt = now;

        _dbContext.DomainAuditEntries.Add(CreateAudit(claim.Domain, "renewal_reconciled", correlationId, new
        {
            ActiveCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = claim.ReplacementCertificate.Id,
            OperationId = claim.Operation.Id,
            activation.LastObservedThumbprint,
            activation.CertificateStatus,
            activation.DomainLifecycleState,
        }));

        await _eventPublisher.PublishAsync(new CertificateRenewedEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            OldCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = claim.ReplacementCertificate.Id,
            OperationId = claim.Operation.Id,
            Generation = claim.ReplacementCertificate.Generation,
            Thumbprint = claim.ReplacementCertificate.Thumbprint,
            NotAfterUtc = claim.ReplacementCertificate.NotAfterUtc,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);

        await _eventPublisher.PublishAsync(new CertificateGenerationRetiredEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            OldCertificateId = claim.ActiveCertificate.Id,
            ReplacementCertificateId = claim.ReplacementCertificate.Id,
            OperationId = claim.Operation.Id,
            OldGeneration = claim.ActiveCertificate.Generation,
            ReplacementGeneration = claim.ReplacementCertificate.Generation,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);

        await _eventPublisher.PublishAsync(new DomainSslRestoredEvent
        {
            TenantId = claim.Domain.TenantId,
            DomainId = claim.Domain.Id,
            CertificateId = claim.ReplacementCertificate.Id,
            OperationId = claim.Operation.Id,
            Generation = claim.ReplacementCertificate.Generation,
            CorrelationId = correlationId,
            IdempotencyKey = claim.Operation.IdempotencyKey,
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsProviderRateLimitedAsync(
        CertificateRenewalSchedulerCandidate candidate,
        Guid currentOperationId,
        CertificateRenewalSchedulerOptions options,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.Subtract(options.ProviderRateLimitWindow);
        var attempts = await _dbContext.SslCertificateOperations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(o =>
                o.TenantId == candidate.TenantId &&
                o.DomainId == candidate.DomainId &&
                o.Id != currentOperationId &&
                o.OperationType == CertificateOperationType.Renewal &&
                !o.IsDeleted &&
                o.CreatedAtUtc >= cutoff,
                cancellationToken);

        return attempts >= options.ProviderAttemptBudget;
    }

    private static void ValidateRenewalAdmission(
        Domain domain,
        SslCertificate active,
        CertificateRenewalSchedulerCandidate candidate,
        DateTime now)
    {
        if (domain.IsDeleted || domain.LifecycleState is DomainLifecycleState.Deleted or DomainLifecycleState.Archived)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "Deleted or archived domains cannot be renewed.");
        }

        if (domain.LifecycleState is not (DomainLifecycleState.SslActive or DomainLifecycleState.Primary) ||
            domain.SslCertificateId != active.Id ||
            active.TenantId != domain.TenantId ||
            active.DomainId != domain.Id)
        {
            var expiredRecovery = active.Status == CertificateStatus.Expired &&
                active.FailureCode == CertificateRevocationReasonCodes.Expired &&
                domain.LifecycleState == DomainLifecycleState.Verified &&
                domain.SslCertificateId is null;

            if (!expiredRecovery)
            {
                throw new DomainOperationException(DomainOperationErrorCode.NotVerifiedOrSslActive, "Only the domain's active certificate can be renewed.");
            }
        }

        if (active.Status is not (CertificateStatus.Active or CertificateStatus.Expired))
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "Only active or expired recovery certificates can enter renewal recovery.");
        }

        if (active.Status is CertificateStatus.Revoked || active.Provider == ProviderType.CustomUpload)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "This certificate is not eligible for automatic renewal.");
        }

        EdgeCertificateHostName.Validate(domain.NormalizedHostName);

        if (active.NextRenewalUtc is null && active.NotAfterUtc.HasValue)
        {
            active.NextRenewalUtc = active.NotAfterUtc.Value.AddDays(-30);
            active.UpdatedAt = now;
        }

        if (!string.Equals(active.NormalizedHostName, candidate.NormalizedHostName, StringComparison.Ordinal))
        {
            throw new DomainConflictException("Certificate hostname does not match the renewal candidate.");
        }
    }

    private static SslCertificate CreateReplacementCertificate(
        Domain domain,
        SslCertificate active,
        SslCertificateOperation operation,
        string idempotencyKey,
        string correlationId,
        DateTime now)
    {
        return new SslCertificate
        {
            Id = Guid.NewGuid(),
            TenantId = domain.TenantId,
            DomainId = domain.Id,
            NormalizedHostName = domain.NormalizedHostName,
            Provider = active.Provider,
            ProviderImplementation = "CertificateRenewalService",
            ProviderAccountReference = active.ProviderAccountReference,
            ProviderEnvironment = active.ProviderEnvironment,
            Status = CertificateStatus.Pending,
            OperationPhase = CertificateOperationPhase.RenewalWindow,
            Generation = Math.Max(1, domain.SslCertificates.Max(c => c.Generation) + 1),
            OperationId = operation.Id,
            IdempotencyKey = idempotencyKey,
            ProviderRequestReference = operation.ProviderRequestReference,
            SubjectAlternativeNamesJson = JsonSerializer.Serialize(new[] { domain.NormalizedHostName }),
            RenewalEligible = false,
            Retryable = false,
            CorrelationId = correlationId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        };
    }

    private static bool IsReplacementObserved(SslCertificate active, EdgeCertificateIdentity observed)
    {
        if (observed.NotAfterUtc <= DateTime.UtcNow)
        {
            return false;
        }

        var thumbprintChanged = !string.IsNullOrWhiteSpace(observed.Thumbprint) &&
            !string.Equals(NormalizeHex(active.Thumbprint), NormalizeHex(observed.Thumbprint), StringComparison.OrdinalIgnoreCase);
        var serialChanged = !string.IsNullOrWhiteSpace(observed.SerialNumber) &&
            !string.Equals(active.SerialNumber, observed.SerialNumber, StringComparison.OrdinalIgnoreCase);
        var validityExtended = active.NotAfterUtc.HasValue && observed.NotAfterUtc > active.NotAfterUtc.Value.AddDays(1);

        return thumbprintChanged || serialChanged || validityExtended;
    }

    private static EdgeCertificateIdentity ToEdgeIdentity(string normalizedHostName, CertificateMetadata metadata) =>
        new(
            metadata.Subject ?? $"CN={normalizedHostName}",
            metadata.Issuer ?? "unknown",
            metadata.SubjectAlternativeNames,
            metadata.Thumbprint ?? string.Empty,
            metadata.Fingerprint ?? string.Empty,
            metadata.SerialNumber ?? string.Empty,
            metadata.SignatureAlgorithm ?? "unknown",
            metadata.NotBeforeUtc?.UtcDateTime ?? DateTime.UtcNow,
            metadata.NotAfterUtc?.UtcDateTime ?? DateTime.UtcNow);

    private TimeSpan ComputeBackoff(int attemptCount)
    {
        var options = _renewalOptions.Value;
        var exponent = Math.Max(0, attemptCount - 1);
        var seconds = options.RetryBackoff.TotalSeconds * Math.Pow(2, Math.Min(exponent, 10));
        var delay = TimeSpan.FromSeconds(Math.Min(seconds, options.MaxRetryDelay.TotalSeconds));
        if (options.JitterPercent <= 0)
        {
            return delay;
        }

        var jitter = 1 + (RandomNumberGenerator.GetInt32(0, options.JitterPercent + 1) / 100d);
        return TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * jitter, options.MaxRetryDelay.TotalSeconds));
    }

    private DateTime? CalculateNextRenewalUtc(DateTime? notAfterUtc) =>
        notAfterUtc?.Subtract(_renewalOptions.Value.RenewalThreshold);

    private async Task<RenewalClaim> ReloadClaimAsync(
        CertificateRenewalSchedulerCandidate candidate,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var domain = await _dbContext.Domains
            .IgnoreQueryFilters()
            .Include(d => d.SslCertificates.Where(c => !c.IsDeleted))
            .Include(d => d.SslCertificateOperations.Where(o => !o.IsDeleted))
            .SingleAsync(d => d.Id == candidate.DomainId && d.TenantId == candidate.TenantId, cancellationToken);
        var active = domain.SslCertificates.Single(c => c.Id == candidate.CertificateId);
        var operation = domain.SslCertificateOperations.Single(o =>
            o.OperationType == CertificateOperationType.Renewal &&
            o.IdempotencyKey == idempotencyKey);
        var replacement = domain.SslCertificates.SingleOrDefault(c => c.OperationId == operation.Id) ?? active;
        var outcome = operation.Status switch
        {
            CertificateOperationStatus.Succeeded => "AlreadyRenewed",
            CertificateOperationStatus.Superseded => "Superseded",
            CertificateOperationStatus.Failed when !operation.Retryable => "TerminalFailure",
            CertificateOperationStatus.Failed when operation.RetryAfterUtc > DateTime.UtcNow => "RetryDeferred",
            _ => "InProgress",
        };
        return new RenewalClaim(domain, active, replacement, operation, outcome);
    }

    private async Task SupersedeExpiredOperationsAsync(
        Domain domain,
        CertificateOperationType operationType,
        DateTime now,
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

            var replacement = stale.CertificateId.HasValue
                ? domain.SslCertificates.SingleOrDefault(c => c.OperationId == stale.Id && c.Id != stale.CertificateId.Value)
                : domain.SslCertificates.SingleOrDefault(c => c.OperationId == stale.Id);
            if (replacement is not null && replacement.Status != CertificateStatus.Active)
            {
                replacement.Status = CertificateStatus.Failed;
                replacement.OperationPhase = CertificateOperationPhase.RetryBackoff;
                replacement.FailureCode = stale.FailureCode;
                replacement.SafeFailureReason = stale.SafeFailureReason;
                replacement.Retryable = false;
                replacement.UpdatedAt = now;
                if (domain.SslCertificates.SingleOrDefault(c => c.Id == stale.CertificateId) is { } active &&
                    active.ReplacementCertificateId == replacement.Id)
                    active.ReplacementCertificateId = null;
            }

            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "renewal_operation_superseded", correlationId, new
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

    private static string BuildRenewalIdempotencyKey(CertificateRenewalSchedulerCandidate candidate)
    {
        if (candidate.ExpirySeverity.StartsWith("manual:", StringComparison.Ordinal))
        {
            var manualKey = candidate.ExpirySeverity["manual:".Length..].Trim();
            return $"certificate-renewal:{candidate.TenantId:N}:{candidate.DomainId:N}:{candidate.CertificateId:N}:g{candidate.Generation}:{manualKey}";
        }

        return $"certificate-renewal:{candidate.TenantId:N}:{candidate.DomainId:N}:{candidate.CertificateId:N}:g{candidate.Generation}";
    }

    private static string HashRenewalRequest(CertificateRenewalSchedulerCandidate candidate, string idempotencyKey)
    {
        var value = $"{candidate.TenantId:N}:{candidate.DomainId:N}:{candidate.CertificateId:N}:{candidate.Generation}:{idempotencyKey}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static DomainAuditEntry CreateAudit(Domain domain, string action, string correlationId, object safeMetadata) =>
        new()
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = domain.TenantId,
            Action = action,
            ActorType = ActorType.System,
            ActorUserId = null,
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
                CertificateRenewal = safeMetadata,
            }),
            TraceId = correlationId,
            TimestampUtc = DateTime.UtcNow,
        };

    private static CertificateRenewalServiceResult MapResult(
        CertificateRenewalSchedulerCandidate candidate,
        SslCertificate active,
        SslCertificate? replacement,
        SslCertificateOperation operation,
        string outcome,
        bool retryable,
        string? failureCode,
        DateTime? retryAfterUtc) =>
        new(
            candidate.TenantId,
            candidate.DomainId,
            active.Id,
            replacement?.Id == active.Id ? null : replacement?.Id,
            operation.Id,
            candidate.ProviderType,
            replacement?.Generation ?? active.Generation,
            operation.Phase,
            outcome,
            string.Equals(outcome, "Renewed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outcome, "AlreadyRenewed", StringComparison.OrdinalIgnoreCase),
            retryable,
            failureCode,
            retryAfterUtc);

    private static string NormalizeHex(string? value) =>
        (value ?? string.Empty).Replace(":", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

    private sealed record RenewalClaim(
        Domain Domain,
        SslCertificate ActiveCertificate,
        SslCertificate ReplacementCertificate,
        SslCertificateOperation Operation,
        string? Outcome);
}
