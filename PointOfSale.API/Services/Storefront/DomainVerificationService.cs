using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Constitutional Domain Verification Engine (DOMAIN-4.3).
///
/// Executes exactly one verification attempt and encapsulates verification
/// business rules: tenant ownership, lifecycle validation, resolver invocation,
/// token comparison, state transition, audit, attempt records, event publication,
/// failure classification, and retry eligibility.
///
/// This service consumes only IDnsResolver for DNS/HTTP lookups. It never
/// references provider-specific DNS libraries.
/// </summary>
public sealed class DomainVerificationService : IDomainVerificationService
{
    private const int TokenValidityDays = 90;
    private const int MaxFailedAttemptsPerHour = 5;
    private static readonly TimeSpan StaleVerificationClaimTimeout = TimeSpan.FromMinutes(5);
    private const string VerificationHostPrefix = "_shopixy-verification.";
    private const string HttpVerificationPath = "/.well-known/shopixy-domain-verification.txt";

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> VerificationLocks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;
    private readonly IDnsResolver _dnsResolver;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DomainVerificationService> _logger;

    public DomainVerificationService(
        ApplicationDbContext dbContext,
        IDnsResolver dnsResolver,
        IEventPublisher eventPublisher,
        ILogger<DomainVerificationService> logger)
    {
        _dbContext = dbContext;
        _dnsResolver = dnsResolver;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<VerificationResult> VerifyAsync(
        Guid domainId,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (tenantId == Guid.Empty)
        {
            throw new DomainOperationException(
                DomainOperationErrorCode.TenantRequired,
                "Tenant ID is required.");
        }

        var verificationLock = VerificationLocks.GetOrAdd(domainId, static _ => new SemaphoreSlim(1, 1));
        await verificationLock.WaitAsync(cancellationToken);

        try
        {
        correlationId = NormalizeCorrelationId(correlationId);
        var now = DateTime.UtcNow;

        var domain = await _dbContext.Domains
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                d => d.Id == domainId &&
                     d.TenantId == tenantId,
                cancellationToken);

        if (domain is null)
        {
            throw new DomainNotFoundException($"Domain '{domainId}' not found for tenant '{tenantId}'.");
        }

        var requiresReverification = RequiresReverification(domain, now);
        if (domain.VerifiedAtUtc.HasValue && !requiresReverification &&
            domain.LifecycleState is DomainLifecycleState.Verified or DomainLifecycleState.SslPending or DomainLifecycleState.SslActive or DomainLifecycleState.Primary)
        {
            return await MapSuccessfulReplayAsync(domain, tenantId, correlationId, cancellationToken);
        }

        if (requiresReverification)
        {
            await PrepareReverificationAsync(domain, tenantId, correlationId, now, cancellationToken);
        }

        var attemptNumber = await GetNextAttemptNumberAsync(domain.Id, cancellationToken);

        var validationFailure = ValidateVerificationEligibility(domain, now, requiresReverification);
        var failedAttemptsLastHour = await CountFailedAttemptsLastHourAsync(
            domain.Id,
            now,
            cancellationToken);
        AttemptEvaluation evaluation;
        DomainVerification? verificationClaim = null;

        if (validationFailure is not null)
        {
            evaluation = AttemptEvaluation.Failure(
                validationFailure,
                resolvedValue: null,
                expectedValue: domain.VerificationToken,
                ttl: null,
                resolverEndpoint: "validation",
                latency: TimeSpan.Zero,
                resolvedAtUtc: now);
        }
        else if (failedAttemptsLastHour >= MaxFailedAttemptsPerHour)
        {
            var retryAfterUtc = await GetFailedAttemptRateLimitRetryAfterUtcAsync(
                domain.Id,
                now,
                cancellationToken);

            _logger.LogWarning(
                "Domain verification rate limited before resolver call | DomainId: {DomainId} | TenantId: {TenantId} | Attempt: {Attempt} | CorrelationId: {CorrelationId}",
                domain.Id,
                tenantId,
                attemptNumber,
                correlationId);

            var rateLimitAuditEntry = new DomainAuditEntry
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                TenantId = tenantId,
                Action = "DomainVerificationRateLimited",
                ActorType = ActorType.System,
                ActorUserId = null,
                BeforeSnapshotJson = SerializeDomainSnapshot(domain),
                AfterSnapshotJson = JsonSerializer.Serialize(new
                {
                    domain.Id,
                    domain.TenantId,
                    domain.LifecycleState,
                    Outcome = DomainVerificationFailureReasons.RateLimited,
                    FailedAttemptsLastHour = failedAttemptsLastHour,
                    RetryAfterUtc = retryAfterUtc,
                }, JsonOptions),
                TraceId = correlationId,
                IpAddress = null,
                UserAgent = null,
                TimestampUtc = now,
            };
            await _dbContext.DomainAuditEntries.AddAsync(rateLimitAuditEntry, cancellationToken);

            var rateLimitedEvent = new DomainVerificationFailedEvent
            {
                DomainId = domain.Id,
                HostName = domain.HostName,
                NormalizedHostName = domain.NormalizedHostName,
                VerificationMethod = domain.VerificationMethod ?? VerificationMethod.DnsTxt,
                FailureReason = DomainVerificationFailureReasons.RateLimited,
                Attempt = attemptNumber,
                RetryEligible = false,
            };
            rateLimitedEvent.TenantId = tenantId;
            rateLimitedEvent.CorrelationId = correlationId;
            await _eventPublisher.PublishAsync(rateLimitedEvent, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new VerificationResult
            {
                DomainId = domain.Id,
                TenantId = tenantId,
                LifecycleState = domain.LifecycleState,
                Method = domain.VerificationMethod ?? VerificationMethod.DnsTxt,
                Status = VerificationStatus.Failed,
                FailureReason = DomainVerificationFailureReasons.RateLimited,
                ResolvedValue = null,
                ExpectedValue = string.Empty,
                Ttl = null,
                ResolverEndpoint = "rate-limit",
                Latency = TimeSpan.Zero,
                AttemptNumber = attemptNumber,
                ResolvedAtUtc = now,
                RetryEligible = false,
                RetryAfterUtc = retryAfterUtc,
                CorrelationId = correlationId,
            };
        }
        else
        {
            verificationClaim = await TryClaimResolverAttemptAsync(
                domain,
                attemptNumber,
                correlationId,
                now,
                cancellationToken);
            if (verificationClaim is null)
            {
                return new VerificationResult
                {
                    DomainId = domain.Id,
                    TenantId = tenantId,
                    LifecycleState = domain.LifecycleState,
                    Method = domain.VerificationMethod ?? VerificationMethod.DnsTxt,
                    Status = VerificationStatus.Pending,
                    FailureReason = DomainVerificationFailureReasons.InProgress,
                    ResolvedValue = null,
                    ExpectedValue = string.Empty,
                    Ttl = null,
                    ResolverEndpoint = "durable-claim",
                    Latency = TimeSpan.Zero,
                    AttemptNumber = attemptNumber,
                    ResolvedAtUtc = now,
                    RetryEligible = true,
                    CorrelationId = correlationId,
                };
            }

            evaluation = await ExecuteResolverAttemptAsync(domain, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var previousSnapshot = SerializeDomainSnapshot(domain);

        if (evaluation.Status == VerificationStatus.Failed)
        {
            failedAttemptsLastHour++;
        }

        var retryEligible = IsRetryEligible(evaluation.FailureReason, failedAttemptsLastHour);
        var method = domain.VerificationMethod ?? VerificationMethod.DnsTxt;
        var verificationTimestamp = DateTime.UtcNow;

        var verification = verificationClaim ?? new DomainVerification
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Method = method,
            Token = domain.VerificationToken,
            Attempt = attemptNumber,
            CreatedAt = verificationTimestamp,
            IsDeleted = false,
        };
        verification.Status = evaluation.Status;
        verification.AttemptedAtUtc = verificationTimestamp;
        verification.VerifiedAtUtc = evaluation.Status == VerificationStatus.Success ? verificationTimestamp : null;
        verification.FailureReason = evaluation.FailureReason;
        verification.CorrelationId = correlationId;
        verification.ResolverEndpoint = evaluation.ResolverEndpoint;
        verification.LatencyMilliseconds = (int)Math.Min(int.MaxValue, Math.Max(0, evaluation.Latency.TotalMilliseconds));
        verification.Ttl = evaluation.Ttl;
        verification.ResolvedValue = Truncate(evaluation.ResolvedValue, 1024);
        verification.UpdatedAt = verificationTimestamp;

        if (verificationClaim is null)
            await _dbContext.DomainVerifications.AddAsync(verification, cancellationToken);

        if (evaluation.Status == VerificationStatus.Success)
        {
            if (requiresReverification)
            {
                domain.VerifiedAtUtc = verificationTimestamp;
                domain.UpdatedAtUtc = verificationTimestamp;
            }
            else
            {
                domain.MarkVerified();
            }
        }

        var afterSnapshot = SerializeVerificationAuditSnapshot(
            domain,
            verification,
            evaluation,
            retryEligible,
            correlationId);

        var auditEntry = new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = evaluation.Status == VerificationStatus.Success
                ? requiresReverification ? "DomainReverified" : "DomainVerified"
                : requiresReverification ? "DomainReverificationFailed" : "DomainVerificationFailed",
            ActorType = ActorType.System,
            ActorUserId = null,
            BeforeSnapshotJson = previousSnapshot,
            AfterSnapshotJson = afterSnapshot,
            TraceId = correlationId,
            IpAddress = null,
            UserAgent = null,
            TimestampUtc = verificationTimestamp,
        };

        await _dbContext.DomainAuditEntries.AddAsync(auditEntry, cancellationToken);

        IntegrationEvent eventToPublish;

        if (evaluation.Status == VerificationStatus.Success)
        {
            var verifiedEvent = new DomainVerifiedEvent
            {
                DomainId = domain.Id,
                HostName = domain.HostName,
                NormalizedHostName = domain.NormalizedHostName,
                VerifiedAtUtc = domain.VerifiedAtUtc ?? verificationTimestamp,
                VerificationMethod = method,
            };
            verifiedEvent.TenantId = tenantId;
            verifiedEvent.CorrelationId = correlationId;

            eventToPublish = verifiedEvent;
        }
        else
        {
            var failedEvent = new DomainVerificationFailedEvent
            {
                DomainId = domain.Id,
                HostName = domain.HostName,
                NormalizedHostName = domain.NormalizedHostName,
                VerificationMethod = method,
                FailureReason = evaluation.FailureReason ?? DomainVerificationFailureReasons.ResolverFailure,
                Attempt = attemptNumber,
                RetryEligible = retryEligible,
            };
            failedEvent.TenantId = tenantId;
            failedEvent.CorrelationId = correlationId;

            eventToPublish = failedEvent;
        }

        await _eventPublisher.PublishAsync(eventToPublish, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new VerificationResult
            {
                DomainId = domain.Id,
                TenantId = tenantId,
                LifecycleState = domain.LifecycleState,
                Method = method,
                Status = VerificationStatus.Failed,
                FailureReason = DomainVerificationFailureReasons.LifecycleViolation,
                ResolvedValue = null,
                ExpectedValue = evaluation.ExpectedValue,
                Ttl = null,
                ResolverEndpoint = "concurrency",
                Latency = TimeSpan.Zero,
                AttemptNumber = attemptNumber,
                ResolvedAtUtc = DateTime.UtcNow,
                RetryEligible = false,
                CorrelationId = correlationId,
            };
        }

        _logger.LogInformation(
            "Domain verification attempt completed | DomainId: {DomainId} | TenantId: {TenantId} | Method: {Method} | Status: {Status} | FailureReason: {FailureReason} | Attempt: {Attempt} | TraceId: {TraceId}",
            domain.Id,
            tenantId,
            method,
            evaluation.Status,
            evaluation.FailureReason ?? "none",
            attemptNumber,
            correlationId);

        return new VerificationResult
        {
            DomainId = domain.Id,
            TenantId = tenantId,
            LifecycleState = domain.LifecycleState,
            Method = method,
            Status = evaluation.Status,
            FailureReason = evaluation.FailureReason,
            ResolvedValue = evaluation.ResolvedValue,
            ExpectedValue = evaluation.ExpectedValue,
            Ttl = evaluation.Ttl,
            ResolverEndpoint = evaluation.ResolverEndpoint,
            Latency = evaluation.Latency,
            AttemptNumber = attemptNumber,
            ResolvedAtUtc = evaluation.ResolvedAtUtc,
            RetryEligible = retryEligible,
            CorrelationId = correlationId,
        };
        }
        finally
        {
            verificationLock.Release();
        }
    }

    private async Task PrepareReverificationAsync(
        Domain domain,
        Guid tenantId,
        string correlationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (domain.VerificationTokenIssuedAtUtc.HasValue &&
            domain.VerifiedAtUtc.HasValue &&
            domain.VerificationTokenIssuedAtUtc.Value > domain.VerifiedAtUtc.Value)
        {
            return;
        }

        var beforeSnapshot = SerializeDomainSnapshot(domain);
        domain.VerificationToken = GenerateVerificationToken();
        domain.VerificationTokenIssuedAtUtc = now;
        domain.UpdatedAtUtc = now;
        _dbContext.DomainAuditEntries.Add(new DomainAuditEntry
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            TenantId = tenantId,
            Action = "DomainReverificationStarted",
            ActorType = ActorType.System,
            BeforeSnapshotJson = beforeSnapshot,
            AfterSnapshotJson = SerializeDomainSnapshot(domain),
            TraceId = correlationId,
            TimestampUtc = now,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool RequiresReverification(Domain domain, DateTime now) =>
        domain.VerifiedAtUtc.HasValue &&
        domain.VerifiedAtUtc.Value <= now.AddDays(-TokenValidityDays) &&
        (domain.LifecycleState is DomainLifecycleState.Verified or DomainLifecycleState.SslPending or DomainLifecycleState.SslActive or DomainLifecycleState.Primary) &&
        !domain.IsDeleted;

    private static string GenerateVerificationToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"shopixy-domain-verify-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private async Task<DomainVerification?> TryClaimResolverAttemptAsync(
        Domain domain,
        int attemptNumber,
        string correlationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var pending = await _dbContext.DomainVerifications
            .IgnoreQueryFilters()
            .Where(verification =>
                verification.DomainId == domain.Id &&
                verification.Method == domain.VerificationMethod!.Value &&
                verification.Status == VerificationStatus.Pending &&
                !verification.IsDeleted)
            .OrderByDescending(verification => verification.AttemptedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is not null)
        {
            if (pending.AttemptedAtUtc > now - StaleVerificationClaimTimeout)
                return null;

            pending.Status = VerificationStatus.Failed;
            pending.FailureReason = DomainVerificationFailureReasons.Timeout;
            pending.ResolverEndpoint = "stale-claim-recovery";
            pending.UpdatedAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var claim = new DomainVerification
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Method = domain.VerificationMethod!.Value,
            Token = domain.VerificationToken,
            Status = VerificationStatus.Pending,
            AttemptedAtUtc = now,
            CorrelationId = correlationId,
            ResolverEndpoint = "durable-claim",
            Attempt = attemptNumber,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        };
        _dbContext.DomainVerifications.Add(claim);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return claim;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogInformation(
                "Domain verification durable claim lost to another worker | DomainId: {DomainId} | Method: {Method} | TraceId: {TraceId}",
                domain.Id,
                domain.VerificationMethod,
                correlationId);
            return null;
        }
    }

    private async Task<VerificationResult> MapSuccessfulReplayAsync(
        Domain domain,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var prior = await _dbContext.DomainVerifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(verification =>
                verification.DomainId == domain.Id &&
                verification.Status == VerificationStatus.Success &&
                !verification.IsDeleted)
            .OrderByDescending(verification => verification.VerifiedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new VerificationResult
        {
            DomainId = domain.Id,
            TenantId = tenantId,
            LifecycleState = domain.LifecycleState,
            Method = prior?.Method ?? domain.VerificationMethod ?? VerificationMethod.DnsTxt,
            Status = VerificationStatus.Success,
            FailureReason = null,
            ResolvedValue = prior?.ResolvedValue,
            ExpectedValue = domain.VerificationToken,
            Ttl = prior?.Ttl,
            ResolverEndpoint = prior?.ResolverEndpoint ?? "replay",
            Latency = prior?.LatencyMilliseconds is int latency
                ? TimeSpan.FromMilliseconds(latency)
                : TimeSpan.Zero,
            AttemptNumber = prior?.Attempt ?? 0,
            ResolvedAtUtc = prior?.VerifiedAtUtc ?? domain.VerifiedAtUtc.Value,
            RetryEligible = false,
            CorrelationId = correlationId,
        };
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("UQ_", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<AttemptEvaluation> ExecuteResolverAttemptAsync(
        Domain domain,
        CancellationToken cancellationToken)
    {
        var method = domain.VerificationMethod!.Value;
        var verificationHost = $"{VerificationHostPrefix}{domain.NormalizedHostName}";

        try
        {
            return method switch
            {
                VerificationMethod.DnsTxt => EvaluateDnsResult(
                    await _dnsResolver.ResolveTxtAsync(verificationHost, cancellationToken),
                    expectedValue: domain.VerificationToken,
                    normalizeResolvedValue: NormalizeTxtValue,
                    normalizeExpectedValue: static value => value,
                    compare: static (left, right) => StringComparer.Ordinal.Equals(left, right)),

                VerificationMethod.DnsCname => await ExecuteCnameResolverAttemptAsync(
                    verificationHost,
                    domain.VerificationToken,
                    cancellationToken),

                VerificationMethod.HttpFile => EvaluateHttpResult(
                    await _dnsResolver.ResolveHttpFileAsync(
                        $"https://{domain.NormalizedHostName}{HttpVerificationPath}",
                        cancellationToken),
                    expectedValue: domain.VerificationToken),

                _ => throw new NotSupportedException(
                    $"Verification method '{method}' is not supported by the Domain Verification Engine."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DnsResolverException ex)
        {
            return AttemptEvaluation.Failure(
                MapResolverErrorCode(ex.ErrorCode),
                resolvedValue: null,
                expectedValue: method == VerificationMethod.DnsCname
                    ? BuildExpectedCnameTarget(domain.VerificationToken)
                    : domain.VerificationToken,
                ttl: null,
                resolverEndpoint: "exception",
                latency: TimeSpan.Zero,
                resolvedAtUtc: DateTime.UtcNow);
        }
        catch (TimeoutException)
        {
            return AttemptEvaluation.Failure(
                DomainVerificationFailureReasons.Timeout,
                resolvedValue: null,
                expectedValue: method == VerificationMethod.DnsCname
                    ? BuildExpectedCnameTarget(domain.VerificationToken)
                    : domain.VerificationToken,
                ttl: null,
                resolverEndpoint: "exception",
                latency: TimeSpan.Zero,
                resolvedAtUtc: DateTime.UtcNow);
        }
    }

    private async Task<AttemptEvaluation> ExecuteCnameResolverAttemptAsync(
        string verificationHost,
        string verificationToken,
        CancellationToken cancellationToken)
    {
        var expectedTarget = BuildExpectedCnameTarget(verificationToken);
        var targetEvaluation = EvaluateDnsResult(
            await _dnsResolver.ResolveCnameAsync(verificationHost, cancellationToken),
            expectedTarget,
            NormalizeNullableCnameTarget,
            NormalizeCnameTarget,
            static (left, right) => StringComparer.Ordinal.Equals(left, right));

        if (targetEvaluation.Status != VerificationStatus.Success)
            return targetEvaluation;

        return EvaluateDnsResult(
            await _dnsResolver.ResolveTxtAsync(expectedTarget, cancellationToken),
            verificationToken,
            NormalizeTxtValue,
            static value => value,
            static (left, right) => StringComparer.Ordinal.Equals(left, right));
    }

    private static AttemptEvaluation EvaluateDnsResult(
        DnsLookupResult lookupResult,
        string expectedValue,
        Func<string?, string?> normalizeResolvedValue,
        Func<string, string> normalizeExpectedValue,
        Func<string, string, bool> compare)
    {
        var normalizedExpected = normalizeExpectedValue(expectedValue);
        var candidateValues = lookupResult.Values.Count > 0
            ? lookupResult.Values
            : lookupResult.Value is null
                ? Array.Empty<string>()
                : new[] { lookupResult.Value };
        var normalizedValues = candidateValues
            .Select(normalizeResolvedValue)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        var normalizedResolved = normalizedValues.FirstOrDefault();

        if (!lookupResult.Success)
        {
            return AttemptEvaluation.Failure(
                MapResolverErrorCode(lookupResult.ErrorCode),
                normalizedResolved,
                normalizedExpected,
                lookupResult.Ttl,
                lookupResult.ResolverEndpoint,
                lookupResult.Latency,
                lookupResult.QueriedAtUtc);
        }

        if (string.IsNullOrWhiteSpace(normalizedResolved))
        {
            return AttemptEvaluation.Failure(
                DomainVerificationFailureReasons.RecordMissing,
                normalizedResolved,
                normalizedExpected,
                lookupResult.Ttl,
                lookupResult.ResolverEndpoint,
                lookupResult.Latency,
                lookupResult.QueriedAtUtc);
        }

        var matchingValue = normalizedValues.FirstOrDefault(value => compare(value, normalizedExpected));
        if (matchingValue is null)
        {
            return AttemptEvaluation.Failure(
                DomainVerificationFailureReasons.TokenMismatch,
                normalizedResolved,
                normalizedExpected,
                lookupResult.Ttl,
                lookupResult.ResolverEndpoint,
                lookupResult.Latency,
                lookupResult.QueriedAtUtc);
        }

        return AttemptEvaluation.Success(
            matchingValue,
            normalizedExpected,
            lookupResult.Ttl,
            lookupResult.ResolverEndpoint,
            lookupResult.Latency,
            lookupResult.QueriedAtUtc);
    }

    private static AttemptEvaluation EvaluateHttpResult(
        HttpLookupResult lookupResult,
        string expectedValue)
    {
        var resolved = lookupResult.ResponseBody?.Trim();
        var expected = expectedValue;

        if (!lookupResult.Success)
        {
            return AttemptEvaluation.Failure(
                MapResolverErrorCode(lookupResult.ErrorCode),
                resolved,
                expected,
                ttl: null,
                resolverEndpoint: "https",
                lookupResult.Latency,
                lookupResult.QueriedAtUtc);
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return AttemptEvaluation.Failure(
                DomainVerificationFailureReasons.RecordMissing,
                resolved,
                expected,
                ttl: null,
                resolverEndpoint: "https",
                lookupResult.Latency,
                lookupResult.QueriedAtUtc);
        }

        if (!StringComparer.Ordinal.Equals(resolved, expected))
        {
            return AttemptEvaluation.Failure(
                DomainVerificationFailureReasons.TokenMismatch,
                resolved,
                expected,
                ttl: null,
                resolverEndpoint: "https",
                lookupResult.Latency,
                lookupResult.QueriedAtUtc);
        }

        return AttemptEvaluation.Success(
            resolved,
            expected,
            ttl: null,
            resolverEndpoint: "https",
            lookupResult.Latency,
            lookupResult.QueriedAtUtc);
    }

    private static string? ValidateVerificationEligibility(
        Domain domain,
        DateTime now,
        bool requiresReverification)
    {
        if (requiresReverification)
        {
            if (string.IsNullOrWhiteSpace(domain.VerificationToken) || !domain.VerificationMethod.HasValue)
                return DomainVerificationFailureReasons.UnsupportedMethod;

            return null;
        }

        if (domain.IsDeleted ||
            domain.DomainType == DomainType.Primary ||
            domain.LifecycleState == DomainLifecycleState.Primary ||
            domain.LifecycleState == DomainLifecycleState.Archived ||
            domain.LifecycleState == DomainLifecycleState.Deleted ||
            domain.LifecycleState == DomainLifecycleState.Disabled ||
            domain.LifecycleState == DomainLifecycleState.Verified ||
            domain.LifecycleState == DomainLifecycleState.SslPending ||
            domain.LifecycleState == DomainLifecycleState.SslActive ||
            domain.VerifiedAtUtc.HasValue)
        {
            return DomainVerificationFailureReasons.LifecycleViolation;
        }

        if (domain.LifecycleState != DomainLifecycleState.PendingVerification)
        {
            return DomainVerificationFailureReasons.LifecycleViolation;
        }

        if (string.IsNullOrWhiteSpace(domain.VerificationToken) ||
            !domain.VerificationMethod.HasValue)
        {
            return DomainVerificationFailureReasons.UnsupportedMethod;
        }

        var tokenIssuedAtUtc = domain.VerificationTokenIssuedAtUtc ?? domain.CreatedAtUtc;
        if (tokenIssuedAtUtc < now.AddDays(-TokenValidityDays))
        {
            return DomainVerificationFailureReasons.ExpiredToken;
        }

        _ = domain.VerificationMethod.Value switch
        {
            VerificationMethod.DnsTxt => true,
            VerificationMethod.DnsCname => true,
            VerificationMethod.HttpFile => true,
            _ => throw new NotSupportedException(
                $"Verification method '{domain.VerificationMethod.Value}' is not supported by the Domain Verification Engine."),
        };

        return null;
    }

    private async Task<int> GetNextAttemptNumberAsync(Guid domainId, CancellationToken cancellationToken)
    {
        var maxAttempt = await _dbContext.DomainVerifications
            .IgnoreQueryFilters()
            .Where(v => v.DomainId == domainId && !v.IsDeleted)
            .Select(v => (int?)v.Attempt)
            .MaxAsync(cancellationToken);
        return (maxAttempt ?? 0) + 1;
    }

    private async Task<int> CountFailedAttemptsLastHourAsync(
        Guid domainId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-1);
        return await _dbContext.DomainVerifications
            .IgnoreQueryFilters()
            .CountAsync(
                v => v.DomainId == domainId &&
                     !v.IsDeleted &&
                     v.Status == VerificationStatus.Failed &&
                     v.AttemptedAtUtc > cutoff,
                cancellationToken);
    }

    private async Task<DateTime> GetFailedAttemptRateLimitRetryAfterUtcAsync(
        Guid domainId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-1);
        var earliestFailedAttempt = await _dbContext.DomainVerifications
            .IgnoreQueryFilters()
            .Where(v => v.DomainId == domainId &&
                        !v.IsDeleted &&
                        v.Status == VerificationStatus.Failed &&
                        v.AttemptedAtUtc > cutoff)
            .OrderBy(v => v.AttemptedAtUtc)
            .Select(v => (DateTime?)v.AttemptedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return (earliestFailedAttempt ?? now).AddHours(1);
    }

    private static bool IsRetryEligible(string? failureReason, int failedAttemptsLastHour)
    {
        if (failureReason is null)
        {
            return false;
        }

        if (failedAttemptsLastHour >= MaxFailedAttemptsPerHour)
        {
            return false;
        }

        return failureReason is
            DomainVerificationFailureReasons.RecordMissing or
            DomainVerificationFailureReasons.TokenMismatch or
            DomainVerificationFailureReasons.Timeout or
            DomainVerificationFailureReasons.ResolverFailure or
            DomainVerificationFailureReasons.NetworkFailure;
    }

    private static string MapResolverErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "NXDOMAIN" => DomainVerificationFailureReasons.RecordMissing,
            "HTTP_404" => DomainVerificationFailureReasons.RecordMissing,
            "TIMEOUT" => DomainVerificationFailureReasons.Timeout,
            "HTTP_TIMEOUT" => DomainVerificationFailureReasons.Timeout,
            "NETWORK" => DomainVerificationFailureReasons.NetworkFailure,
            "HTTP_403" => DomainVerificationFailureReasons.Unauthorized,
            "SSL_ERROR" => DomainVerificationFailureReasons.NetworkFailure,
            "SERVFAIL" => DomainVerificationFailureReasons.ResolverFailure,
            "REFUSED" => DomainVerificationFailureReasons.ResolverFailure,
            "HTTP_500" => DomainVerificationFailureReasons.ResolverFailure,
            "UNKNOWN" => DomainVerificationFailureReasons.ResolverFailure,
            _ => DomainVerificationFailureReasons.ResolverFailure,
        };
    }

    private static string? NormalizeTxtValue(string? value)
    {
        return value;
    }

    private static string NormalizeCnameTarget(string value)
    {
        return value.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string? NormalizeNullableCnameTarget(string? value)
    {
        return value is null ? null : NormalizeCnameTarget(value);
    }

    private static string BuildExpectedCnameTarget(string verificationToken)
    {
        var token = verificationToken.Trim().TrimEnd('.');

        if (token.Contains('.', StringComparison.Ordinal))
        {
            return NormalizeCnameTarget(token);
        }

        var label = new string(token
            .ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' ? ch : '-')
            .ToArray())
            .Trim('-');

        if (label.Length > 48)
        {
            label = label[..48].Trim('-');
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = "token";
        }

        return $"verify-{label}.domains.shopixy.com";
    }

    private static string NormalizeCorrelationId(string correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId.Trim();
        }

        return Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
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
        }, JsonOptions);
    }

    private static string SerializeVerificationAuditSnapshot(
        Domain domain,
        DomainVerification verification,
        AttemptEvaluation evaluation,
        bool retryEligible,
        string correlationId)
    {
        return JsonSerializer.Serialize(new
        {
            Domain = new
            {
                domain.Id,
                domain.TenantId,
                domain.HostName,
                domain.NormalizedHostName,
                domain.DomainType,
                domain.LifecycleState,
                domain.VerifiedAtUtc,
                domain.UpdatedAtUtc,
                domain.IsDeleted,
                domain.Version,
            },
            Verification = new
            {
                verification.Id,
                verification.DomainId,
                verification.Method,
                verification.Status,
                verification.Attempt,
                verification.AttemptedAtUtc,
                verification.VerifiedAtUtc,
                verification.FailureReason,
                verification.CorrelationId,
                verification.ResolverEndpoint,
                verification.LatencyMilliseconds,
                verification.Ttl,
                RetryEligible = retryEligible,
                AuditCorrelationId = correlationId,
            }
        }, JsonOptions);
    }

    private sealed record AttemptEvaluation(
        VerificationStatus Status,
        string? FailureReason,
        string? ResolvedValue,
        string ExpectedValue,
        int? Ttl,
        string ResolverEndpoint,
        TimeSpan Latency,
        DateTime ResolvedAtUtc)
    {
        public static AttemptEvaluation Success(
            string resolvedValue,
            string expectedValue,
            int? ttl,
            string resolverEndpoint,
            TimeSpan latency,
            DateTime resolvedAtUtc) =>
            new(
                VerificationStatus.Success,
                null,
                resolvedValue,
                expectedValue,
                ttl,
                resolverEndpoint,
                latency,
                resolvedAtUtc);

        public static AttemptEvaluation Failure(
            string failureReason,
            string? resolvedValue,
            string expectedValue,
            int? ttl,
            string resolverEndpoint,
            TimeSpan latency,
            DateTime resolvedAtUtc) =>
            new(
                VerificationStatus.Failed,
                failureReason,
                resolvedValue,
                expectedValue,
                ttl,
                resolverEndpoint,
                latency,
                resolvedAtUtc);
    }
}
