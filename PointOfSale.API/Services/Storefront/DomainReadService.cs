using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

/// <inheritdoc />
public sealed class DomainReadService : IDomainReadService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DomainReadService> _logger;

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const string VerificationHostPrefix = "_shopixy-verification";
    private const string HttpVerificationPath = "/.well-known/shopixy-domain-verification.txt";

    public DomainReadService(ApplicationDbContext dbContext, ILogger<DomainReadService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DomainListPageDto> GetDomainListAsync(
        Guid tenantId,
        int page,
        int pageSize,
        string? search = null,
        DomainType? typeFilter = null,
        DomainLifecycleState? stateFilter = null,
        string? sortBy = null,
        string? sortDir = null,
        CancellationToken ct = default)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? "unknown";

        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning(
                "DomainReadService.GetDomainListAsync called with empty tenantId | TraceId: {TraceId}", traceId);
            return EmptyPage<DomainSummaryDto, DomainListPageDto>(page, pageSize);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        try
        {
            // Projection-based query: selects only the fields needed by DomainSummaryDto.
            // Resolves the active certificate from the Domain.SslCertificateId pointer.
            // Soft-delete filter (!d.IsDeleted) and tenant isolation (d.TenantId) are applied via WHERE.
            var query = _dbContext.Domains
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId && !d.IsDeleted)
                .TagWith($"GetDomainListAsync | TenantId: {tenantId} | TraceId: {traceId}")
                .Select(d => new
                {
                    Domain = d,
                    Version = d.Version,
                    SslStatus = d.SslCertificates
                        .Where(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted)
                        .Select(c => (CertificateStatus?)c.Status)
                        .FirstOrDefault(),
                    SslNotAfterUtc = d.SslCertificates
                        .Where(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted)
                        .Select(c => c.NotAfterUtc)
                        .FirstOrDefault(),
                    HasSsl = d.SslCertificates
                        .Any(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted),
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLowerInvariant();
                query = query.Where(x => EF.Functions.Like(x.Domain.NormalizedHostName, $"%{searchLower}%"));
            }

            if (typeFilter.HasValue)
            {
                query = query.Where(x => x.Domain.DomainType == typeFilter.Value);
            }

            if (stateFilter.HasValue)
            {
                query = query.Where(x => x.Domain.LifecycleState == stateFilter.Value);
            }

            var sortField = sortBy?.ToLowerInvariant() ?? "hostname";
            var isDescending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

            query = sortField switch
            {
                "type" => isDescending
                    ? query.OrderByDescending(x => x.Domain.DomainType)
                    : query.OrderBy(x => x.Domain.DomainType),
                "status" => isDescending
                    ? query.OrderByDescending(x => x.Domain.LifecycleState)
                    : query.OrderBy(x => x.Domain.LifecycleState),
                "created" => isDescending
                    ? query.OrderByDescending(x => x.Domain.CreatedAtUtc)
                    : query.OrderBy(x => x.Domain.CreatedAtUtc),
                _ => isDescending
                    ? query.OrderByDescending(x => x.Domain.NormalizedHostName)
                    : query.OrderBy(x => x.Domain.NormalizedHostName),
            };

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var result = items.Select(x => MapToSummaryDtoProjection(
                x.Domain, x.HasSsl, x.SslStatus ?? CertificateStatus.Pending, x.SslNotAfterUtc)).ToList();

            return new DomainListPageDto
            {
                Items = result,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                HasNextPage = page < (int)Math.Ceiling((double)totalCount / pageSize),
                HasPreviousPage = page > 1,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error fetching domain list | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            // An unavailable query is not evidence of an empty portfolio. Let the
            // controller return its error contract so the administrator can retry.
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DomainDetailDto?> GetDomainDetailAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default)
    {
        // Single query with projection — no N+1 from navigation property access.
        // Only fetches the columns needed for DomainDetailDto.
        var domain = await _dbContext.Domains
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted)
            .Select(d => new
            {
                d.Id,
                d.Version,
                d.HostName,
                d.NormalizedHostName,
                d.DomainType,
                d.LifecycleState,
                d.RedirectType,
                d.RedirectTargetDomainId,
                RedirectTargetHost = d.RedirectTargetDomain == null ? null : d.RedirectTargetDomain.HostName,
                d.CanonicalPolicy,
                SslCertificateThumbprint = d.SslCertificates
                    .Where(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted)
                    .Select(c => c.Thumbprint)
                    .FirstOrDefault(),
                d.SslCertificateId,
                d.VerifiedAtUtc,
                d.CreatedAtUtc,
                d.SslActivatedAtUtc,
                d.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (domain == null)
        {
            return null;
        }

        return new DomainDetailDto
        {
            Id = domain.Id,
            HostName = domain.HostName,
            NormalizedHostName = domain.NormalizedHostName,
            DomainType = domain.DomainType,
            LifecycleState = domain.LifecycleState,
            IsPrimary = domain.DomainType == DomainType.Primary &&
                        domain.LifecycleState == DomainLifecycleState.Primary,
            Version = domain.Version,
            RedirectType = domain.RedirectType,
            RedirectTargetDomainId = domain.RedirectTargetDomainId,
            RedirectTargetHost = domain.RedirectTargetHost,
            CanonicalPolicy = domain.CanonicalPolicy,
            SslCertificateThumbprint = domain.SslCertificateThumbprint,
            SslCertificateId = domain.SslCertificateId,
            IsVerified = domain.VerifiedAtUtc.HasValue,
            CreatedAtUtc = domain.CreatedAtUtc,
            VerifiedAtUtc = domain.VerifiedAtUtc,
            SslActivatedAtUtc = domain.SslActivatedAtUtc,
            UpdatedAtUtc = domain.UpdatedAtUtc,
            CanonicalUrl = domain.DomainType == DomainType.Primary
                ? $"https://{domain.NormalizedHostName}"
                : null,
        };
    }

    /// <inheritdoc />
    public async Task<DomainDashboardSummaryDto> GetDashboardSummaryAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? "unknown";

        // Single-pass aggregation query: computes all dashboard aggregates in one round-trip.
        // Avoids N+1 from separate primary-domain and recent-domains queries.
        var aggregates = await _dbContext.Domains
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && !d.IsDeleted)
            .GroupBy(d => 1)
            .Select(g => new
            {
                TotalCount = g.Count(),
                SslExpiringSoon = g.Count(d => d.SslCertificates.Any(c =>
                    d.SslCertificateId != null &&
                    c.Id == d.SslCertificateId &&
                    !c.IsDeleted &&
                    c.NotAfterUtc.HasValue &&
                    c.NotAfterUtc <= DateTime.UtcNow.AddDays(30) &&
                    c.Status == CertificateStatus.Active)),
                PendingVerification = g.Count(d => d.LifecycleState == DomainLifecycleState.PendingVerification),
                DraftCount = g.Count(d => d.LifecycleState == DomainLifecycleState.Draft),
                PrimaryDomain = g
                    .Where(d => d.DomainType == DomainType.Primary && d.LifecycleState == DomainLifecycleState.Primary)
                    .Select(d => new DomainSummaryDto
                    {
                        Id = d.Id,
                        HostName = d.HostName,
                        DomainType = d.DomainType,
                        LifecycleState = d.LifecycleState,
                        IsPrimary = true,
                        Version = d.Version,
                        RedirectType = d.RedirectType,
                        SslStatus = d.SslCertificates
                            .Where(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted)
                            .Select(c => (CertificateStatus?)c.Status)
                            .FirstOrDefault() ?? CertificateStatus.Pending,
                        SslExpiryDate = d.SslCertificates
                            .Where(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted)
                            .Select(c => c.NotAfterUtc == null ? null : c.NotAfterUtc.Value.ToString("yyyy-MM-dd"))
                            .FirstOrDefault(),
                        IsVerified = d.VerifiedAtUtc.HasValue,
                        CreatedAtUtc = d.CreatedAtUtc,
                        UpdatedAtUtc = d.UpdatedAtUtc,
                        CanonicalUrl = d.DomainType == DomainType.Primary
                            ? $"https://{d.NormalizedHostName}"
                            : null,
                    })
                    .FirstOrDefault(),
                RecentDomains = g
                    .OrderByDescending(d => d.CreatedAtUtc)
                    .Take(5)
                    .Select(d => new DomainSummaryDto
                    {
                        Id = d.Id,
                        HostName = d.HostName,
                        DomainType = d.DomainType,
                        LifecycleState = d.LifecycleState,
                        IsPrimary = d.DomainType == DomainType.Primary && d.LifecycleState == DomainLifecycleState.Primary,
                        Version = d.Version,
                        RedirectType = d.RedirectType,
                        SslStatus = d.SslCertificates
                            .Where(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted)
                            .Select(c => (CertificateStatus?)c.Status)
                            .FirstOrDefault() ?? CertificateStatus.Pending,
                        SslExpiryDate = d.SslCertificates
                            .Where(c => d.SslCertificateId != null && c.Id == d.SslCertificateId && !c.IsDeleted)
                            .Select(c => c.NotAfterUtc == null ? null : c.NotAfterUtc.Value.ToString("yyyy-MM-dd"))
                            .FirstOrDefault(),
                        IsVerified = d.VerifiedAtUtc.HasValue,
                        CreatedAtUtc = d.CreatedAtUtc,
                        UpdatedAtUtc = d.UpdatedAtUtc,
                        CanonicalUrl = d.DomainType == DomainType.Primary
                            ? $"https://{d.NormalizedHostName}"
                            : null,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct) ?? new
            {
                TotalCount = 0,
                SslExpiringSoon = 0,
                PendingVerification = 0,
                DraftCount = 0,
                PrimaryDomain = (DomainSummaryDto?)null,
                RecentDomains = new List<DomainSummaryDto>(),
            };

        _ = traceId; // retained for future logging integration

        return new DomainDashboardSummaryDto
        {
            PrimaryDomain = aggregates.PrimaryDomain,
            TotalDomains = aggregates.TotalCount,
            SslExpiringSoon = aggregates.SslExpiringSoon,
            PendingVerification = aggregates.PendingVerification,
            DraftCount = aggregates.DraftCount,
            RecentDomains = aggregates.RecentDomains,
        };
    }

    /// <inheritdoc />
    public async Task<DomainVerificationDto?> GetDomainVerificationAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default)
    {
        // Single query: join Domain with its latest DomainVerification in one round-trip.
        // Avoids the previous two-query pattern (first Domain, then separate verification query).
        var result = await _dbContext.Domains
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted)
            .Select(d => new
            {
                d.Id,
                d.Version,
                d.HostName,
                d.NormalizedHostName,
                d.VerificationMethod,
                d.VerificationToken,
                d.VerifiedAtUtc,
                LatestVerification = d.Verifications
                    .Where(v => !v.IsDeleted)
                    .OrderByDescending(v => v.AttemptedAtUtc)
                    .Select(v => new
                    {
                        v.Status,
                    })
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        if (result == null)
        {
            return null;
        }

        var method = result.VerificationMethod ?? VerificationMethod.DnsTxt;
        var instructions = BuildVerificationInstructions(
            result.NormalizedHostName,
            result.VerificationToken,
            method);

        return new DomainVerificationDto
        {
            DomainId = result.Id,
            Version = result.Version,
            HostName = result.HostName,
            NormalizedHostName = result.NormalizedHostName,
            Status = result.LatestVerification?.Status ?? VerificationStatus.Pending,
            Method = method,
            VerificationToken = result.VerificationToken,
            VerifiedAtUtc = result.VerifiedAtUtc,
            DnsRecordType = instructions.DnsRecordType,
            DnsRecordName = instructions.DnsRecordName,
            DnsRecordValue = instructions.DnsRecordValue,
            DnsRecordHost = instructions.DnsRecordHost,
            HttpVerificationUrl = instructions.HttpVerificationUrl,
        };
    }

    private static VerificationInstructions BuildVerificationInstructions(
        string normalizedHostName,
        string? verificationToken,
        VerificationMethod method)
    {
        var verificationHost = $"{VerificationHostPrefix}.{normalizedHostName}";

        return method switch
        {
            VerificationMethod.DnsCname => new VerificationInstructions(
                DnsRecordType: "CNAME",
                DnsRecordName: verificationHost,
                DnsRecordValue: BuildExpectedCnameTarget(verificationToken),
                DnsRecordHost: VerificationHostPrefix,
                HttpVerificationUrl: null),

            VerificationMethod.HttpFile => new VerificationInstructions(
                DnsRecordType: null,
                DnsRecordName: null,
                DnsRecordValue: verificationToken,
                DnsRecordHost: null,
                HttpVerificationUrl: $"https://{normalizedHostName}{HttpVerificationPath}"),

            _ => new VerificationInstructions(
                DnsRecordType: "TXT",
                DnsRecordName: verificationHost,
                DnsRecordValue: verificationToken,
                DnsRecordHost: VerificationHostPrefix,
                HttpVerificationUrl: null),
        };
    }

    private static string BuildExpectedCnameTarget(string? verificationToken)
    {
        var token = verificationToken?.Trim() ?? string.Empty;
        if (token.EndsWith(".domains.shopixy.com", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeCnameTarget(token);
        }

        var label = token;
        const string tokenPrefix = "shopixy-domain-verify-";
        if (label.StartsWith(tokenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            label = label[tokenPrefix.Length..];
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = "token";
        }

        return $"verify-{label}.domains.shopixy.com";
    }

    private static string NormalizeCnameTarget(string value)
    {
        return value.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private sealed record VerificationInstructions(
        string? DnsRecordType,
        string? DnsRecordName,
        string? DnsRecordValue,
        string? DnsRecordHost,
        string? HttpVerificationUrl);

    /// <inheritdoc />
    public async Task<DomainSslDto?> GetDomainSslAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default)
    {
        var domain = await _dbContext.Domains
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted)
            .Select(d => new
            {
                d.Id,
                d.SslCertificateId,
            })
            .FirstOrDefaultAsync(ct);

        if (domain == null)
        {
            return null;
        }

        var cert = await _dbContext.SslCertificates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => !c.IsDeleted &&
                        c.DomainId == domain.Id &&
                        c.TenantId == tenantId &&
                        (domain.SslCertificateId == null
                            ? c.Status == CertificateStatus.Active
                            : c.Id == domain.SslCertificateId))
            .OrderByDescending(c => c.Generation)
            .FirstOrDefaultAsync(ct);

        if (cert == null)
        {
            return new DomainSslDto
            {
                DomainId = domainId,
                Status = CertificateStatus.Pending,
                NotBefore = null,
                NotAfter = null,
                NotAfterUtc = null,
                AutoRenewEnabled = true,
                NextRenewalUtc = null,
                ActivatedAtUtc = null,
                DaysUntilExpiry = 0,
                ExpiringSoon = false,
            };
        }

        return MapToSslDto(cert);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DomainRedirectDto>?> GetDomainRedirectsAsync(
        Guid tenantId,
        Guid domainId,
        CancellationToken ct = default)
    {
        // Single query: domain existence check is done by checking if the query returns any rows.
        // The domain existence gate replaces the previous separate AnyAsync query.
        var domainExists = await _dbContext.Domains
            .AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted, ct);

        if (!domainExists)
        {
            return null;
        }

        // Projection query: selects only the fields needed by DomainRedirectDto.
        // Avoids .Include(d => d.SslCertificate) and .Include(d => d.RedirectTargetDomain) —
        // only RedirectTargetDomain.HostName is needed.
        var redirectSources = await _dbContext.Domains
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId &&
                        d.RedirectTargetDomainId == domainId &&
                        d.RedirectType != RedirectType.None &&
                        !d.IsDeleted)
            .Select(d => new DomainRedirectDto
            {
                Id = d.Id,
                DomainId = d.Id,
                Source = d.NormalizedHostName,
                Target = d.RedirectTargetDomain == null ? null : d.RedirectTargetDomain.HostName,
                Type = d.RedirectType,
                IsActive = d.LifecycleState == DomainLifecycleState.SslActive || d.LifecycleState == DomainLifecycleState.Primary,
                CreatedAtUtc = d.CreatedAtUtc,
            })
            .ToListAsync(ct);

        return redirectSources.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<DomainHistoryPageDto> GetDomainHistoryAsync(
        Guid tenantId,
        Guid domainId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Verify the domain exists and belongs to this tenant before returning history.
        // This ensures a 404 for non-existent or cross-tenant domains.
        var domainExists = await _dbContext.Domains
            .AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.Id == domainId && !d.IsDeleted, ct);

        if (!domainExists)
        {
            return EmptyPage<DomainHistoryEntryDto, DomainHistoryPageDto>(page, pageSize);
        }

        var query = _dbContext.DomainAuditEntries
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.DomainId == domainId)
            .OrderByDescending(e => e.TimestampUtc)
            .TagWith($"GetDomainHistoryAsync | DomainId: {domainId}");

        var totalCount = await query.CountAsync(ct);

        var entries = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new DomainHistoryPageDto
        {
            Items = entries.Select(MapToHistoryDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            HasNextPage = page < (int)Math.Ceiling((double)totalCount / pageSize),
            HasPreviousPage = page > 1,
        };
    }

    /// <inheritdoc />
    public async Task<DomainActivitySummaryDto> GetActivitySummaryAsync(
        Guid tenantId,
        int limit,
        CancellationToken ct = default)
    {
        // Single query with LEFT JOIN to Domain: projects HostName directly.
        // Avoids .Include(e => e.Domain) which would materialize full Domain entities
        // and cause N+1 when the consumer iterates navigation properties.
        var recentEntries = await _dbContext.DomainAuditEntries
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(limit)
            .Select(e => new
            {
                e.Id,
                e.DomainId,
                HostName = e.Domain!.HostName,
                e.Action,
                e.AfterSnapshotJson,
                e.TimestampUtc,
            })
            .ToListAsync(ct);

        var recentActivity = recentEntries.Select(e => new DomainActivityEntryDto
        {
            Id = e.Id,
            DomainId = e.DomainId,
            HostName = e.HostName,
            EventType = e.Action,
            Status = e.Action.Contains("fail", StringComparison.OrdinalIgnoreCase) ? "failed" : "success",
            Details = e.AfterSnapshotJson,
            TimestampUtc = e.TimestampUtc,
            CompletedAtUtc = e.TimestampUtc,
        }).ToList();

        var jobsRunning = recentActivity.Count(a => a.Status == "in-progress");
        var jobsSucceeded = recentActivity.Count(a => a.Status == "success");
        var jobsFailed = recentActivity.Count(a => a.Status == "failed");

        return new DomainActivitySummaryDto
        {
            JobsRunning = jobsRunning,
            JobsSucceeded = jobsSucceeded,
            JobsFailed = jobsFailed,
            RecentActivity = recentActivity,
        };
    }

    // =========================================================================
    // Mapping helpers
    // =========================================================================

    /// <summary>
    /// Maps a Domain entity to DomainSummaryDto using pre-projected SSL fields.
    /// This avoids accessing the SslCertificate navigation property (which would
    /// cause lazy-loading N+1 in projection contexts).
    /// </summary>
    private static DomainSummaryDto MapToSummaryDtoProjection(
        Domain domain,
        bool hasSsl,
        CertificateStatus sslStatus,
        DateTime? sslNotAfterUtc)
    {
        return new DomainSummaryDto
        {
            Id = domain.Id,
            HostName = domain.HostName,
            DomainType = domain.DomainType,
            LifecycleState = domain.LifecycleState,
            IsPrimary = domain.DomainType == DomainType.Primary &&
                        domain.LifecycleState == DomainLifecycleState.Primary,
            Version = domain.Version,
            RedirectType = domain.RedirectType,
            SslStatus = hasSsl ? sslStatus : CertificateStatus.Pending,
            SslExpiryDate = hasSsl ? sslNotAfterUtc?.ToString("yyyy-MM-dd") : null,
            IsVerified = domain.VerifiedAtUtc.HasValue,
            CreatedAtUtc = domain.CreatedAtUtc,
            UpdatedAtUtc = domain.UpdatedAtUtc,
            CanonicalUrl = domain.DomainType == DomainType.Primary
                ? $"https://{domain.NormalizedHostName}"
                : null,
        };
    }

    /// <summary>
    /// Backwards-compatible entity-based mapper for paths that still materialize entities.
    /// </summary>
    private static DomainSummaryDto MapToSummaryDto(Domain domain)
    {
        return new DomainSummaryDto
        {
            Id = domain.Id,
            HostName = domain.HostName,
            DomainType = domain.DomainType,
            LifecycleState = domain.LifecycleState,
            IsPrimary = domain.DomainType == DomainType.Primary &&
                        domain.LifecycleState == DomainLifecycleState.Primary,
            Version = domain.Version,
            RedirectType = domain.RedirectType,
            SslStatus = domain.SslCertificate?.Status ?? CertificateStatus.Pending,
            SslExpiryDate = domain.SslCertificate?.NotAfterUtc?.ToString("yyyy-MM-dd"),
            IsVerified = domain.VerifiedAtUtc.HasValue,
            CreatedAtUtc = domain.CreatedAtUtc,
            UpdatedAtUtc = domain.UpdatedAtUtc,
            CanonicalUrl = domain.DomainType == DomainType.Primary
                ? $"https://{domain.NormalizedHostName}"
                : null,
        };
    }

    private static DomainSslDto MapToSslDto(SslCertificate cert)
    {
        var notAfter = cert.NotAfterUtc;
        var daysUntilExpiry = notAfter.HasValue
            ? (int)(notAfter.Value - DateTime.UtcNow).TotalDays
            : 0;

        return new DomainSslDto
        {
            DomainId = cert.DomainId,
            CertificateId = cert.Id,
            Provider = cert.Provider,
            Status = cert.Status,
            Thumbprint = cert.Thumbprint,
            NotBefore = cert.NotBeforeUtc?.ToString("yyyy-MM-dd"),
            NotAfter = cert.NotAfterUtc?.ToString("yyyy-MM-dd"),
            NotAfterUtc = cert.NotAfterUtc,
            AutoRenewEnabled = cert.NextRenewalUtc.HasValue,
            NextRenewalUtc = cert.NextRenewalUtc,
            ActivatedAtUtc = cert.ActivatedAtUtc,
            SANs = ParseSans(cert),
            DaysUntilExpiry = daysUntilExpiry,
            ExpiringSoon = daysUntilExpiry <= 30 && notAfter.HasValue,
        };
    }

    private static List<string> ParseSans(SslCertificate cert)
    {
        var sans = new List<string>();

        try
        {
            if (!string.IsNullOrWhiteSpace(cert.SubjectAlternativeNamesJson))
            {
                var entries = System.Text.Json.JsonDocument.Parse(cert.SubjectAlternativeNamesJson)
                    .RootElement.GetRawText();
                if (entries.StartsWith("["))
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(entries);
                    if (arr != null) sans.AddRange(arr);
                }
            }
        }
        catch
        {
            // CertificateArn is not a JSON array — it's an ARN string, ignore
        }

        return sans;
    }

    private static DomainHistoryEntryDto MapToHistoryDto(DomainAuditEntry entry)
    {
        return new DomainHistoryEntryDto
        {
            Id = entry.Id,
            DomainId = entry.DomainId,
            Action = entry.Action,
            ActorType = entry.ActorType,
            ActorUserId = entry.ActorUserId,
            BeforeSnapshotJson = entry.BeforeSnapshotJson,
            AfterSnapshotJson = entry.AfterSnapshotJson,
            TraceId = entry.TraceId,
            IpAddress = entry.IpAddress,
            TimestampUtc = entry.TimestampUtc,
        };
    }

    private static TPage EmptyPage<TItem, TPage>(int page, int pageSize)
        where TPage : new()
    {
        var result = new TPage();

        var pageProp = typeof(TPage).GetProperty("Page");
        var pageSizeProp = typeof(TPage).GetProperty("PageSize");
        var totalCountProp = typeof(TPage).GetProperty("TotalCount");
        var totalPagesProp = typeof(TPage).GetProperty("TotalPages");
        var hasNextProp = typeof(TPage).GetProperty("HasNextPage");
        var hasPrevProp = typeof(TPage).GetProperty("HasPreviousPage");
        var itemsProp = typeof(TPage).GetProperty("Items");

        pageProp?.SetValue(result, page);
        pageSizeProp?.SetValue(result, pageSize);
        totalCountProp?.SetValue(result, 0);
        totalPagesProp?.SetValue(result, 0);
        hasNextProp?.SetValue(result, false);
        hasPrevProp?.SetValue(result, page > 1);

        if (typeof(TItem) == typeof(DomainSummaryDto))
        {
            itemsProp?.SetValue(result, new List<DomainSummaryDto>());
        }
        else if (typeof(TItem) == typeof(DomainHistoryEntryDto))
        {
            itemsProp?.SetValue(result, new List<DomainHistoryEntryDto>());
        }

        return result;
    }
}
