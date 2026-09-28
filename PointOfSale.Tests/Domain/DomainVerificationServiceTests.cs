using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.CustomDomains;

public sealed class DomainVerificationServiceTests
{
    private const string Token = "shopixy-domain-verify-AbC123";
    private const string CorrelationId = "domain-4-3-correlation";

    [Fact]
    public async Task VerifyAsync_TxtSuccess_RequiresExactRecordAndMarksVerified()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(
                Token,
                "resolver-a",
                TimeSpan.FromMilliseconds(17),
                ttl: 300)
        };
        var publisher = new RecordingEventPublisher();
        var service = CreateService(context, resolver, publisher);

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Success, result.Status);
        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.Equal(1, result.AttemptNumber);
        Assert.Equal(1, resolver.TxtCalls);
        Assert.Equal(0, resolver.CnameCalls);
        Assert.Equal(0, resolver.HttpCalls);

        var savedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        Assert.Equal(DomainLifecycleState.Verified, savedDomain.LifecycleState);
        Assert.NotNull(savedDomain.VerifiedAtUtc);

        var verification = await context.DomainVerifications.IgnoreQueryFilters().SingleAsync(v => v.DomainId == domain.Id);
        Assert.Equal(VerificationStatus.Success, verification.Status);
        Assert.Equal(CorrelationId, verification.CorrelationId);
        Assert.Equal("resolver-a", verification.ResolverEndpoint);
        Assert.Equal(17, verification.LatencyMilliseconds);
        Assert.Equal(300, verification.Ttl);
        Assert.Equal(Token, verification.ResolvedValue);
        Assert.Equal(1, verification.Attempt);

        var audit = await context.DomainAuditEntries.IgnoreQueryFilters().SingleAsync(a => a.DomainId == domain.Id && a.Action == "DomainVerified");
        Assert.Equal(CorrelationId, audit.TraceId);
        Assert.Contains("\"attempt\":1", audit.AfterSnapshotJson);
        Assert.DoesNotContain(Token, audit.BeforeSnapshotJson ?? string.Empty);
        Assert.DoesNotContain(Token, audit.AfterSnapshotJson);

        Assert.Single(publisher.PublishedEvents.OfType<DomainVerifiedEvent>());
        Assert.Empty(publisher.PublishedEvents.OfType<DomainVerificationFailedEvent>());
    }

    [Fact]
    public async Task VerifyAsync_TxtMissing_ClassifiesRecordMissingAndKeepsPending()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateFailure(
                "NXDOMAIN",
                "resolver-b",
                TimeSpan.FromMilliseconds(12))
        };
        var publisher = new RecordingEventPublisher();
        var service = CreateService(context, resolver, publisher);

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Failed, result.Status);
        Assert.Equal(DomainVerificationFailureReasons.RecordMissing, result.FailureReason);
        Assert.True(result.RetryEligible);
        Assert.Equal(1, resolver.TxtCalls);

        var savedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        Assert.Equal(DomainLifecycleState.PendingVerification, savedDomain.LifecycleState);
        Assert.Null(savedDomain.VerifiedAtUtc);

        var verification = await context.DomainVerifications.IgnoreQueryFilters().SingleAsync(v => v.DomainId == domain.Id);
        Assert.Equal(DomainVerificationFailureReasons.RecordMissing, verification.FailureReason);

        var failedEvent = Assert.Single(publisher.PublishedEvents.OfType<DomainVerificationFailedEvent>());
        Assert.Equal(DomainVerificationFailureReasons.RecordMissing, failedEvent.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_TxtMismatch_IsCaseSensitive()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(
                Token.ToLowerInvariant(),
                "resolver-c",
                TimeSpan.FromMilliseconds(3),
                ttl: 120)
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Failed, result.Status);
        Assert.Equal(DomainVerificationFailureReasons.TokenMismatch, result.FailureReason);
        Assert.Equal(Token.ToLowerInvariant(), result.ResolvedValue);
        Assert.Equal(Token, result.ExpectedValue);
    }

    [Fact]
    public async Task VerifyAsync_TxtSuccess_AcceptsExactMatchFromAnyTxtRecord()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(
                "unrelated-record",
                "resolver-multiple",
                TimeSpan.FromMilliseconds(2),
                ttl: 60,
                values: new[] { "unrelated-record", Token })
        };

        var result = await CreateService(context, resolver, new RecordingEventPublisher())
            .VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Success, result.Status);
        Assert.Equal(Token, result.ResolvedValue);
    }

    [Fact]
    public async Task VerifyAsync_TxtQuotedPresentationValue_IsNotAcceptedAsToken()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(
                $" \"{Token}\" ",
                "resolver-quoted",
                TimeSpan.FromMilliseconds(2),
                ttl: 60)
        };

        var result = await CreateService(context, resolver, new RecordingEventPublisher())
            .VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Failed, result.Status);
        Assert.Equal(DomainVerificationFailureReasons.TokenMismatch, result.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_CnameSuccess_RequiresCanonicalTargetAndTargetTxtProof()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var cnameTarget = "verify-token.domains.shopixy.com";
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsCname, token: cnameTarget);
        var resolver = new FakeDnsResolver
        {
            CnameResult = DnsLookupResult.CreateSuccess(
                " VERIFY-TOKEN.DOMAINS.SHOPIXY.COM. ",
                "resolver-cname",
                TimeSpan.FromMilliseconds(8),
                ttl: 180),
            TxtResult = DnsLookupResult.CreateSuccess(
                cnameTarget,
                "resolver-cname-target",
                TimeSpan.FromMilliseconds(4),
                ttl: 180),
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Success, result.Status);
        Assert.Equal(cnameTarget, result.ExpectedValue);
        Assert.Equal(cnameTarget, result.ResolvedValue);
        Assert.Equal(1, resolver.TxtCalls);
        Assert.Equal(1, resolver.CnameCalls);
        Assert.Equal(0, resolver.HttpCalls);
    }

    [Fact]
    public async Task VerifyAsync_HttpSuccess_TrimsBodyAndRequiresResolverHttpEndpoint()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.HttpFile);
        var resolver = new FakeDnsResolver
        {
            HttpResult = HttpLookupResult.CreateSuccess(
                $" {Token}\r\n",
                TimeSpan.FromMilliseconds(25))
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Success, result.Status);
        Assert.Equal(Token, result.ResolvedValue);
        Assert.Equal(0, resolver.TxtCalls);
        Assert.Equal(0, resolver.CnameCalls);
        Assert.Equal(1, resolver.HttpCalls);
        Assert.Equal($"https://{domain.NormalizedHostName}/.well-known/shopixy-domain-verification.txt", resolver.LastHttpUrl);
    }

    [Theory]
    [InlineData(DomainLifecycleState.Archived, DomainType.Alias)]
    [InlineData(DomainLifecycleState.Deleted, DomainType.Alias)]
    [InlineData(DomainLifecycleState.Disabled, DomainType.Alias)]
    [InlineData(DomainLifecycleState.PendingVerification, DomainType.Primary)]
    public async Task VerifyAsync_LifecycleViolations_DoNotCallResolver(
        DomainLifecycleState state,
        DomainType domainType)
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt, state: state, domainType: domainType);
        var resolver = new FakeDnsResolver();
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Failed, result.Status);
        Assert.Equal(DomainVerificationFailureReasons.LifecycleViolation, result.FailureReason);
        Assert.False(result.RetryEligible);
        Assert.Equal(0, resolver.TotalCalls);

        var verification = await context.DomainVerifications.IgnoreQueryFilters().SingleAsync(v => v.DomainId == domain.Id);
        Assert.Equal(DomainVerificationFailureReasons.LifecycleViolation, verification.FailureReason);
    }

    [Fact]
    public async Task VerifyAsync_ExpiredToken_ClassifiesExpiredTokenAndSkipsResolver()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(
            context,
            tenantId,
            VerificationMethod.DnsTxt,
            createdAtUtc: DateTime.UtcNow.AddDays(-91),
            updatedAtUtc: DateTime.UtcNow.AddDays(-91));
        var resolver = new FakeDnsResolver();
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(DomainVerificationFailureReasons.ExpiredToken, result.FailureReason);
        Assert.False(result.RetryEligible);
        Assert.Equal(0, resolver.TotalCalls);
    }

    [Fact]
    public async Task VerifyAsync_OldDomainWithRecentPendingEntry_UsesEntryTimestamp()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(
            context,
            tenantId,
            VerificationMethod.DnsTxt,
            createdAtUtc: DateTime.UtcNow.AddDays(-120),
            updatedAtUtc: DateTime.UtcNow);
        domain.VerificationTokenIssuedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(Token, "resolver", TimeSpan.FromMilliseconds(1), ttl: 60)
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Success, result.Status);
        Assert.Equal(1, resolver.TotalCalls);
    }

    [Fact]
    public async Task VerifyAsync_MissingToken_FailsBeforeResolver()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt, token: string.Empty);
        var resolver = new FakeDnsResolver();
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Failed, result.Status);
        Assert.Equal(DomainVerificationFailureReasons.UnsupportedMethod, result.FailureReason);
        Assert.Equal(0, resolver.TotalCalls);
    }

    [Fact]
    public async Task VerifyAsync_CrossTenantAccess_ThrowsNotFoundAndSkipsResolverAndAudit()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var context = CreateContext(tenantA);
        var domain = AddDomain(context, tenantA, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver();
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        await Assert.ThrowsAsync<DomainNotFoundException>(() =>
            service.VerifyAsync(domain.Id, tenantB, CorrelationId));

        Assert.Equal(0, resolver.TotalCalls);
        Assert.Empty(await context.DomainVerifications.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await context.DomainAuditEntries.IgnoreQueryFilters().Where(a => a.DomainId == domain.Id).ToListAsync());
    }

    [Fact]
    public async Task VerifyAsync_ReplayAfterSuccess_DoesNotCallResolverAgain()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(Token, "resolver", TimeSpan.FromMilliseconds(1), ttl: 60)
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var first = await service.VerifyAsync(domain.Id, tenantId, "first");
        var second = await service.VerifyAsync(domain.Id, tenantId, "second");

        Assert.Equal(VerificationStatus.Success, first.Status);
        Assert.Equal(VerificationStatus.Success, second.Status);
        Assert.Null(second.FailureReason);
        Assert.Equal(1, resolver.TxtCalls);
        Assert.Equal(1, await context.DomainVerifications.IgnoreQueryFilters().CountAsync(v => v.DomainId == domain.Id));
    }

    [Fact]
    public async Task VerifyAsync_StaleVerifiedDomain_RotatesTokenAndRetainsServingAuthorityOnFailure()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(
            context,
            tenantId,
            VerificationMethod.DnsTxt,
            state: DomainLifecycleState.SslActive);
        var originalToken = domain.VerificationToken;
        var originalVerifiedAt = DateTime.UtcNow.AddDays(-91);
        domain.VerifiedAtUtc = originalVerifiedAt;
        domain.VerificationTokenIssuedAtUtc = originalVerifiedAt.AddDays(-1);
        await context.SaveChangesAsync();
        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateFailure("NXDOMAIN", "resolver-reverify", TimeSpan.FromMilliseconds(1))
        };

        var result = await CreateService(context, resolver, new RecordingEventPublisher())
            .VerifyAsync(domain.Id, tenantId, "reverify-correlation");

        var saved = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        Assert.Equal(VerificationStatus.Failed, result.Status);
        Assert.Equal(DomainLifecycleState.SslActive, saved.LifecycleState);
        Assert.Equal(originalVerifiedAt, saved.VerifiedAtUtc);
        Assert.NotEqual(originalToken, saved.VerificationToken);
        Assert.True(saved.VerificationTokenIssuedAtUtc > saved.VerifiedAtUtc);
        Assert.Equal(1, await context.DomainAuditEntries.CountAsync(a =>
            a.DomainId == domain.Id && a.Action == "DomainReverificationStarted"));
        Assert.Equal(1, await context.DomainAuditEntries.CountAsync(a =>
            a.DomainId == domain.Id && a.Action == "DomainReverificationFailed"));
    }

    [Fact]
    public async Task VerifyAsync_FifthFailedAttempt_IsNotRetryEligible()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);

        for (var i = 1; i <= 4; i++)
        {
            context.DomainVerifications.Add(new DomainVerification
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                Method = VerificationMethod.DnsTxt,
                Token = Token,
                Status = VerificationStatus.Failed,
                AttemptedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                FailureReason = DomainVerificationFailureReasons.RecordMissing,
                Attempt = i,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                IsDeleted = false,
            });
        }
        await context.SaveChangesAsync();

        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateFailure("NXDOMAIN", "resolver", TimeSpan.FromMilliseconds(1))
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(5, result.AttemptNumber);
        Assert.False(result.RetryEligible);
    }

    [Fact]
    public async Task VerifyAsync_RateLimitedBeforeResolver_DoesNotCallResolver()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);

        for (var i = 1; i <= 5; i++)
        {
            context.DomainVerifications.Add(new DomainVerification
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                Method = VerificationMethod.DnsTxt,
                Token = Token,
                Status = VerificationStatus.Failed,
                AttemptedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                FailureReason = DomainVerificationFailureReasons.RecordMissing,
                Attempt = i,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                IsDeleted = false,
            });
        }
        await context.SaveChangesAsync();

        var resolver = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(Token, "resolver", TimeSpan.FromMilliseconds(1), ttl: 60)
        };
        var publisher = new RecordingEventPublisher();
        var service = CreateService(context, resolver, publisher);

        var result = await service.VerifyAsync(domain.Id, tenantId, CorrelationId);

        Assert.Equal(VerificationStatus.Failed, result.Status);
        Assert.Equal(DomainVerificationFailureReasons.RateLimited, result.FailureReason);
        Assert.False(result.RetryEligible);
        Assert.True(result.RetryAfterUtc > DateTime.UtcNow);
        Assert.Equal(0, resolver.TotalCalls);
        Assert.Equal(5, await context.DomainVerifications.IgnoreQueryFilters().CountAsync(v => v.DomainId == domain.Id));
        Assert.Single(await context.DomainAuditEntries
            .Where(entry => entry.DomainId == domain.Id && entry.Action == "DomainVerificationRateLimited")
            .ToListAsync());
        var rateLimitedEvent = Assert.Single(publisher.PublishedEvents.OfType<DomainVerificationFailedEvent>());
        Assert.Equal(DomainVerificationFailureReasons.RateLimited, rateLimitedEvent.FailureReason);
        Assert.False(rateLimitedEvent.RetryEligible);
    }

    [Fact]
    public async Task VerifyAsync_UnsupportedFutureMethod_ThrowsNotSupported()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, (VerificationMethod)999);
        var resolver = new FakeDnsResolver();
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.VerifyAsync(domain.Id, tenantId, CorrelationId));

        Assert.Equal(0, resolver.TotalCalls);
        Assert.Empty(await context.DomainVerifications.IgnoreQueryFilters().Where(v => v.DomainId == domain.Id).ToListAsync());
    }

    [Fact]
    public async Task VerifyAsync_CancellationDuringResolver_LeavesDurableClaimForRecovery()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.DnsTxt);
        var resolver = new FakeDnsResolver
        {
            ThrowCancellationFromTxt = true
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.VerifyAsync(domain.Id, tenantId, CorrelationId));

        var claim = await context.DomainVerifications.IgnoreQueryFilters().SingleAsync(v => v.DomainId == domain.Id);
        Assert.Equal(VerificationStatus.Pending, claim.Status);
        Assert.Equal("durable-claim", claim.ResolverEndpoint);
        Assert.Empty(await context.DomainAuditEntries.IgnoreQueryFilters().Where(a => a.DomainId == domain.Id).ToListAsync());
    }

    [Fact]
    public async Task VerifyAsync_SequentialConcurrentRegression_OnlyFirstSuccessfulAttemptTransitionsState()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateContext(tenantId);
        var domain = AddDomain(context, tenantId, VerificationMethod.HttpFile);
        var resolver = new FakeDnsResolver
        {
            HttpResult = HttpLookupResult.CreateSuccess(Token, TimeSpan.FromMilliseconds(1))
        };
        var service = CreateService(context, resolver, new RecordingEventPublisher());

        var first = await service.VerifyAsync(domain.Id, tenantId, "concurrency-1");
        var second = await service.VerifyAsync(domain.Id, tenantId, "concurrency-2");

        Assert.Equal(VerificationStatus.Success, first.Status);
        Assert.Equal(VerificationStatus.Success, second.Status);
        Assert.Null(second.FailureReason);
        Assert.Equal(1, resolver.HttpCalls);

        var savedDomain = await context.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domain.Id);
        Assert.Equal(DomainLifecycleState.Verified, savedDomain.LifecycleState);
        Assert.Equal(1, await context.DomainVerifications.IgnoreQueryFilters().CountAsync(v => v.DomainId == domain.Id));
    }

    [Fact]
    public async Task VerifyAsync_ConcurrentSuccess_ProducesOneVerifiedTransitionAndOneVerifiedEvent()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = $"DomainVerificationConcurrent_{Guid.NewGuid():N}";
        var databaseRoot = SharedInMemoryDatabaseRoot.Instance;

        using (var seedContext = CreateContext(tenantId, databaseName, databaseRoot))
        {
            AddDomain(seedContext, tenantId, VerificationMethod.DnsTxt);
        }

        using var contextA = CreateContext(tenantId, databaseName, databaseRoot);
        using var contextB = CreateContext(tenantId, databaseName, databaseRoot);
        var domainId = await contextA.Domains.IgnoreQueryFilters().Select(d => d.Id).SingleAsync();

        var resolverA = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(Token, "resolver-a", TimeSpan.FromMilliseconds(1), ttl: 60)
        };
        var resolverB = new FakeDnsResolver
        {
            TxtResult = DnsLookupResult.CreateSuccess(Token, "resolver-b", TimeSpan.FromMilliseconds(1), ttl: 60)
        };
        var publisherA = new RecordingEventPublisher();
        var publisherB = new RecordingEventPublisher();
        var serviceA = CreateService(contextA, resolverA, publisherA);
        var serviceB = CreateService(contextB, resolverB, publisherB);

        var results = await Task.WhenAll(
            serviceA.VerifyAsync(domainId, tenantId, "concurrent-a"),
            serviceB.VerifyAsync(domainId, tenantId, "concurrent-b"));

        Assert.Equal(2, results.Count(result => result.Status == VerificationStatus.Success));
        Assert.Equal(1, resolverA.TotalCalls + resolverB.TotalCalls);
        Assert.Equal(1,
            publisherA.PublishedEvents.OfType<DomainVerifiedEvent>().Count() +
            publisherB.PublishedEvents.OfType<DomainVerifiedEvent>().Count());

        using var assertContext = CreateContext(tenantId, databaseName, databaseRoot);
        var savedDomain = await assertContext.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domainId);
        Assert.Equal(DomainLifecycleState.Verified, savedDomain.LifecycleState);
        Assert.Equal(1, await assertContext.DomainVerifications.IgnoreQueryFilters().CountAsync(v => v.DomainId == domainId));
        Assert.Equal(1, await assertContext.DomainVerifications.IgnoreQueryFilters().CountAsync(v =>
            v.DomainId == domainId && v.Status == VerificationStatus.Success));
    }

    [Fact]
    public void DomainVerificationService_MustNotReferenceDnsClientOrSchedulerOrControllers()
    {
        var source = File.ReadAllText(Path.Combine(
            TestConfiguration.GetSolutionRoot(),
            "PointOfSale.API",
            "Services",
            "Storefront",
            "DomainVerificationService.cs"));

        Assert.DoesNotContain("DnsClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ILookupClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Hangfire", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
        Assert.Contains("IDnsResolver", source, StringComparison.Ordinal);
    }

    private static DomainVerificationService CreateService(
        ApplicationDbContext context,
        IDnsResolver resolver,
        IEventPublisher publisher)
    {
        return new DomainVerificationService(
            context,
            resolver,
            publisher,
            NullLogger<DomainVerificationService>.Instance);
    }

    private static ApplicationDbContext CreateContext(
        Guid tenantId,
        string? databaseName = null,
        InMemoryDatabaseRoot? databaseRoot = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (databaseRoot is null)
        {
            builder.UseInMemoryDatabase(databaseName ?? $"DomainVerificationServiceTests_{Guid.NewGuid():N}");
        }
        else
        {
            builder.UseInMemoryDatabase(databaseName ?? $"DomainVerificationServiceTests_{Guid.NewGuid():N}", databaseRoot);
        }

        var options = builder.Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Database.EnsureCreated();
        return context;
    }

    private static PointOfSale.Core.Entities.Domain AddDomain(
        ApplicationDbContext context,
        Guid tenantId,
        VerificationMethod method,
        string token = Token,
        DomainLifecycleState state = DomainLifecycleState.PendingVerification,
        DomainType domainType = DomainType.Alias,
        DateTime? createdAtUtc = null,
        DateTime? updatedAtUtc = null)
    {
        var domain = new PointOfSale.Core.Entities.Domain
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HostName = "verify.example.com",
            NormalizedHostName = $"verify-{Guid.NewGuid():N}.example.com",
            DomainType = domainType,
            LifecycleState = state,
            VerificationToken = token,
            VerificationMethod = method,
            RedirectType = RedirectType.None,
            CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow,
            IsDeleted = state == DomainLifecycleState.Deleted,
        };

        context.Domains.Add(domain);
        context.SaveChanges();
        return domain;
    }

    private sealed class FakeDnsResolver : IDnsResolver
    {
        public DnsLookupResult TxtResult { get; init; } =
            DnsLookupResult.CreateFailure("NXDOMAIN", "fake", TimeSpan.Zero);

        public DnsLookupResult CnameResult { get; init; } =
            DnsLookupResult.CreateFailure("NXDOMAIN", "fake", TimeSpan.Zero);

        public HttpLookupResult HttpResult { get; init; } =
            HttpLookupResult.CreateFailure("HTTP_404", 404, TimeSpan.Zero);

        public bool ThrowCancellationFromTxt { get; init; }
        public int TxtCalls { get; private set; }
        public int CnameCalls { get; private set; }
        public int HttpCalls { get; private set; }
        public int TotalCalls => TxtCalls + CnameCalls + HttpCalls;
        public string? LastHttpUrl { get; private set; }

        public Task<DnsLookupResult> ResolveTxtAsync(string hostname, CancellationToken ct = default)
        {
            TxtCalls++;
            if (ThrowCancellationFromTxt)
            {
                throw new OperationCanceledException(ct);
            }

            return Task.FromResult(TxtResult);
        }

        public Task<DnsLookupResult> ResolveCnameAsync(string hostname, CancellationToken ct = default)
        {
            CnameCalls++;
            return Task.FromResult(CnameResult);
        }

        public Task<HttpLookupResult> ResolveHttpFileAsync(string url, CancellationToken ct = default)
        {
            HttpCalls++;
            LastHttpUrl = url;
            return Task.FromResult(HttpResult);
        }
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> PublishedEvents { get; } = new();

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
}
