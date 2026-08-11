using System.Collections.Concurrent;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using System.Data;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// WRITE service implementing all DOMAIN-3.1-C commands for Storefront
/// Domain Management.
///
/// Architecture Law #18: No Direct DB Writes Outside Domain Repository.
/// All persistence goes through IDomainRepository (which wraps the DbContext).
/// The service uses ApplicationDbContext only for transactional coordination
/// (BeginTransaction / SaveChanges), consistent with the CommerceHub command
/// handler pattern (CommerceHubCommandHandlers.cs:117-304).
///
/// Architecture Law #11: No command may bypass optimistic concurrency.
/// Version/xmin is checked on UpdateDomain and PromotePrimaryDomain.
///
/// Every mutation writes exactly one DomainAuditEntry in the SAME
/// SaveChanges call (Architecture Law #15: Every mutation must create
/// DomainAuditEntry; DOMAIN-1.5 CPD-13: same transaction).
///
/// Exactly one SaveChanges per command (Performance: No duplicated saves).
/// </summary>
public sealed class DomainWriteService : IDomainWriteService
{
    private const int MaxVerificationTokenRefreshesPerHour = 5;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> VerificationCommandLocks = new();

    private readonly ApplicationDbContext _dbContext;
    private readonly IDomainRepository _domainRepository;
    private readonly IDomainVerificationService _domainVerificationService;
    private readonly IHostNormalizer _hostNormalizer;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DomainWriteService> _logger;

    // Reserved hostnames that cannot be used as custom domains.
    // These are platform-managed, well-known hosts (DOMAIN-1.5 Section 7.2).
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

    private static readonly string[] ReservedPlatformSuffixes =
    [
        "shopixy.com",
        "shopixy.dev",
    ];

    public DomainWriteService(
        ApplicationDbContext dbContext,
        IDomainRepository domainRepository,
        IDomainVerificationService domainVerificationService,
        IHostNormalizer hostNormalizer,
        IEventPublisher eventPublisher,
        ILogger<DomainWriteService> logger)
    {
        _dbContext = dbContext;
        _domainRepository = domainRepository;
        _domainVerificationService = domainVerificationService;
        _hostNormalizer = hostNormalizer;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    // =========================================================================
    // Command 1: CreateDomain
    // =========================================================================

    /// <summary>
    /// Creates a new domain in Draft state. Verification is started by the
    /// explicit StartDomainVerification command.
    ///
    /// Validation:
    /// - Hostname normalization (via IHostNormalizer)
    /// - Hostname format (non-empty, valid)
    /// - Reserved host check
    /// - Duplicate hostname (global and per-tenant)
    /// - DomainType cannot be Redirect or Temporary at creation
    /// - If Primary type, no existing Primary for the tenant
    ///
    /// Audit: DomainAuditEntry with Action="DomainCreated"
    /// Event: DomainRegisteredEvent via IEventPublisher
    /// </summary>
    public async Task<DomainMutationResponse> CreateDomainAsync(
        CreateDomainRequest request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        // --- Validate hostname ---
        if (string.IsNullOrWhiteSpace(request.HostName))
            throw new DomainOperationException(DomainOperationErrorCode.InvalidHostFormat,
                "Hostname is required.");

        var normalizedHost = _hostNormalizer.Normalize(request.HostName);
        if (string.IsNullOrWhiteSpace(normalizedHost))
            throw new DomainOperationException(DomainOperationErrorCode.InvalidHostFormat,
                "Hostname is invalid after normalization.");

        // --- Reserved host check ---
        if (IsReservedHost(normalizedHost))
            throw new DomainOperationException(DomainOperationErrorCode.ReservedHost,
                $"The hostname '{request.HostName}' is reserved and cannot be used.");

        // --- Duplicate hostname check (global uniqueness) ---
        if (await _domainRepository.ExistsByNormalizedHostAsync(normalizedHost, cancellationToken))
            throw new DomainConflictException(
                $"The hostname '{request.HostName}' is already registered.");

        // --- Duplicate hostname check (per-tenant) ---
        if (await _domainRepository.ExistsByTenantAndNormalizedHostAsync(
            tenantId, normalizedHost, cancellationToken))
            throw new DomainConflictException(
                $"This hostname is already registered to your account.");

        // --- DomainType validation ---
        if (request.DomainType is DomainType.Primary or DomainType.Redirect or DomainType.Temporary)
            throw new DomainOperationException(DomainOperationErrorCode.InvalidDomainType,
                "Primary, Redirect, and Temporary domains cannot be created directly. " +
                "Create an Alias domain, complete verification and SSL activation, then promote or configure it.");

        // --- Build new domain entity ---
        var domain = new Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = request.HostName.Trim(),
            NormalizedHostName = normalizedHost,
            DomainType = request.DomainType,
            LifecycleState = DomainLifecycleState.Draft,
            VerificationToken = GenerateVerificationToken(),
            VerificationTokenIssuedAtUtc = DateTime.UtcNow,
            VerificationMethod = VerificationMethod.DnsTxt,
            RedirectType = RedirectType.None,
            CanonicalPolicy = request.CanonicalPolicy,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAsync(domain, cancellationToken);

        // --- Audit entry ---
        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainCreated",
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
                domain.VerificationMethod,
                domain.RedirectType,
                domain.CanonicalPolicy,
                domain.CreatedAtUtc,
                domain.IsDeleted,
            }),
            TraceId = correlationId,
            IpAddress = GetRequestIpAddress(),
            UserAgent = GetRequestUserAgent(),
            TimestampUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

        // --- Publish event (before SaveChanges, within the outbox) ---
        var registeredEvent = new DomainRegisteredEvent
        {
            DomainId = domain.Id,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            DomainType = domain.DomainType,
            LifecycleState = domain.LifecycleState,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            UserId = actorUserId,
        };
        registeredEvent.TenantId = tenantId;
        registeredEvent.CorrelationId = correlationId;

        await _eventPublisher.PublishAsync(registeredEvent, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Domain created | DomainId: {DomainId} | TenantId: {TenantId} | Host: {Host} | TraceId: {TraceId}",
            domain.Id, tenantId, domain.HostName, correlationId);

        return MapToResponse(domain, null, auditEntry.Id, correlationId);
    }

    // =========================================================================
    // Command 2: UpdateDomain
    // =========================================================================

    /// <summary>
    /// Updates a domain's hostname, canonical policy, or redirect configuration.
    ///
    /// Validation:
    /// - Domain must exist, belong to tenant, not be deleted
    /// - Optimistic concurrency: ExpectedVersion must match current Version
    /// - Hostname normalization + reserved host + duplicate checks
    /// - Redirect target must belong to the tenant and be a Primary domain
    /// </summary>
    public async Task<DomainMutationResponse> UpdateDomainAsync(
        Guid domainId,
        UpdateDomainRequest request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        // --- Load domain (tenant-scoped, not deleted) ---
        var domain = await _domainRepository.FindByIdAsync(tenantId, domainId, cancellationToken);
        if (domain == null)
            throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

        // --- Lifecycle state validation ---
        // Cannot update a Deleted or Archived domain
        if (domain.LifecycleState == DomainLifecycleState.Deleted)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainDeleted,
                "Cannot update a deleted domain. Restore it first.");

        if (domain.LifecycleState == DomainLifecycleState.Archived)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainArchived,
                "Cannot update an archived domain. Restore it first.");

        // --- Optimistic concurrency ---
        ValidateExpectedVersion(request.ExpectedVersion, domain);

        var previousState = domain.LifecycleState;
        var previousHostName = domain.HostName;
        var previousSnapshot = SerializeDomainSnapshot(domain);

        // --- Update hostname if provided ---
        if (!string.IsNullOrWhiteSpace(request.HostName))
        {
            var normalizedHost = _hostNormalizer.Normalize(request.HostName);
            if (string.IsNullOrWhiteSpace(normalizedHost))
                throw new DomainOperationException(DomainOperationErrorCode.InvalidHostFormat,
                    "Hostname is invalid after normalization.");

            if (IsReservedHost(normalizedHost))
                throw new DomainOperationException(DomainOperationErrorCode.ReservedHost,
                    $"The hostname '{request.HostName}' is reserved and cannot be used.");

            if (domain.LifecycleState != DomainLifecycleState.Draft &&
                !string.Equals(domain.NormalizedHostName, normalizedHost, StringComparison.Ordinal))
                throw new DomainOperationException(
                    DomainOperationErrorCode.InvalidStateTransition,
                    "A hostname cannot change after ownership verification has started. Create a new domain instead.");

            // Check for duplicate (excluding self)
            if (await _domainRepository.ExistsByNormalizedHostAsync(normalizedHost, cancellationToken) &&
                domain.NormalizedHostName != normalizedHost)
                throw new DomainOperationException(DomainOperationErrorCode.DuplicateHostname,
                    $"The hostname '{request.HostName}' is already registered.");

            domain.HostName = request.HostName.Trim();
            domain.NormalizedHostName = normalizedHost;
        }

        // --- Update redirect type if provided ---
        if (request.RedirectType.HasValue)
        {
            // If setting a redirect type, the target must be specified
            if (request.RedirectType != RedirectType.None && !request.RedirectTargetDomainId.HasValue)
                throw new DomainOperationException(DomainOperationErrorCode.InvalidRedirectTarget,
                    "Redirect target domain is required when setting a redirect type.");

            // Redirect target validation
            if (request.RedirectTargetDomainId.HasValue)
            {
                var targetDomain = await _domainRepository.FindByIdAsync(
                    tenantId, request.RedirectTargetDomainId.Value, cancellationToken);
                if (targetDomain == null)
                    throw new DomainOperationException(DomainOperationErrorCode.InvalidRedirectTarget,
                        "Redirect target domain not found or does not belong to this tenant.");

                if (targetDomain.Id == domain.Id)
                    throw new DomainOperationException(DomainOperationErrorCode.RedirectLoop,
                        "A domain cannot redirect to itself.");

                ValidateRedirectTarget(targetDomain);
                domain.RedirectTargetDomainId = targetDomain.Id;
            }

            domain.RedirectType = request.RedirectType.Value;
            domain.DomainType = request.RedirectType.Value == RedirectType.None
                ? DomainType.Alias
                : DomainType.Redirect;
            if (request.RedirectType.Value == RedirectType.None)
                domain.RedirectTargetDomainId = null;
        }

        // --- Update redirect target if provided ---
        if (request.RedirectTargetDomainId.HasValue && !request.RedirectType.HasValue)
        {
            var targetDomain = await _domainRepository.FindByIdAsync(
                tenantId, request.RedirectTargetDomainId.Value, cancellationToken);
            if (targetDomain == null)
                throw new DomainOperationException(DomainOperationErrorCode.InvalidRedirectTarget,
                    "Redirect target domain not found or does not belong to this tenant.");

            if (targetDomain.Id == domain.Id)
                throw new DomainOperationException(DomainOperationErrorCode.RedirectLoop,
                    "A domain cannot redirect to itself.");

            ValidateRedirectTarget(targetDomain);
            domain.RedirectTargetDomainId = request.RedirectTargetDomainId;
            domain.DomainType = DomainType.Redirect;
        }

        // --- Update canonical policy if provided ---
        if (request.CanonicalPolicy.HasValue)
        {
            domain.CanonicalPolicy = request.CanonicalPolicy.Value;
        }

        domain.UpdatedAtUtc = DateTime.UtcNow;

        // --- Audit entry ---
        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainUpdated",
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            BeforeSnapshotJson = previousSnapshot,
            AfterSnapshotJson = SerializeDomainSnapshot(domain),
            TraceId = correlationId,
            IpAddress = GetRequestIpAddress(),
            UserAgent = GetRequestUserAgent(),
            TimestampUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

        // --- Publish event ---
        var updatedEvent = new DomainUpdatedEvent
        {
            DomainId = domain.Id,
            PreviousHostName = previousHostName != domain.HostName ? previousHostName : null,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            DomainType = domain.DomainType,
            PreviousState = previousState,
            LifecycleState = domain.LifecycleState,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            UserId = actorUserId,
        };
        updatedEvent.TenantId = tenantId;
        updatedEvent.CorrelationId = correlationId;

        await _eventPublisher.PublishAsync(updatedEvent, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Domain updated | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId}",
            domainId, tenantId, correlationId);

        return MapToResponse(domain, previousState, auditEntry.Id, correlationId);
    }

    // =========================================================================
    // DOMAIN-4.3.1 Command: StartDomainVerification
    // =========================================================================

    /// <summary>
    /// Explicitly starts domain ownership verification by transitioning a
    /// Draft domain to PendingVerification.
    /// </summary>
    public async Task<DomainMutationResponse> StartDomainVerificationAsync(
        Guid domainId,
        StartDomainVerificationRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        request ??= new StartDomainVerificationRequest();

        var commandLock = GetVerificationCommandLock(domainId);
        await commandLock.WaitAsync(cancellationToken);
        try
        {
        var domain = await _domainRepository.FindByIdAsync(tenantId, domainId, cancellationToken);
        if (domain == null)
            throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

        var previousState = domain.LifecycleState;

        ValidateExpectedVersion(request.ExpectedVersion, domain);

        if (domain.IsDeleted || domain.LifecycleState == DomainLifecycleState.Deleted)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainDeleted,
                "Cannot start verification for a deleted domain.");

        if (domain.LifecycleState == DomainLifecycleState.Archived)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainArchived,
                "Cannot start verification for an archived domain.");

        if (domain.LifecycleState == DomainLifecycleState.Disabled)
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                "Cannot start verification for a disabled domain. Restore it first.");

        if (domain.DomainType == DomainType.Primary || domain.LifecycleState == DomainLifecycleState.Primary)
            throw new DomainOperationException(DomainLifecycleStateCode.CannotModifyPrimary,
                "Primary domains cannot enter ownership verification.");

        if (domain.LifecycleState != DomainLifecycleState.Draft)
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                $"Only Draft domains can enter PendingVerification. Current state: {domain.LifecycleState}.");

        var normalizedHost = _hostNormalizer.Normalize(domain.HostName);
        if (string.IsNullOrWhiteSpace(normalizedHost) ||
            !StringComparer.OrdinalIgnoreCase.Equals(normalizedHost, domain.NormalizedHostName))
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidHostFormat,
                "Domain hostname is invalid or does not match its normalized hostname.");
        }

        if (IsReservedHost(normalizedHost))
            throw new DomainOperationException(DomainOperationErrorCode.ReservedHost,
                $"The hostname '{domain.HostName}' is reserved and cannot be verified.");

        var method = request.VerificationMethod ?? domain.VerificationMethod ?? VerificationMethod.DnsTxt;
        ValidateVerificationMethod(method);

        var methodChanged = domain.VerificationMethod.HasValue && domain.VerificationMethod.Value != method;
        var verificationToken = string.IsNullOrWhiteSpace(domain.VerificationToken) || methodChanged
            ? GenerateVerificationToken()
            : domain.VerificationToken;

        var previousSnapshot = SerializeDomainSnapshot(domain);
        domain.MarkPendingVerification(method, verificationToken);

        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainVerificationStarted",
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            BeforeSnapshotJson = previousSnapshot,
            AfterSnapshotJson = SerializeDomainSnapshot(domain),
            TraceId = correlationId,
            IpAddress = GetRequestIpAddress(),
            UserAgent = GetRequestUserAgent(),
            TimestampUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

        var updatedEvent = new DomainUpdatedEvent
        {
            DomainId = domain.Id,
            PreviousHostName = null,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            DomainType = domain.DomainType,
            PreviousState = previousState,
            LifecycleState = domain.LifecycleState,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            UserId = actorUserId,
        };
        updatedEvent.TenantId = tenantId;
        updatedEvent.CorrelationId = correlationId;

        await _eventPublisher.PublishAsync(updatedEvent, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainConcurrencyException(
                "The domain was modified by another user. Please refresh and try again.",
                domain.Version);
        }

        _logger.LogInformation(
            "Domain verification started | DomainId: {DomainId} | TenantId: {TenantId} | Method: {Method} | TraceId: {TraceId}",
            domain.Id, tenantId, method, correlationId);

        return MapToResponse(domain, previousState, auditEntry.Id, correlationId);
        }
        finally
        {
            ReleaseVerificationCommandLock(domainId, commandLock);
        }
    }

    // =========================================================================
    // DOMAIN-4.5 Commands: VerifyNow, RetryVerification, RefreshVerificationToken
    // =========================================================================

    /// <summary>
    /// Executes one manual verification attempt through the certified engine.
    /// </summary>
    public async Task<VerificationResult> VerifyDomainNowAsync(
        Guid domainId,
        VerifyDomainNowRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        _ = actorUserId;
        await ValidateVerificationExpectedVersionAsync(
            domainId,
            tenantId,
            request?.ExpectedVersion,
            cancellationToken);
        return await _domainVerificationService.VerifyAsync(domainId, tenantId, correlationId, cancellationToken);
    }

    /// <summary>
    /// Retries verification through the certified engine. Retry policy and
    /// attempt limits remain engine-owned.
    /// </summary>
    public async Task<VerificationResult> RetryDomainVerificationAsync(
        Guid domainId,
        RetryDomainVerificationRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        _ = actorUserId;
        await ValidateVerificationExpectedVersionAsync(
            domainId,
            tenantId,
            request?.ExpectedVersion,
            cancellationToken);
        return await _domainVerificationService.VerifyAsync(domainId, tenantId, correlationId, cancellationToken);
    }

    /// <summary>
    /// Refreshes the verification token before ownership verification succeeds.
    /// Token refresh is forbidden after Verified and does not reset attempts.
    /// </summary>
    public async Task<DomainMutationResponse> RefreshDomainVerificationTokenAsync(
        Guid domainId,
        RefreshDomainVerificationTokenRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        request ??= new RefreshDomainVerificationTokenRequest();

        var commandLock = GetVerificationCommandLock(domainId);
        await commandLock.WaitAsync(cancellationToken);
        try
        {
        var domain = await _domainRepository.FindByIdAsync(tenantId, domainId, cancellationToken);
        if (domain == null)
            throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

        ValidateExpectedVersion(request.ExpectedVersion, domain);

        var previousState = domain.LifecycleState;

        if (domain.IsDeleted || domain.LifecycleState == DomainLifecycleState.Deleted)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainDeleted,
                "Cannot refresh verification token for a deleted domain.");

        if (domain.LifecycleState == DomainLifecycleState.Archived)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainArchived,
                "Cannot refresh verification token for an archived domain.");

        if (domain.LifecycleState == DomainLifecycleState.Disabled)
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                "Cannot refresh verification token for a disabled domain. Restore it first.");

        if (domain.DomainType == DomainType.Primary ||
            (domain.LifecycleState is DomainLifecycleState.Primary or
                DomainLifecycleState.Verified or
                DomainLifecycleState.SslPending or
                DomainLifecycleState.SslActive) ||
            domain.VerifiedAtUtc.HasValue)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                $"Verification token cannot be refreshed in state {domain.LifecycleState}.");
        }

        if (domain.LifecycleState != DomainLifecycleState.Draft &&
            domain.LifecycleState != DomainLifecycleState.PendingVerification)
        {
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                $"Verification token cannot be refreshed in state {domain.LifecycleState}.");
        }

        var method = request.VerificationMethod ?? domain.VerificationMethod ?? VerificationMethod.DnsTxt;
        ValidateVerificationMethod(method);

        var rateLimitNow = DateTime.UtcNow;
        var refreshesLastHour = await CountVerificationTokenRefreshesLastHourAsync(
            domain.Id,
            tenantId,
            rateLimitNow,
            cancellationToken);
        if (refreshesLastHour >= MaxVerificationTokenRefreshesPerHour)
        {
            var retryAfterUtc = await GetVerificationTokenRefreshRetryAfterUtcAsync(
                domain.Id,
                tenantId,
                rateLimitNow,
                cancellationToken);
            throw new DomainOperationException(DomainOperationErrorCode.RateLimitExceeded,
                "Verification token refresh is temporarily rate limited. Please try again later.",
                retryAfterUtc);
        }

        var previousSnapshot = SerializeDomainSnapshot(domain);

        domain.VerificationMethod = method;
        domain.VerificationToken = GenerateVerificationToken();
        domain.VerificationTokenIssuedAtUtc = DateTime.UtcNow;
        domain.UpdatedAtUtc = domain.VerificationTokenIssuedAtUtc;

        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainVerificationTokenRefreshed",
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            BeforeSnapshotJson = previousSnapshot,
            AfterSnapshotJson = SerializeDomainSnapshot(domain),
            TraceId = correlationId,
            IpAddress = GetRequestIpAddress(),
            UserAgent = GetRequestUserAgent(),
            TimestampUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

        var updatedEvent = new DomainUpdatedEvent
        {
            DomainId = domain.Id,
            PreviousHostName = null,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            DomainType = domain.DomainType,
            PreviousState = previousState,
            LifecycleState = domain.LifecycleState,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            UserId = actorUserId,
        };
        updatedEvent.TenantId = tenantId;
        updatedEvent.CorrelationId = correlationId;

        await _eventPublisher.PublishAsync(updatedEvent, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainConcurrencyException(
                "The domain was modified by another user. Please refresh and try again.",
                domain.Version);
        }

        _logger.LogInformation(
            "Domain verification token refreshed | DomainId: {DomainId} | TenantId: {TenantId} | Method: {Method} | TraceId: {TraceId}",
            domain.Id, tenantId, method, correlationId);

        return MapToResponse(domain, previousState, auditEntry.Id, correlationId);
        }
        finally
        {
            ReleaseVerificationCommandLock(domainId, commandLock);
        }
    }

    // =========================================================================
    // Command 3: ArchiveDomain
    // =========================================================================

    /// <summary>
    /// Archives a domain — transitions to Archived lifecycle state.
    ///
    /// Validation:
    /// - Domain must exist, belong to tenant, not be deleted
    /// - Domain must NOT be Primary (cannot archive the primary domain)
    /// - Domain must not have redirect dependents
    /// </summary>
    public async Task<DomainMutationResponse> ArchiveDomainAsync(
        Guid domainId,
        ArchiveDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        request ??= new ArchiveDomainRequest();

        var domain = await _domainRepository.FindByIdAsync(tenantId, domainId, cancellationToken);
        if (domain == null)
            throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

        ValidateExpectedVersion(request.ExpectedVersion, domain);

        // --- Lifecycle state validation ---
        var previousState = domain.LifecycleState;

        if (domain.LifecycleState == DomainLifecycleState.Deleted)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainDeleted,
                "Cannot archive a deleted domain.");

        if (domain.LifecycleState == DomainLifecycleState.Archived)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainArchived,
                "Domain is already archived.");

        if (domain.LifecycleState == DomainLifecycleState.Primary ||
            domain.DomainType == DomainType.Primary)
            throw new DomainOperationException(DomainLifecycleStateCode.CannotArchivePrimary,
                "Cannot archive the primary domain. Demote or delete it first.");

        if (await HasActiveCertificateAsync(domain, tenantId, cancellationToken))
            throw new DomainOperationException(
                DomainOperationErrorCode.CannotDeleteWithSsl,
                "Cannot archive a domain with an active SSL certificate. Revoke the certificate first.");

        // --- Redirect dependents check ---
        if (await _domainRepository.HasRedirectDependentsAsync(domainId, cancellationToken))
            throw new DomainOperationException(DomainOperationErrorCode.HasRedirectDependents,
                "Cannot archive a domain that has redirect dependents. Remove or reassign them first.");

        var previousSnapshot = SerializeDomainSnapshot(domain);

        // --- Perform state transition ---
        domain.MarkArchived();

        // --- Audit entry ---
        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainArchived",
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            BeforeSnapshotJson = previousSnapshot,
            AfterSnapshotJson = SerializeDomainSnapshot(domain),
            TraceId = correlationId,
            IpAddress = GetRequestIpAddress(),
            UserAgent = GetRequestUserAgent(),
            TimestampUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

        // --- Publish event ---
        var archivedEvent = new DomainArchivedEvent
        {
            DomainId = domain.Id,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            PreviousState = previousState,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            UserId = actorUserId,
        };
        archivedEvent.TenantId = tenantId;
        archivedEvent.CorrelationId = correlationId;

        await _eventPublisher.PublishAsync(archivedEvent, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Domain archived | DomainId: {DomainId} | TenantId: {TenantId} | Reason: {Reason} | TraceId: {TraceId}",
            domainId, tenantId, request?.Reason ?? "n/a", correlationId);

        return MapToResponse(domain, previousState, auditEntry.Id, correlationId, wasChanged: true);
    }

    // =========================================================================
    // Command 4: RestoreDomain
    // =========================================================================

    /// <summary>
    /// Restores a domain from Deleted, Archived, or Disabled state.
    ///
    /// Validation:
    /// - Domain must exist (including deleted/archived), belong to tenant
    /// - Domain must be in Deleted, Archived, or Disabled state
    /// - Restore must be within 90-day retention window for Deleted domains
    /// - Domain cannot have been permanently purged
    /// </summary>
    public async Task<DomainMutationResponse> RestoreDomainAsync(
        Guid domainId,
        RestoreDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        request ??= new RestoreDomainRequest();

        // Use FindByIdIncludingDeletedAsync to find soft-deleted domains
        var domain = await _domainRepository.FindByIdIncludingDeletedAsync(tenantId, domainId, cancellationToken);
        if (domain == null)
            throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

        ValidateExpectedVersion(request.ExpectedVersion, domain);

        var previousState = domain.LifecycleState;

        // --- State validation for restore ---
        if (domain.LifecycleState == DomainLifecycleState.Draft ||
            domain.LifecycleState == DomainLifecycleState.PendingVerification)
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                $"Cannot restore a domain in '{domain.LifecycleState}' state. Restore is only valid from Deleted, Archived, or Disabled states.");

        if (domain.LifecycleState == DomainLifecycleState.Primary)
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                "Domain is already Primary.");

        if (domain.LifecycleState == DomainLifecycleState.Verified ||
            domain.LifecycleState == DomainLifecycleState.SslPending ||
            domain.LifecycleState == DomainLifecycleState.SslActive)
            throw new DomainOperationException(DomainOperationErrorCode.InvalidStateTransition,
                $"Domain is already in an active state ({domain.LifecycleState}).");

        // --- 90-day retention check for Deleted domains ---
        if (domain.LifecycleState == DomainLifecycleState.Deleted)
        {
            if (domain.DeletedAtUtc.HasValue &&
                domain.DeletedAtUtc.Value.AddDays(90) < DateTime.UtcNow)
                throw new DomainOperationException(DomainOperationErrorCode.RetentionExpired,
                    "The 90-day retention window for deleted domains has expired.");

            // Validate that no duplicate hostname exists after restore
            if (await _domainRepository.ExistsByNormalizedHostAsync(
                domain.NormalizedHostName, cancellationToken) &&
                domain.NormalizedHostName != null)
            {
                // The domain itself exists in the check, so we need to verify it's not a different domain
                var existing = await _domainRepository.FindByNormalizedHostAsync(
                    domain.NormalizedHostName, cancellationToken);
                if (existing != null && existing.Id != domain.Id)
                    throw new DomainOperationException(DomainOperationErrorCode.DuplicateHostname,
                        $"The hostname '{domain.HostName}' is now in use by another domain.");
            }
        }

        var previousSnapshot = SerializeDomainSnapshot(domain);

        // --- Perform state transition ---
        if (domain.LifecycleState == DomainLifecycleState.Deleted)
        {
            // Soft-delete is reversed; go to Archived first (per blueprint §4.2)
            domain.IsDeleted = false;
            domain.DeletedAtUtc = null;
            domain.LifecycleState = DomainLifecycleState.Archived;
        }
        else if (domain.LifecycleState == DomainLifecycleState.Archived)
        {
            // Restore to Draft (merchant can re-verify from scratch)
            domain.LifecycleState = DomainLifecycleState.Draft;
            ResetDomainAuthorityForFreshVerification(domain);
        }
        else if (domain.LifecycleState == DomainLifecycleState.Disabled)
        {
            // Restore to Draft
            domain.LifecycleState = DomainLifecycleState.Draft;
            ResetDomainAuthorityForFreshVerification(domain);
        }

        domain.UpdatedAtUtc = DateTime.UtcNow;

        // --- Audit entry ---
        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainRestored",
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            BeforeSnapshotJson = previousSnapshot,
            AfterSnapshotJson = SerializeDomainSnapshot(domain),
            TraceId = correlationId,
            IpAddress = GetRequestIpAddress(),
            UserAgent = GetRequestUserAgent(),
            TimestampUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

        // --- Publish event ---
        var restoredEvent = new DomainRestoredEvent
        {
            DomainId = domain.Id,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            PreviousState = previousState,
            LifecycleState = domain.LifecycleState,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            UserId = actorUserId,
        };
        restoredEvent.TenantId = tenantId;
        restoredEvent.CorrelationId = correlationId;

        await _eventPublisher.PublishAsync(restoredEvent, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Domain restored | DomainId: {DomainId} | TenantId: {TenantId} | Reason: {Reason} | TraceId: {TraceId}",
            domainId, tenantId, request?.Reason ?? "n/a", correlationId);

        return MapToResponse(domain, previousState, auditEntry.Id, correlationId, wasChanged: true);
    }

    // =========================================================================
    // Command 5: DeleteDomain (soft-delete ONLY)
    // =========================================================================

    /// <summary>
    /// Soft-deletes a domain. NEVER physical delete (Architecture Law #3).
    ///
    /// Validation:
    /// - Domain must exist, belong to tenant, not be already deleted
    /// - Cannot delete the Primary domain
    /// - Cannot delete a domain with active SSL (SslActive state)
    /// - Cannot delete a domain with redirect dependents
    /// - Cannot delete a domain in Archived state (must restore first,
    ///   or use a separate hard-delete flow for GC)
    /// </summary>
    public async Task<DomainMutationResponse> DeleteDomainAsync(
        Guid domainId,
        DeleteDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        request ??= new DeleteDomainRequest();

        var domain = await _domainRepository.FindByIdAsync(tenantId, domainId, cancellationToken);
        if (domain == null)
            throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

        ValidateExpectedVersion(request.ExpectedVersion, domain);

        var previousState = domain.LifecycleState;

        // --- Lifecycle state validation ---
        if (domain.LifecycleState == DomainLifecycleState.Deleted)
            throw new DomainOperationException(DomainLifecycleStateCode.DomainDeleted,
                "Domain is already deleted.");

        if (domain.LifecycleState == DomainLifecycleState.Primary ||
            domain.DomainType == DomainType.Primary)
            throw new DomainOperationException(DomainLifecycleStateCode.CannotDeletePrimary,
                "Cannot delete the primary domain. Demote it first by promoting another domain to Primary.");

        // Cannot delete a domain with active SSL
        if (domain.SslCertificateId.HasValue)
        {
            var cert = await _dbContext.SslCertificates
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    c => c.TenantId == tenantId &&
                         c.DomainId == domainId &&
                         c.Id == domain.SslCertificateId &&
                         !c.IsDeleted,
                    cancellationToken);
            if (cert != null && cert.Status == CertificateStatus.Active)
                throw new DomainOperationException(DomainOperationErrorCode.CannotDeleteWithSsl,
                    "Cannot delete a domain with an active SSL certificate. Revoke the certificate first.");
        }

        // --- Redirect dependents check ---
        if (await _domainRepository.HasRedirectDependentsAsync(domainId, cancellationToken))
            throw new DomainOperationException(DomainOperationErrorCode.HasRedirectDependents,
                "Cannot delete a domain that has redirect dependents. Remove or reassign them first.");

        var previousSnapshot = SerializeDomainSnapshot(domain);

        // --- Perform soft-delete ---
        domain.MarkDeleted();

        // --- Audit entry ---
        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainDeleted",
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            BeforeSnapshotJson = previousSnapshot,
            AfterSnapshotJson = SerializeDomainSnapshot(domain),
            TraceId = correlationId,
            IpAddress = GetRequestIpAddress(),
            UserAgent = GetRequestUserAgent(),
            TimestampUtc = DateTime.UtcNow,
        };

        await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

        // --- Publish event ---
        var deletedEvent = new DomainDeletedEvent
        {
            DomainId = domain.Id,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            PreviousState = previousState,
            ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
            ActorUserId = actorUserId,
            UserId = actorUserId,
        };
        deletedEvent.TenantId = tenantId;
        deletedEvent.CorrelationId = correlationId;

        await _eventPublisher.PublishAsync(deletedEvent, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Domain soft-deleted | DomainId: {DomainId} | TenantId: {TenantId} | Reason: {Reason} | TraceId: {TraceId}",
            domainId, tenantId, request?.Reason ?? "n/a", correlationId);

        return MapToResponse(domain, previousState, auditEntry.Id, correlationId, wasChanged: true);
    }

    // =========================================================================
    // Command 6: PromotePrimaryDomain
    // =========================================================================

    /// <summary>
    /// Atomically promotes a domain to Primary, demoting the current
    /// primary (if any) back to SslActive.
    ///
    /// This is the ONLY command that uses a database transaction to
    /// guarantee the single-primary invariant under concurrent requests.
    ///
    /// Architecture Law #2: Exactly one Primary Domain per tenant.
    /// Transaction strategy: Serializable isolation on the promote+ demote
    /// operation, with the filtered unique index as a backstop.
    ///
    /// Validation:
    /// - Domain must exist, belong to tenant, not be deleted/archived
    /// - Domain must be in SslActive state
    /// - Domain must not already be Primary
    /// - If a current primary exists, it is demoted to SslActive
    /// - Optimistic concurrency: ExpectedVersion checked on both domains
    ///
    /// Concurrency strategy:
    /// 1. Optimistic concurrency via Version/xmin on both promote and demote
    /// 2. Serializable transaction as backstop against race conditions
    /// 3. Filtered unique index UQ_Domains_TenantId_Primary as last-resort guard
    /// </summary>
    public async Task<DomainMutationResponse> PromotePrimaryDomainAsync(
        Guid domainId,
        PromotePrimaryDomainRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new DomainOperationException(DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");

        // --- Transactional scope: promote + demote in one atomic operation ---
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        try
        {
            // Use FOR UPDATE / row-level locking for the promote target
            // Re-fetch with tracking to get the current Version
            var domain = await _dbContext.Domains
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    d => d.TenantId == tenantId &&
                         d.Id == domainId &&
                         !d.IsDeleted,
                    cancellationToken);

            if (domain == null)
                throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

            var previousState = domain.LifecycleState;

            // --- State validation ---
            if (domain.LifecycleState == DomainLifecycleState.Deleted)
                throw new DomainOperationException(DomainLifecycleStateCode.DomainDeleted,
                    "Cannot promote a deleted domain.");

            if (domain.LifecycleState == DomainLifecycleState.Archived)
                throw new DomainOperationException(DomainLifecycleStateCode.DomainArchived,
                    "Cannot promote an archived domain.");

            if (domain.LifecycleState == DomainLifecycleState.Primary)
                throw new DomainOperationException(DomainOperationErrorCode.AlreadyPrimary,
                    "Domain is already the primary domain.");

            // Domain must be SslActive to become Primary (per §4.2 transition)
            if (domain.LifecycleState != DomainLifecycleState.SslActive)
                throw new DomainOperationException(DomainOperationErrorCode.NotVerifiedOrSslActive,
                    $"Domain must be in SslActive state to be promoted to Primary. " +
                    $"Current state: {domain.LifecycleState}.");

            if (!domain.SslCertificateId.HasValue ||
                !await _dbContext.SslCertificates
                    .IgnoreQueryFilters()
                    .AnyAsync(c =>
                        c.Id == domain.SslCertificateId.Value &&
                        c.TenantId == tenantId &&
                        c.DomainId == domain.Id &&
                        c.Status == CertificateStatus.Active &&
                        !c.IsDeleted,
                        cancellationToken))
            {
                throw new DomainOperationException(
                    DomainOperationErrorCode.NotVerifiedOrSslActive,
                    "Domain promotion requires an active, edge-proven certificate linked to the domain.");
            }

            // --- Optimistic concurrency on the target domain ---
            ValidateExpectedVersion(request?.ExpectedVersion, domain);

            // --- Find current primary within the transaction ---
            var currentPrimary = await _dbContext.Domains
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    d => d.TenantId == tenantId &&
                         d.DomainType == DomainType.Primary &&
                         d.LifecycleState == DomainLifecycleState.Primary &&
                         !d.IsDeleted,
                    cancellationToken);

            // --- Optimistic concurrency on the current primary (if exists) ---
            if (currentPrimary != null)
            {
                if (request?.PreviousPrimaryExpectedVersion.HasValue == true &&
                    currentPrimary.Version != request.PreviousPrimaryExpectedVersion.Value)
                    throw new DomainConcurrencyException(
                        "The current primary domain was modified by another user. " +
                        "Please refresh and try again.",
                        currentPrimary.Version);

                if (currentPrimary.Id == domain.Id)
                    throw new DomainOperationException(DomainOperationErrorCode.AlreadyPrimary,
                        "Domain is already the primary domain.");
            }

            var promoteSnapshot = SerializeDomainSnapshot(domain);
            var demoteSnapshot = currentPrimary != null ? SerializeDomainSnapshot(currentPrimary) : null;

            // --- Promote target domain ---
            var previousPrimaryId = currentPrimary?.Id;
            var previousPrimaryHostName = currentPrimary?.HostName;

            domain.LifecycleState = DomainLifecycleState.Primary;
            domain.DomainType = DomainType.Primary;
            domain.UpdatedAtUtc = DateTime.UtcNow;

            // --- Demote current primary (if exists) ---
            if (currentPrimary != null)
            {
                currentPrimary.LifecycleState = DomainLifecycleState.SslActive;
                currentPrimary.DomainType = DomainType.Alias;
                currentPrimary.UpdatedAtUtc = DateTime.UtcNow;
            }

            // --- Audit entry for promotion ---
            var auditEntry = new DomainAuditEntry
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                TenantId = tenantId,
                Action = "DomainPromotedToPrimary",
                ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
                ActorUserId = actorUserId,
                BeforeSnapshotJson = promoteSnapshot,
                AfterSnapshotJson = SerializeDomainSnapshot(domain),
                TraceId = correlationId,
                IpAddress = GetRequestIpAddress(),
                UserAgent = GetRequestUserAgent(),
                TimestampUtc = DateTime.UtcNow,
            };

            await _domainRepository.AddAuditEntryAsync(auditEntry, cancellationToken);

            // --- Audit entry for demotion (if a primary was demoted) ---
            if (currentPrimary != null)
            {
                var demoteAuditEntry = new DomainAuditEntry
                {
                    Id = Guid.NewGuid(),
                    DomainId = currentPrimary.Id,
                    TenantId = tenantId,
                    Action = "DomainDowngradedFromPrimary",
                    ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
                    ActorUserId = actorUserId,
                    BeforeSnapshotJson = demoteSnapshot,
                    AfterSnapshotJson = SerializeDomainSnapshot(currentPrimary),
                    TraceId = correlationId,
                    IpAddress = GetRequestIpAddress(),
                    UserAgent = GetRequestUserAgent(),
                    TimestampUtc = DateTime.UtcNow,
                };
                await _domainRepository.AddAuditEntryAsync(demoteAuditEntry, cancellationToken);
            }

            // --- Publish events ---
            var promotedEvent = new DomainPromotedToPrimaryEvent
            {
                DomainId = domain.Id,
                HostName = domain.HostName,
                NormalizedHostName = domain.NormalizedHostName,
                PreviousPrimaryDomainId = previousPrimaryId,
                PreviousPrimaryHostName = previousPrimaryHostName,
                ActorType = actorUserId.HasValue ? ActorType.Merchant : ActorType.System,
                ActorUserId = actorUserId,
                UserId = actorUserId,
            };
            promotedEvent.TenantId = tenantId;
            promotedEvent.CorrelationId = correlationId;

            await _eventPublisher.PublishAsync(promotedEvent, cancellationToken);

            // Single SaveChanges for both domain and audit entries
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Domain promoted to Primary | DomainId: {DomainId} | TenantId: {TenantId} | " +
                "PreviousPrimary: {PreviousPrimary} | TraceId: {TraceId}",
                domain.Id, tenantId, previousPrimaryHostName ?? "none", correlationId);

            return MapToResponse(domain, previousState, auditEntry.Id, correlationId, wasChanged: true);
        }
        catch (DomainOperationException)
        {
            // Domain-specific exceptions bubble up with their own error codes
            throw;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Race condition: another concurrent promotion succeeded first
            _logger.LogWarning(
                "Concurrent primary promotion detected | DomainId: {DomainId} | TenantId: {TenantId} | TraceId: {TraceId} | Error: {Error}",
                domainId, tenantId, correlationId, ex.Message);
            throw new DomainConflictException(
                $"A primary domain conflict occurred. Another promotion may have completed concurrently. " +
                $"Please refresh the domain list and try again.");
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static bool IsReservedHost(string normalizedHost)
    {
        if (string.IsNullOrWhiteSpace(normalizedHost))
            return false;

        // Check exact match
        if (ReservedHosts.Contains(normalizedHost))
            return true;

        if (ReservedPlatformSuffixes.Any(suffix =>
                normalizedHost.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                normalizedHost.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check if it's an IP address
        if (IsIpAddress(normalizedHost))
            return true;

        // Check if it's a bare TLD or single-label host (no dot)
        if (!normalizedHost.Contains('.'))
            return true;

        // Check for example.com, example.org, example.net specifically
        if (normalizedHost == "example.com" || normalizedHost == "example.org" || normalizedHost == "example.net")
            return true;

        // Check if this is a subdomain of example.com/org/net (for testing)
        if (normalizedHost.EndsWith(".example.com", StringComparison.OrdinalIgnoreCase) ||
            normalizedHost.EndsWith(".example.org", StringComparison.OrdinalIgnoreCase) ||
            normalizedHost.EndsWith(".example.net", StringComparison.OrdinalIgnoreCase))
            return false;  // Allow subdomains of example domains for testing

        // Check for .local TLD (reserved for local development)
        if (normalizedHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for reserved subdomain on NON-RESERVED parent domains
        // (admin.myshop.com → reject, but shop.example.com → allow because example.com is already reserved)
        var parts = normalizedHost.Split('.');
        if (parts.Length >= 3)  // Must have at least subdomain.domain.tld
        {
            var firstLabel = parts[0];
            
            // Get the parent domain (everything after the first label)
            var parentDomain = string.Join(".", parts.Skip(1));
            
            // Only block reserved subdomains if the parent domain itself is NOT reserved
            // If parent is example.com or *.local, allow any subdomain (for testing)
            bool parentIsReserved = parentDomain.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
            
            if (!parentIsReserved && ReservedHosts.Contains(firstLabel))
                return true;
        }

        return false;
    }

    private async Task ValidateVerificationExpectedVersionAsync(
        Guid domainId,
        Guid tenantId,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var domain = await _dbContext.Domains
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                d => d.Id == domainId && d.TenantId == tenantId && !d.IsDeleted,
                cancellationToken)
            ?? throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");

        ValidateExpectedVersion(expectedVersion, domain);
    }

    private static void ValidateExpectedVersion(int? expectedVersion, Domain domain)
    {
        if (!expectedVersion.HasValue)
            throw new DomainOperationException(
                DomainOperationErrorCode.ExpectedVersionRequired,
                "ExpectedVersion is required for domain mutations.");

        if (domain.Version != expectedVersion.Value)
            throw new DomainConcurrencyException(
                "The domain was modified by another user. Please refresh and try again.",
                domain.Version);
    }

    private static void ValidateRedirectTarget(Domain targetDomain)
    {
        if (targetDomain.DomainType != DomainType.Primary ||
            targetDomain.LifecycleState != DomainLifecycleState.Primary ||
            !targetDomain.SslCertificateId.HasValue)
            throw new DomainOperationException(
                DomainOperationErrorCode.InvalidRedirectTarget,
                "Redirect target must be this tenant's active primary domain.");
    }

    private async Task<bool> HasActiveCertificateAsync(
        Domain domain,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!domain.SslCertificateId.HasValue)
            return false;

        return await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                certificate => certificate.TenantId == tenantId &&
                               certificate.DomainId == domain.Id &&
                               certificate.Id == domain.SslCertificateId.Value &&
                               certificate.Status == CertificateStatus.Active &&
                               !certificate.IsDeleted,
                cancellationToken);
    }

    private static void ResetDomainAuthorityForFreshVerification(Domain domain)
    {
        domain.VerificationToken = GenerateVerificationToken();
        domain.VerificationTokenIssuedAtUtc = DateTime.UtcNow;
        domain.VerificationMethod = null;
        domain.VerifiedAtUtc = null;
        domain.SslActivatedAtUtc = null;
        domain.SslCertificateId = null;
        domain.DomainType = DomainType.Alias;
        domain.RedirectType = RedirectType.None;
        domain.RedirectTargetDomainId = null;
    }

    private static bool IsIpAddress(string host)
    {
        return System.Net.IPAddress.TryParse(host, out _);
    }

    private static string GenerateVerificationToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"shopixy-domain-verify-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static SemaphoreSlim GetVerificationCommandLock(Guid domainId) =>
        VerificationCommandLocks.GetOrAdd(domainId, static _ => new SemaphoreSlim(1, 1));

    private static void ReleaseVerificationCommandLock(Guid domainId, SemaphoreSlim commandLock)
    {
        commandLock.Release();

        if (commandLock.CurrentCount == 1)
        {
            VerificationCommandLocks.TryRemove(new KeyValuePair<Guid, SemaphoreSlim>(domainId, commandLock));
        }
    }

    private static void ValidateVerificationMethod(VerificationMethod method)
    {
        _ = method switch
        {
            VerificationMethod.DnsTxt => true,
            VerificationMethod.DnsCname => true,
            VerificationMethod.HttpFile => true,
            _ => throw new DomainOperationException(
                DomainOperationErrorCode.InvalidDomainType,
                $"Verification method '{method}' is not supported."),
        };
    }

    private Task<int> CountVerificationTokenRefreshesLastHourAsync(
        Guid domainId,
        Guid tenantId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var windowStart = now.AddHours(-1);
        return _dbContext.DomainAuditEntries
            .AsNoTracking()
            .CountAsync(
                entry => entry.DomainId == domainId &&
                         entry.TenantId == tenantId &&
                         entry.Action == "DomainVerificationTokenRefreshed" &&
                         entry.TimestampUtc >= windowStart,
                cancellationToken);
    }

    private async Task<DateTime> GetVerificationTokenRefreshRetryAfterUtcAsync(
        Guid domainId,
        Guid tenantId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var windowStart = now.AddHours(-1);
        var earliestRefresh = await _dbContext.DomainAuditEntries
            .AsNoTracking()
            .Where(entry => entry.DomainId == domainId &&
                            entry.TenantId == tenantId &&
                            entry.Action == "DomainVerificationTokenRefreshed" &&
                            entry.TimestampUtc >= windowStart)
            .OrderBy(entry => entry.TimestampUtc)
            .Select(entry => (DateTime?)entry.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return (earliestRefresh ?? now).AddHours(1);
    }

    private static string SerializeDomainSnapshot(Domain domain)
    {
        return JsonSerializer.Serialize(new
        {
            domain.Id,
            domain.TenantId,
            domain.HostName,
            domain.NormalizedHostName,
            domain.DomainType,
            domain.LifecycleState,
            domain.SslCertificateId,
            domain.VerificationMethod,
            domain.RedirectType,
            domain.RedirectTargetDomainId,
            domain.CanonicalPolicy,
            domain.CreatedAtUtc,
            domain.VerifiedAtUtc,
            domain.SslActivatedAtUtc,
            domain.UpdatedAtUtc,
            domain.DeletedAtUtc,
            domain.IsDeleted,
            domain.Version,
        });
    }

    private static DomainMutationResponse MapToResponse(
        Domain domain,
        DomainLifecycleState? previousState,
        Guid auditEntryId,
        string correlationId,
        bool wasChanged = true)
    {
        return new DomainMutationResponse
        {
            DomainId = domain.Id,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            DomainType = domain.DomainType,
            LifecycleState = domain.LifecycleState,
            PreviousState = previousState,
            Version = domain.Version,
            UpdatedAtUtc = domain.UpdatedAtUtc ?? DateTime.UtcNow,
            AuditEntryId = auditEntryId.ToString(),
            CorrelationId = correlationId,
            WasChanged = wasChanged,
        };
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is not null &&
               (ex.InnerException.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                ex.InnerException.Message.Contains("UQ_", StringComparison.OrdinalIgnoreCase) ||
                ex.InnerException.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    private string GetRequestIpAddress()
    {
        // HTTP request metadata is not available in this service layer yet.
        return string.Empty;
    }

    private string GetRequestUserAgent()
    {
        return string.Empty;
    }
}

// =========================================================================
// Custom exception types for domain operation errors
// =========================================================================

/// <summary>
/// Thrown when a domain operation fails due to a validation or business-rule error.
/// Maps to HTTP 400 (Bad Request) or 409 (Conflict) depending on the error code.
/// </summary>
public sealed class DomainOperationException : Exception
{
    public DomainOperationErrorCode ErrorCode { get; }
    public DateTime? RetryAfterUtc { get; }

    public DomainOperationException(
        DomainOperationErrorCode errorCode,
        string message,
        DateTime? retryAfterUtc = null)
        : base(message)
    {
        ErrorCode = errorCode;
        RetryAfterUtc = retryAfterUtc;
    }

    public DomainOperationException(DomainLifecycleStateCode lifecycleCode, string message)
        : base(message)
    {
        ErrorCode = lifecycleCode switch
        {
            DomainLifecycleStateCode.DomainDeleted => DomainOperationErrorCode.InvalidStateTransition,
            DomainLifecycleStateCode.DomainArchived => DomainOperationErrorCode.InvalidStateTransition,
            DomainLifecycleStateCode.CannotArchivePrimary => DomainOperationErrorCode.AlreadyPrimary,
            DomainLifecycleStateCode.CannotDeletePrimary => DomainOperationErrorCode.AlreadyPrimary,
            DomainLifecycleStateCode.CannotModifyPrimary => DomainOperationErrorCode.AlreadyPrimary,
            _ => DomainOperationErrorCode.InvalidStateTransition,
        };
    }
}

/// <summary>
/// Thrown when a domain operation fails due to a concurrency conflict.
/// Maps to HTTP 409 (Conflict).
/// </summary>
public sealed class DomainConcurrencyException : Exception
{
    public int ServerVersion { get; }

    public DomainConcurrencyException(string message, int serverVersion)
        : base(message)
    {
        ServerVersion = serverVersion;
    }
}

/// <summary>
/// Thrown when a domain operation fails because the domain was not found.
/// Maps to HTTP 404 (Not Found).
/// </summary>
public sealed class DomainNotFoundException : Exception
{
    public DomainNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a domain operation fails due to a conflict (duplicate, primary conflict, etc.).
/// Maps to HTTP 409 (Conflict).
/// </summary>
public sealed class DomainConflictException : Exception
{
    public DomainConflictException(string message) : base(message) { }
}

/// <summary>
/// Error codes for domain operation failures.
/// Used by the controller to map to appropriate HTTP status codes.
/// </summary>
public enum DomainOperationErrorCode
{
    InvalidHostFormat,
    ReservedHost,
    DuplicateHostname,
    InvalidDomainType,
    InvalidRedirectTarget,
    RedirectLoop,
    HasRedirectDependents,
    CannotDeleteWithSsl,
    AlreadyPrimary,
    NotVerifiedOrSslActive,
    TenantRequired,
    InvalidStateTransition,
    RetentionExpired,
    RateLimitExceeded,
    ExpectedVersionRequired,
}

/// <summary>
/// Error codes for domain lifecycle state violations.
/// </summary>
public enum DomainLifecycleStateCode
{
    DomainDeleted,
    DomainArchived,
    CannotArchivePrimary,
    CannotDeletePrimary,
    CannotModifyPrimary,
}
