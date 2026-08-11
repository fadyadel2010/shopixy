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
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed class CertificateAdminCommandService : ICertificateAdminCommandService
{
    private const int MaxPageSize = 100;

    private readonly ApplicationDbContext _dbContext;
    private readonly ICertificateProvisioningService _provisioningService;
    private readonly ICertificateActivationService _activationService;
    private readonly ICertificateRenewalService _renewalService;
    private readonly ICertificateRevocationService _revocationService;
    private readonly ICaddyEdgeClient _edgeClient;
    private readonly IEdgeCertificateProbe _certificateProbe;
    private readonly IOptions<CertificateRenewalSchedulerOptions> _renewalOptions;
    private readonly ILogger<CertificateAdminCommandService> _logger;

    public CertificateAdminCommandService(
        ApplicationDbContext dbContext,
        ICertificateProvisioningService provisioningService,
        ICertificateActivationService activationService,
        ICertificateRenewalService renewalService,
        ICertificateRevocationService revocationService,
        ICaddyEdgeClient edgeClient,
        IEdgeCertificateProbe certificateProbe,
        IOptions<CertificateRenewalSchedulerOptions> renewalOptions,
        ILogger<CertificateAdminCommandService> logger)
    {
        _dbContext = dbContext;
        _provisioningService = provisioningService;
        _activationService = activationService;
        _renewalService = renewalService;
        _revocationService = revocationService;
        _edgeClient = edgeClient;
        _certificateProbe = certificateProbe;
        _renewalOptions = renewalOptions;
        _logger = logger;
    }

    public Task<CertificateProvisioningResponse> ProvisionAsync(
        Guid domainId,
        CertificateProvisioningRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        _provisioningService.ProvisionAsync(domainId, request, tenantId, actorUserId, correlationId, cancellationToken);

    public Task<CertificateActivationResponse> ActivateAsync(
        Guid domainId,
        CertificateActivationRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        _activationService.ActivateAsync(domainId, request, tenantId, actorUserId, correlationId, cancellationToken);

    public async Task<CertificateCommandResponse> RenewAsync(
        Guid domainId,
        CertificateRenewalCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request?.IdempotencyKey);
        var (domain, certificate) = await LoadDomainAndCertificateAsync(domainId, request?.CertificateId, tenantId, requireAuthoritative: true, cancellationToken);

        var candidate = new CertificateRenewalSchedulerCandidate(
            tenantId,
            domain.Id,
            certificate.Id,
            certificate.Provider,
            certificate.Generation,
            domain.NormalizedHostName,
            certificate.NextRenewalUtc,
            certificate.NotAfterUtc,
            $"manual:{idempotencyKey}");

        var result = await _renewalService.RenewAsync(candidate, correlationId, cancellationToken);
        await AddAdminAuditAsync(domain, "admin_certificate_renew_requested", actorUserId, correlationId, new
        {
            result.CertificateId,
            result.ReplacementCertificateId,
            result.OperationId,
            result.Generation,
            result.Outcome,
            result.FailureCode,
        }, cancellationToken);

        if (string.Equals(result.FailureCode, "RATE_LIMITED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CertificateCommandRateLimitException(
                "Certificate renewal is rate limited. Retry after the returned time.",
                result.OperationId,
                result.RetryAfterUtc);
        }

        return new CertificateCommandResponse
        {
            DomainId = result.DomainId,
            CertificateId = result.ReplacementCertificateId ?? result.CertificateId,
            OperationId = result.OperationId ?? Guid.Empty,
            OperationType = CertificateOperationType.Renewal,
            OperationStatus = result.Succeeded ? CertificateOperationStatus.Succeeded : CertificateOperationStatus.Failed,
            OperationPhase = result.Phase,
            CertificateStatus = result.Succeeded ? CertificateStatus.Active : certificate.Status,
            Generation = result.Generation,
            Outcome = result.Outcome,
            Retryable = result.Retryable,
            FailureCode = result.FailureCode,
            RetryAfterUtc = result.RetryAfterUtc,
            NextAction = result.Succeeded ? "NoActionRequired" : result.Retryable ? "RetryRenewalLater" : "ResolveRenewalFailure",
            CorrelationId = correlationId,
        };
    }

    public async Task<CertificateCommandResponse> RevokeAsync(
        Guid domainId,
        CertificateRevocationCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.CertificateId == Guid.Empty)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "CertificateId is required.");
        }

        var reasonCode = CertificateRevocationReasonCodes.Normalize(request.Reason);
        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var result = await _revocationService.RevokeAsync(
            domainId,
            new CertificateRevocationRequest(request.CertificateId, reasonCode, idempotencyKey),
            tenantId,
            actorUserId,
            correlationId,
            cancellationToken);

        var domain = await LoadDomainAsync(domainId, tenantId, cancellationToken);
        await AddAdminAuditAsync(domain, "admin_certificate_revocation_requested", actorUserId, correlationId, new
        {
            result.CertificateId,
            result.OperationId,
            result.Generation,
            result.ReasonCode,
            result.Outcome,
            result.FailureCode,
        }, cancellationToken);

        return new CertificateCommandResponse
        {
            DomainId = result.DomainId,
            CertificateId = result.CertificateId,
            OperationId = result.OperationId,
            OperationType = CertificateOperationType.Revocation,
            OperationStatus = result.OperationStatus,
            OperationPhase = result.Phase,
            CertificateStatus = result.CertificateStatus,
            Generation = result.Generation,
            Outcome = result.Outcome,
            Retryable = result.Retryable,
            FailureCode = result.FailureCode,
            RetryAfterUtc = result.RetryAfterUtc,
            NextAction = result.Succeeded ? "NoActionRequired" : result.Retryable ? "RetryRevocationLater" : "ResolveRevocationFailure",
            CorrelationId = correlationId,
        };
    }

    public async Task<CertificateCommandResponse> RetryAsync(
        Guid domainId,
        CertificateRetryCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.OperationId == Guid.Empty)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "OperationId is required.");
        }

        _ = NormalizeIdempotencyKey(request.IdempotencyKey);
        var operation = await _dbContext.SslCertificateOperations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(o =>
                o.TenantId == tenantId &&
                o.DomainId == domainId &&
                o.Id == request.OperationId &&
                !o.IsDeleted,
                cancellationToken)
            ?? throw new DomainNotFoundException("The requested certificate operation was not found.");

        if (operation.Status != CertificateOperationStatus.Failed || !operation.Retryable)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "This certificate operation is not retryable.");
        }

        if (operation.RetryAfterUtc.HasValue && operation.RetryAfterUtc.Value > DateTime.UtcNow)
        {
            throw new CertificateCommandRateLimitException(
                "This certificate operation cannot be retried yet.",
                operation.Id,
                operation.RetryAfterUtc);
        }

        await AddRetryAuditAsync(operation, tenantId, actorUserId, correlationId, cancellationToken);

        return operation.OperationType switch
        {
            CertificateOperationType.Provisioning => MapProvisioning(await _provisioningService.ProvisionAsync(
                domainId,
                new CertificateProvisioningRequest { IdempotencyKey = operation.IdempotencyKey },
                tenantId,
                actorUserId,
                correlationId,
                cancellationToken)),

            CertificateOperationType.Activation => MapActivation(await _activationService.ActivateAsync(
                domainId,
                new CertificateActivationRequest
                {
                    CertificateId = operation.CertificateId,
                    IdempotencyKey = operation.IdempotencyKey,
                },
                tenantId,
                actorUserId,
                correlationId,
                cancellationToken)),

            CertificateOperationType.Renewal => await RetryRenewalAsync(operation, tenantId, correlationId, cancellationToken),

            CertificateOperationType.Revocation => await RetryRevocationAsync(operation, domainId, tenantId, actorUserId, correlationId, cancellationToken),

            _ => throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "This operation type cannot be retried through the admin API."),
        };
    }

    public async Task<CertificateCommandResponse> RefreshAsync(
        Guid domainId,
        CertificateRefreshCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = NormalizeIdempotencyKey(request?.IdempotencyKey);
        var domain = await LoadDomainAsync(domainId, tenantId, cancellationToken);
        var certificate = await SelectRefreshCertificateAsync(domain, request?.CertificateId, cancellationToken);
        var claim = await ClaimRefreshOperationAsync(domain, certificate, idempotencyKey, correlationId, cancellationToken);
        var operation = claim.Operation;

        if (claim.Outcome is "InProgress" or "Superseded")
        {
            return MapOperation(domain.Id, certificate, operation, claim.Outcome, correlationId);
        }

        if (operation.Status == CertificateOperationStatus.Succeeded)
        {
            return MapOperation(domain.Id, certificate, operation, "Replayed", correlationId);
        }

        if (operation.Status == CertificateOperationStatus.Failed &&
            operation.RetryAfterUtc.HasValue &&
            operation.RetryAfterUtc.Value > DateTime.UtcNow)
        {
            throw new CertificateCommandRateLimitException(
                "Certificate refresh is rate limited. Retry after the returned time.",
                operation.Id,
                operation.RetryAfterUtc);
        }

        var routeState = await _edgeClient.GetDomainRouteStateAsync(new CaddyDomainRouteQuery(
            tenantId,
            domain.Id,
            domain.NormalizedHostName,
            correlationId), cancellationToken);

        if (!routeState.Exists || !routeState.MatchesDesiredState)
        {
            await PersistRefreshFailureAsync(domain, certificate, operation, "EDGE_STATE_FAILURE", "Caddy edge route is not reconciled for this domain.", true, actorUserId, correlationId, cancellationToken);
            return MapOperation(domain.Id, certificate, operation, "EdgeStateFailure", correlationId);
        }

        var probe = await _certificateProbe.ProbeAsync(new EdgeCertificateProbeRequest(
            tenantId,
            domain.Id,
            certificate.Id,
            operation.Id,
            domain.NormalizedHostName,
            certificate.Status == CertificateStatus.Active ? certificate.Thumbprint : null,
            certificate.Status == CertificateStatus.Active ? certificate.Fingerprint : null,
            certificate.Status == CertificateStatus.Active ? certificate.SerialNumber : null,
            certificate.Status == CertificateStatus.Active ? certificate.Issuer : null,
            certificate.Status == CertificateStatus.Active ? certificate.NotAfterUtc : null,
            correlationId), cancellationToken);

        if (!probe.Succeeded || probe.Certificate is null)
        {
            await PersistRefreshFailureAsync(
                domain,
                certificate,
                operation,
                probe.FailureCode ?? "SNI_PROBE_FAILED",
                probe.SafeFailureReason ?? "External HTTPS/SNI certificate proof failed.",
                probe.Retryable,
                actorUserId,
                correlationId,
                cancellationToken);
            return MapOperation(domain.Id, certificate, operation, "SniProbeFailed", correlationId);
        }

        await PersistRefreshSuccessAsync(domain, certificate, operation, probe.Certificate, actorUserId, correlationId, cancellationToken);
        return MapOperation(domain.Id, certificate, operation, "Refreshed", correlationId);
    }

    public async Task<DomainCertificateDto?> GetCertificateAsync(
        Guid domainId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted, cancellationToken);

        if (!exists)
        {
            return null;
        }

        var cert = await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DomainId == domainId && !c.IsDeleted)
            .OrderByDescending(c => c.Status == CertificateStatus.Active)
            .ThenByDescending(c => c.Generation)
            .FirstOrDefaultAsync(cancellationToken);

        if (cert is null)
        {
            return new DomainCertificateDto
            {
                DomainId = domainId,
                Status = CertificateStatus.Pending,
                OperationPhase = CertificateOperationPhase.None,
            };
        }

        var lastOperation = await _dbContext.SslCertificateOperations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.DomainId == domainId && o.CertificateId == cert.Id && !o.IsDeleted)
            .OrderByDescending(o => o.UpdatedAtUtc ?? o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return MapCertificate(cert, lastOperation);
    }

    public async Task<CertificateOperationPageDto?> GetOperationsAsync(
        Guid domainId,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted, cancellationToken);

        if (!exists)
        {
            return null;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _dbContext.SslCertificateOperations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.DomainId == domainId && !o.IsDeleted)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id);

        var total = await query.CountAsync(cancellationToken);
        var operations = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CertificateOperationPageDto
        {
            Items = operations.Select(MapOperationDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            HasNextPage = page * pageSize < total,
            HasPreviousPage = page > 1,
        };
    }

    public async Task<CertificateOperationDto?> GetOperationAsync(
        Guid domainId,
        Guid operationId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var operation = await _dbContext.SslCertificateOperations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o =>
                o.TenantId == tenantId &&
                o.DomainId == domainId &&
                o.Id == operationId &&
                !o.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        return operation is null ? null : MapOperationDto(operation);
    }

    private async Task<CertificateCommandResponse> RetryRenewalAsync(
        SslCertificateOperation operation,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var certificate = await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == operation.CertificateId && !c.IsDeleted, cancellationToken)
            ?? throw new DomainNotFoundException("The requested certificate was not found.");

        var candidate = new CertificateRenewalSchedulerCandidate(
            tenantId,
            operation.DomainId,
            certificate.Id,
            certificate.Provider,
            certificate.Generation,
            operation.NormalizedHostName,
            certificate.NextRenewalUtc,
            certificate.NotAfterUtc,
            operation.IdempotencyKey.StartsWith("certificate-renewal:", StringComparison.Ordinal)
                ? "retry"
                : $"manual:{operation.IdempotencyKey}");

        var result = await _renewalService.RenewAsync(candidate, correlationId, cancellationToken);
        return new CertificateCommandResponse
        {
            DomainId = result.DomainId,
            CertificateId = result.ReplacementCertificateId ?? result.CertificateId,
            OperationId = result.OperationId ?? operation.Id,
            OperationType = CertificateOperationType.Renewal,
            OperationStatus = result.Succeeded ? CertificateOperationStatus.Succeeded : CertificateOperationStatus.Failed,
            OperationPhase = result.Phase,
            CertificateStatus = result.Succeeded ? CertificateStatus.Active : certificate.Status,
            Generation = result.Generation,
            Outcome = result.Outcome,
            Retryable = result.Retryable,
            FailureCode = result.FailureCode,
            RetryAfterUtc = result.RetryAfterUtc,
            NextAction = result.Succeeded ? "NoActionRequired" : result.Retryable ? "RetryRenewalLater" : "ResolveRenewalFailure",
            CorrelationId = correlationId,
        };
    }

    private async Task<CertificateCommandResponse> RetryRevocationAsync(
        SslCertificateOperation operation,
        Guid domainId,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var certificate = await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == operation.CertificateId && c.DomainId == domainId && !c.IsDeleted, cancellationToken)
            ?? throw new DomainNotFoundException("The requested certificate was not found.");

        var reason = certificate.RevocationReason ?? CertificateRevocationReasonCodes.AdminRequested;
        var result = await _revocationService.RevokeAsync(
            domainId,
            new CertificateRevocationRequest(certificate.Id, reason, operation.IdempotencyKey),
            tenantId,
            actorUserId,
            correlationId,
            cancellationToken);

        return new CertificateCommandResponse
        {
            DomainId = result.DomainId,
            CertificateId = result.CertificateId,
            OperationId = result.OperationId,
            OperationType = CertificateOperationType.Revocation,
            OperationStatus = result.OperationStatus,
            OperationPhase = result.Phase,
            CertificateStatus = result.CertificateStatus,
            Generation = result.Generation,
            Outcome = result.Outcome,
            Retryable = result.Retryable,
            FailureCode = result.FailureCode,
            RetryAfterUtc = result.RetryAfterUtc,
            NextAction = result.Succeeded ? "NoActionRequired" : result.Retryable ? "RetryRevocationLater" : "ResolveRevocationFailure",
            CorrelationId = correlationId,
        };
    }

    private async Task<(Domain Domain, SslCertificate Certificate)> LoadDomainAndCertificateAsync(
        Guid domainId,
        Guid? certificateId,
        Guid tenantId,
        bool requireAuthoritative,
        CancellationToken cancellationToken)
    {
        var domain = await _dbContext.Domains
            .IgnoreQueryFilters()
            .Include(d => d.SslCertificates.Where(c => !c.IsDeleted))
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted, cancellationToken)
            ?? throw new DomainNotFoundException("The requested domain was not found.");

        var certificate = certificateId.HasValue
            ? domain.SslCertificates.SingleOrDefault(c => c.Id == certificateId.Value && c.TenantId == tenantId && c.DomainId == domain.Id)
            : domain.SslCertificateId.HasValue
                ? domain.SslCertificates.SingleOrDefault(c => c.Id == domain.SslCertificateId.Value && c.Status == CertificateStatus.Active)
                : domain.SslCertificates
                    .Where(c => c.Status is CertificateStatus.Active or CertificateStatus.Expired)
                    .OrderByDescending(c => c.Generation)
                    .FirstOrDefault();

        if (certificate is null)
        {
            throw new DomainNotFoundException("The requested certificate was not found.");
        }

        if (requireAuthoritative &&
            certificate.Status == CertificateStatus.Active &&
            domain.SslCertificateId != certificate.Id)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "Only the authoritative certificate can be renewed.");
        }

        return (domain, certificate);
    }

    private async Task<Domain> LoadDomainAsync(Guid domainId, Guid tenantId, CancellationToken cancellationToken) =>
        await _dbContext.Domains
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted, cancellationToken)
        ?? throw new DomainNotFoundException("The requested domain was not found.");

    private async Task<SslCertificate> SelectRefreshCertificateAsync(
        Domain domain,
        Guid? certificateId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == domain.TenantId && c.DomainId == domain.Id && !c.IsDeleted);

        if (certificateId.HasValue)
        {
            return await query.SingleOrDefaultAsync(c => c.Id == certificateId.Value, cancellationToken)
                ?? throw new DomainNotFoundException("The requested certificate was not found.");
        }

        return await query
            .OrderByDescending(c => domain.SslCertificateId == c.Id)
            .ThenByDescending(c => c.Generation)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "No certificate is available to refresh.");
    }

    private async Task<RefreshClaim> ClaimRefreshOperationAsync(
        Domain domain,
        SslCertificate certificate,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var requestHash = HashRefreshRequest(domain.TenantId, domain.Id, certificate.Id, certificate.Generation, idempotencyKey);
        var now = DateTime.UtcNow;

        try
        {
            var existing = await _dbContext.SslCertificateOperations
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(o =>
                    o.TenantId == domain.TenantId &&
                    o.DomainId == domain.Id &&
                    o.OperationType == CertificateOperationType.MetadataSync &&
                    o.IdempotencyKey == idempotencyKey &&
                    !o.IsDeleted,
                    cancellationToken);

            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new DomainConflictException("Idempotency key was already used for a different certificate refresh request.");
                }

                if (existing.Status == CertificateOperationStatus.Succeeded)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RefreshClaim(existing, "Replayed");
                }

                if (existing.Status == CertificateOperationStatus.Superseded)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RefreshClaim(existing, "Superseded");
                }

                if (existing.Status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running &&
                    (!existing.LeaseExpiresAtUtc.HasValue || existing.LeaseExpiresAtUtc.Value > now))
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RefreshClaim(existing, "InProgress");
                }

                if (existing.Status == CertificateOperationStatus.Failed && existing.RetryAfterUtc > now)
                {
                    await CommitIfPresentAsync(transaction, cancellationToken);
                    return new RefreshClaim(existing, "RetryDeferred");
                }

                domain.CertificateOperationFencingToken += 1;
                certificate.FencingToken = domain.CertificateOperationFencingToken;
                existing.Status = CertificateOperationStatus.Running;
                existing.Phase = CertificateOperationPhase.MetadataSync;
                existing.FencingToken = domain.CertificateOperationFencingToken;
                existing.LockOwnerReference = Guid.NewGuid();
                existing.LeaseExpiresAtUtc = now.Add(_renewalOptions.Value.StaleOperationTimeout);
                existing.AttemptCount += 1;
                existing.RetryAfterUtc = null;
                existing.CompletedAtUtc = null;
                existing.UpdatedAtUtc = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await CommitIfPresentAsync(transaction, cancellationToken);
                return new RefreshClaim(existing, null);
            }

            await SupersedeExpiredRefreshOperationsAsync(
                domain,
                now,
                correlationId,
                cancellationToken);

            var inProgress = await _dbContext.SslCertificateOperations
                .IgnoreQueryFilters()
                .AnyAsync(o =>
                    o.TenantId == domain.TenantId &&
                    o.DomainId == domain.Id &&
                    o.OperationType == CertificateOperationType.MetadataSync &&
                    (o.Status == CertificateOperationStatus.Pending ||
                     o.Status == CertificateOperationStatus.Running) &&
                    !o.IsDeleted,
                    cancellationToken);

            if (inProgress)
            {
                throw new DomainConflictException("A certificate refresh operation is already in progress for this domain.");
            }

            domain.CertificateOperationFencingToken += 1;
            certificate.FencingToken = domain.CertificateOperationFencingToken;
            var operation = new SslCertificateOperation
            {
                Id = Guid.NewGuid(),
                TenantId = domain.TenantId,
                DomainId = domain.Id,
                CertificateId = certificate.Id,
                NormalizedHostName = domain.NormalizedHostName,
                OperationType = CertificateOperationType.MetadataSync,
                Status = CertificateOperationStatus.Running,
                Phase = CertificateOperationPhase.MetadataSync,
                OperationId = Guid.NewGuid(),
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash,
                EdgeCertificateReference = certificate.EdgeCertificateReference,
                FencingToken = domain.CertificateOperationFencingToken,
                LockOwnerReference = Guid.NewGuid(),
                LeaseExpiresAtUtc = now.Add(_renewalOptions.Value.StaleOperationTimeout),
                AttemptCount = 1,
                Retryable = false,
                CorrelationId = correlationId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsDeleted = false,
            };

            operation.OperationId = operation.Id;
            operation.LockOwnerReference = operation.Id;
            _dbContext.SslCertificateOperations.Add(operation);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfPresentAsync(transaction, cancellationToken);
            return new RefreshClaim(operation, null);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await RollbackIfPresentAsync(transaction, cancellationToken);
            var operation = await _dbContext.SslCertificateOperations
                .IgnoreQueryFilters()
                .SingleAsync(o =>
                    o.TenantId == domain.TenantId &&
                    o.DomainId == domain.Id &&
                    o.OperationType == CertificateOperationType.MetadataSync &&
                    o.IdempotencyKey == idempotencyKey &&
                    !o.IsDeleted,
                    cancellationToken);
            return new RefreshClaim(operation, "InProgress");
        }
    }

    private async Task PersistRefreshSuccessAsync(
        Domain domain,
        SslCertificate certificate,
        SslCertificateOperation operation,
        EdgeCertificateIdentity identity,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        certificate.LastEdgeProbeAtUtc = now;
        certificate.LastObservedThumbprint = identity.Thumbprint;
        certificate.ObservedGeneration = certificate.Generation;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Succeeded;
        operation.Phase = CertificateOperationPhase.MetadataSync;
        operation.Retryable = false;
        operation.FailureCode = null;
        operation.SafeFailureReason = null;
        operation.RetryAfterUtc = null;
        operation.CompletedAtUtc = now;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = null;
        operation.LockOwnerReference = null;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "admin_certificate_refresh_requested", actorUserId, correlationId, new
        {
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            Outcome = "refreshed",
            identity.Thumbprint,
            identity.SerialNumber,
        }));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistRefreshFailureAsync(
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
        certificate.LastFailureAtUtc = now;
        certificate.FailureCode = failureCode;
        certificate.SafeFailureReason = safeFailureReason;
        certificate.Retryable = retryable;
        certificate.RetryAfterUtc = retryable ? now.Add(_renewalOptions.Value.RetryBackoff) : null;
        certificate.UpdatedAt = now;

        operation.Status = CertificateOperationStatus.Failed;
        operation.Phase = retryable ? CertificateOperationPhase.RetryBackoff : CertificateOperationPhase.MetadataSync;
        operation.FailureCode = failureCode;
        operation.SafeFailureReason = safeFailureReason;
        operation.Retryable = retryable;
        operation.RetryAfterUtc = certificate.RetryAfterUtc;
        operation.CompletedAtUtc = retryable ? null : now;
        operation.UpdatedAtUtc = now;
        operation.LeaseExpiresAtUtc = null;
        operation.LockOwnerReference = null;

        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "admin_certificate_refresh_requested", actorUserId, correlationId, new
        {
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            certificate.Generation,
            Outcome = "failed",
            failureCode,
            retryable,
        }));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AddRetryAuditAsync(
        SslCertificateOperation operation,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var domain = await LoadDomainAsync(operation.DomainId, tenantId, cancellationToken);
        await AddAdminAuditAsync(domain, "admin_certificate_retry_requested", actorUserId, correlationId, new
        {
            OperationId = operation.Id,
            operation.CertificateId,
            operation.OperationType,
            operation.FailureCode,
            operation.Retryable,
        }, cancellationToken);
    }

    private async Task AddAdminAuditAsync(
        Domain domain,
        string action,
        Guid? actorUserId,
        string correlationId,
        object safeMetadata,
        CancellationToken cancellationToken)
    {
        _dbContext.DomainAuditEntries.Add(CreateAudit(domain, action, actorUserId, correlationId, safeMetadata));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DomainCertificateDto MapCertificate(SslCertificate cert, SslCertificateOperation? lastOperation) =>
        new()
        {
            DomainId = cert.DomainId,
            CertificateId = cert.Id,
            Generation = cert.Generation,
            Provider = cert.Provider,
            Status = cert.Status,
            OperationPhase = cert.OperationPhase,
            Issuer = cert.Issuer,
            SerialNumber = cert.SerialNumber,
            Thumbprint = cert.Thumbprint,
            Fingerprint = cert.Fingerprint,
            NotBeforeUtc = cert.NotBeforeUtc,
            NotAfterUtc = cert.NotAfterUtc,
            ActivatedAtUtc = cert.ActivatedAtUtc,
            NextRenewalUtc = cert.NextRenewalUtc,
            RenewalEligible = cert.RenewalEligible,
            Retryable = cert.Retryable,
            FailureCode = cert.FailureCode,
            SafeFailureReason = cert.SafeFailureReason,
            LastOperationId = lastOperation?.Id,
            LastOperationStatus = lastOperation?.Status,
            LastOperationPhase = lastOperation?.Phase,
            LastOperationUpdatedAtUtc = lastOperation?.UpdatedAtUtc ?? lastOperation?.CreatedAtUtc,
            Degraded = cert.Status is CertificateStatus.Failed or CertificateStatus.Expired || cert.Retryable,
        };

    private static CertificateOperationDto MapOperationDto(SslCertificateOperation operation) =>
        new()
        {
            OperationId = operation.Id,
            DomainId = operation.DomainId,
            CertificateId = operation.CertificateId,
            OperationType = operation.OperationType,
            Status = operation.Status,
            Phase = operation.Phase,
            Retryable = operation.Retryable,
            FailureCode = operation.FailureCode,
            SafeFailureReason = operation.SafeFailureReason,
            RetryAfterUtc = operation.RetryAfterUtc,
            AttemptCount = operation.AttemptCount,
            CreatedAtUtc = operation.CreatedAtUtc,
            UpdatedAtUtc = operation.UpdatedAtUtc,
            CompletedAtUtc = operation.CompletedAtUtc,
            CorrelationId = operation.CorrelationId,
        };

    private static CertificateCommandResponse MapProvisioning(CertificateProvisioningResponse response) =>
        new()
        {
            DomainId = response.DomainId,
            CertificateId = response.CertificateId,
            OperationId = response.OperationId,
            OperationType = response.OperationType,
            OperationStatus = response.OperationStatus,
            OperationPhase = response.OperationPhase,
            CertificateStatus = response.CertificateStatus,
            Generation = response.Generation,
            Outcome = response.Outcome,
            Retryable = response.Retryable,
            FailureCode = response.FailureCode,
            SafeFailureReason = response.SafeFailureReason,
            NextAction = response.NextAction,
            CorrelationId = response.CorrelationId,
        };

    private static CertificateCommandResponse MapActivation(CertificateActivationResponse response) =>
        new()
        {
            DomainId = response.DomainId,
            CertificateId = response.CertificateId,
            OperationId = response.OperationId,
            OperationType = response.OperationType,
            OperationStatus = response.OperationStatus,
            OperationPhase = response.OperationPhase,
            CertificateStatus = response.CertificateStatus,
            Generation = response.Generation,
            Outcome = response.Outcome,
            Retryable = response.Retryable,
            FailureCode = response.FailureCode,
            SafeFailureReason = response.SafeFailureReason,
            NextAction = response.NextAction,
            CorrelationId = response.CorrelationId,
        };

    private static CertificateCommandResponse MapOperation(
        Guid domainId,
        SslCertificate certificate,
        SslCertificateOperation operation,
        string outcome,
        string correlationId) =>
        new()
        {
            DomainId = domainId,
            CertificateId = certificate.Id,
            OperationId = operation.Id,
            OperationType = operation.OperationType,
            OperationStatus = operation.Status,
            OperationPhase = operation.Phase,
            CertificateStatus = certificate.Status,
            Generation = certificate.Generation,
            Outcome = outcome,
            Retryable = operation.Retryable,
            FailureCode = operation.FailureCode,
            SafeFailureReason = operation.SafeFailureReason,
            RetryAfterUtc = operation.RetryAfterUtc,
            NextAction = operation.Status == CertificateOperationStatus.Succeeded
                ? "NoActionRequired"
                : operation.Retryable ? "RetryRefreshLater" : "ResolveRefreshFailure",
            CorrelationId = correlationId,
        };

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
                CertificateAdminCommand = safeMetadata,
            }),
            TraceId = correlationId,
            TimestampUtc = DateTime.UtcNow,
        };

    private static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition, "A valid idempotency key is required.");
        }

        return idempotencyKey.Trim();
    }

    private async Task SupersedeExpiredRefreshOperationsAsync(
        Domain domain,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var staleOperations = await _dbContext.SslCertificateOperations
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == domain.TenantId &&
                        o.DomainId == domain.Id &&
                        o.OperationType == CertificateOperationType.MetadataSync &&
                        (o.Status == CertificateOperationStatus.Pending || o.Status == CertificateOperationStatus.Running) &&
                        o.LeaseExpiresAtUtc.HasValue &&
                        o.LeaseExpiresAtUtc.Value <= now &&
                        !o.IsDeleted)
            .ToListAsync(cancellationToken);

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
            _dbContext.DomainAuditEntries.Add(CreateAudit(domain, "admin_certificate_refresh_superseded", null, correlationId, new
            {
                OperationId = stale.Id,
                stale.CertificateId,
                stale.FencingToken,
            }));
        }

        if (staleOperations.Count > 0)
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

    private sealed record RefreshClaim(SslCertificateOperation Operation, string? Outcome);

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("UQ_", StringComparison.OrdinalIgnoreCase) == true;

    private static string HashRefreshRequest(Guid tenantId, Guid domainId, Guid certificateId, int generation, string idempotencyKey)
    {
        var value = $"{tenantId:N}|{domainId:N}|{certificateId:N}|{generation}|refresh|{idempotencyKey}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
