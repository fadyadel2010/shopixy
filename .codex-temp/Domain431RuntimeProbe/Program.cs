using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

var connectionString = Environment.GetEnvironmentVariable("POS_PROBE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("SKIP: POS_PROBE_CONNECTION_STRING is not set.");
    return 2;
}

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using (var schemaContext = new ApplicationDbContext(options))
{
    await schemaContext.Database.ExecuteSqlRawAsync("""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'DomainVerifications' AND column_name = 'CorrelationId') THEN
                ALTER TABLE "DomainVerifications" ADD "CorrelationId" character varying(128);
            END IF;

            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'DomainVerifications' AND column_name = 'ResolverEndpoint') THEN
                ALTER TABLE "DomainVerifications" ADD "ResolverEndpoint" character varying(128);
            END IF;

            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'DomainVerifications' AND column_name = 'LatencyMilliseconds') THEN
                ALTER TABLE "DomainVerifications" ADD "LatencyMilliseconds" integer;
            END IF;

            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'DomainVerifications' AND column_name = 'Attempt') THEN
                ALTER TABLE "DomainVerifications" ADD "Attempt" integer NOT NULL DEFAULT 0;
            END IF;

            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'DomainVerifications' AND column_name = 'Ttl') THEN
                ALTER TABLE "DomainVerifications" ADD "Ttl" integer;
            END IF;

            IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'DomainVerifications' AND column_name = 'ResolvedValue') THEN
                ALTER TABLE "DomainVerifications" ADD "ResolvedValue" character varying(1024);
            END IF;

            IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics') THEN
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260809090000_DOMAIN_4_3_DomainVerificationEngineDiagnostics', '9.0.1');
            END IF;
        END $$;
        """);
}

await using var tenantLookupContext = new ApplicationDbContext(options);
var tenantId = await tenantLookupContext.Tenants
    .IgnoreQueryFilters()
    .Where(tenant => tenant.IsActive && tenant.DeletedAtUtc == null)
    .Select(tenant => tenant.Id)
    .FirstOrDefaultAsync();

if (tenantId == Guid.Empty)
{
    Console.WriteLine("SKIP: no active tenant found in existing database.");
    return 3;
}

var tenantContext = new ProbeTenantContext(tenantId);
await using var context = new ApplicationDbContext(options, tenantContext);
var repository = new DomainRepository(context);
var events = new RecordingEventPublisher();
var writeService = new DomainWriteService(
    context,
    repository,
    new HostNormalizer(),
    events,
    NullLogger<DomainWriteService>.Instance);

var staleProbeDomainIds = await context.Domains
    .IgnoreQueryFilters()
    .Where(domain =>
        domain.TenantId == tenantId &&
        !domain.IsDeleted &&
        domain.DomainType != DomainType.Primary &&
        EF.Functions.Like(domain.HostName, "domain431-%.example.com"))
    .Select(domain => domain.Id)
    .ToListAsync();

foreach (var staleProbeDomainId in staleProbeDomainIds)
{
    await writeService.DeleteDomainAsync(
        staleProbeDomainId,
        new DeleteDomainRequest { Reason = "DOMAIN-4.3.1 stale runtime probe cleanup" },
        tenantId,
        actorUserId: null,
        correlationId: "domain-4-3-1-db-stale-cleanup");
}

var hostName = $"domain431-{Guid.NewGuid():N}.example.com";
var create = await writeService.CreateDomainAsync(
    new CreateDomainRequest
    {
        HostName = hostName,
        DomainType = DomainType.Alias,
        CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
    },
    tenantId,
    actorUserId: null,
    correlationId: "domain-4-3-1-db-create");

var start = await writeService.StartDomainVerificationAsync(
    create.DomainId,
    new StartDomainVerificationRequest
    {
        VerificationMethod = VerificationMethod.DnsTxt,
        ExpectedVersion = create.Version,
    },
    tenantId,
    actorUserId: null,
    correlationId: "domain-4-3-1-db-start");

var pendingDomain = await context.Domains
    .IgnoreQueryFilters()
    .SingleAsync(domain => domain.Id == create.DomainId);

var resolver = new ProbeDnsResolver(pendingDomain.VerificationToken);
var verificationEvents = new RecordingEventPublisher();
var verificationService = new DomainVerificationService(
    context,
    resolver,
    verificationEvents,
    NullLogger<DomainVerificationService>.Instance);

var verificationResult = await verificationService.VerifyAsync(
    create.DomainId,
    tenantId,
    "domain-4-3-1-db-verify");

var verifiedDomain = await context.Domains
    .IgnoreQueryFilters()
    .SingleAsync(domain => domain.Id == create.DomainId);
var persistedStateBeforeCleanup = verifiedDomain.LifecycleState;

var auditActions = await context.DomainAuditEntries
    .IgnoreQueryFilters()
    .Where(audit => audit.DomainId == create.DomainId)
    .Select(audit => audit.Action)
    .ToListAsync();

var verificationRows = await context.DomainVerifications
    .IgnoreQueryFilters()
    .Where(verification => verification.DomainId == create.DomainId)
    .ToListAsync();

var delete = await writeService.DeleteDomainAsync(
    create.DomainId,
    new DeleteDomainRequest { Reason = "DOMAIN-4.3.1 runtime probe cleanup" },
    tenantId,
    actorUserId: null,
    correlationId: "domain-4-3-1-db-cleanup");

Console.WriteLine("PASS: existing database lifecycle probe completed.");
Console.WriteLine($"TenantId={tenantId}");
Console.WriteLine($"DomainId={create.DomainId}");
Console.WriteLine($"CreateState={create.LifecycleState}");
Console.WriteLine($"StartState={start.LifecycleState}");
Console.WriteLine($"VerificationStatus={verificationResult.Status}");
Console.WriteLine($"PersistedStateBeforeCleanup={persistedStateBeforeCleanup}");
Console.WriteLine($"CleanupState={delete.LifecycleState}");
Console.WriteLine($"ResolverCalls={resolver.TxtCalls}");
Console.WriteLine($"VerificationRows={verificationRows.Count}");
Console.WriteLine($"HasStartAudit={auditActions.Contains("DomainVerificationStarted")}");
Console.WriteLine($"HasVerifiedAudit={auditActions.Contains("DomainVerified")}");
Console.WriteLine($"VerifiedEvents={verificationEvents.PublishedEvents.OfType<DomainVerifiedEvent>().Count()}");
Console.WriteLine($"FailedEvents={verificationEvents.PublishedEvents.OfType<DomainVerificationFailedEvent>().Count()}");
return 0;

sealed class ProbeTenantContext(Guid tenantId) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
}

sealed class ProbeDnsResolver(string token) : IDnsResolver
{
    public int TxtCalls { get; private set; }

    public Task<DnsLookupResult> ResolveTxtAsync(string hostname, CancellationToken ct = default)
    {
        TxtCalls++;
        return Task.FromResult(DnsLookupResult.CreateSuccess(token, "probe", TimeSpan.FromMilliseconds(1), ttl: 60));
    }

    public Task<DnsLookupResult> ResolveCnameAsync(string hostname, CancellationToken ct = default) =>
        Task.FromResult(DnsLookupResult.CreateFailure("NXDOMAIN", "probe", TimeSpan.Zero));

    public Task<HttpLookupResult> ResolveHttpFileAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(HttpLookupResult.CreateFailure("HTTP_404", 404, TimeSpan.Zero));
}

sealed class RecordingEventPublisher : IEventPublisher
{
    public List<object> PublishedEvents { get; } = [];

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : IntegrationEvent
    {
        PublishedEvents.Add(@event);
        return Task.CompletedTask;
    }

    public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
        where T : IntegrationEvent
    {
        PublishedEvents.AddRange(events.Cast<object>());
        return Task.CompletedTask;
    }

    public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : IntegrationEvent, ISensitiveIntegrationEvent
    {
        PublishedEvents.Add(@event);
        return Task.CompletedTask;
    }
}
