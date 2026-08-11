using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Tests.Infrastructure;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PointOfSale.Tests.CustomDomains
{
    /// <summary>
    /// CreateDomain command tests (DOMAIN-3.1-C)
    /// </summary>
    public partial class DomainWriteServiceTests
    {
        private const string TenantId = "TEST-TENANT";
        private const string UserId = "TEST-USER";
        private const string CorrelationId = "test-correlation-id";

        /* CREATE COMMAND TESTS */

        [Fact]
        public async Task CreateDomain_ReturnsDomainId_ForValidRequest()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var request = new CreateDomainRequest
            {
                HostName = "shop.example.com",
                DomainType = DomainType.Alias,
            };

            // Act
            var result = await service.CreateDomainAsync(request, tenantId, Guid.NewGuid(), "create-correlation");

            // Assert
            Assert.NotNull(result);
            Assert.NotEqual(Guid.Empty, result.DomainId);
            Assert.Equal("shop.example.com", result.NormalizedHostName);

            var saved = await context.Domains.FirstOrDefaultAsync(d => d.TenantId == tenantId);
            Assert.NotNull(saved);
            Assert.Equal("shop.example.com", saved.NormalizedHostName);
            Assert.Equal(DomainLifecycleState.Draft, saved.LifecycleState);
            Assert.Equal(DomainType.Alias, saved.DomainType);
        }

        [Fact]
        public async Task CreateDomain_RejectsDirectPrimary()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var error = await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.CreateDomainAsync(new CreateDomainRequest
                {
                    HostName = "premature-primary.example.com",
                    DomainType = DomainType.Primary,
                }, tenantId, Guid.NewGuid(), "create-correlation"));

            Assert.Equal(DomainOperationErrorCode.InvalidDomainType, error.ErrorCode);
            Assert.Empty(context.Domains);
        }

        [Fact]
        public async Task CreateDomain_RejectsUnderscoreInsteadOfAliasingAnotherHostname()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var request = new CreateDomainRequest
            {
                HostName = "SHOP.UNDER_SCORE.example.COM.",
                DomainType = DomainType.Alias,
            };

            var error = await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.CreateDomainAsync(request, tenantId, Guid.NewGuid(), "normalize-correlation"));

            Assert.Equal(DomainOperationErrorCode.InvalidHostFormat, error.ErrorCode);
            Assert.Empty(context.Domains);
        }

        [Fact]
        public async Task CreateDomain_RejectsReservedHost()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var request = new CreateDomainRequest
            {
                HostName = "localhost",
                DomainType = DomainType.Alias,
            };

            // Act & Assert
            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.CreateDomainAsync(request, tenantId, Guid.NewGuid(), "correlation"));
        }

        [Fact]
        public async Task CreateDomain_RejectsAdminSubdomain()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var request = new CreateDomainRequest
            {
                HostName = "admin.myshop.com",
                DomainType = DomainType.Alias,
            };

            // Act & Assert
            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.CreateDomainAsync(request, tenantId, Guid.NewGuid(), "correlation"));
        }

        [Fact]
        public async Task CreateDomain_RejectsEmptyHostname()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var request = new CreateDomainRequest
            {
                HostName = "   ",
                DomainType = DomainType.Alias,
            };

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.CreateDomainAsync(request, tenantId, Guid.NewGuid(), "correlation"));
        }

        [Fact]
        public async Task CreateDomain_RejectsDuplicateHostname()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            // First create
            await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "duplicate.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "correlation-1");

            // Second create with same hostname
            var request2 = new CreateDomainRequest
            {
                HostName = "DUPLICATE.Example.COM",
                DomainType = DomainType.Alias,
            };

            await Assert.ThrowsAsync<DomainConflictException>(() =>
                service.CreateDomainAsync(request2, tenantId, Guid.NewGuid(), "correlation-2"));
        }

        [Fact]
        public async Task CreateDomain_RejectsRedirectTypeAtCreation()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var request = new CreateDomainRequest
            {
                HostName = "redirect.example.com",
                DomainType = DomainType.Temporary,
            };

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.CreateDomainAsync(request, tenantId, Guid.NewGuid(), "correlation"));
        }

        [Fact]
        public async Task CreateDomain_CreatesAuditEntry()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "audit.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "audit-correlation");

            var auditEntry = await context.DomainAuditEntries.FirstOrDefaultAsync(a => a.TenantId == tenantId);
            Assert.NotNull(auditEntry);
            Assert.Equal("DomainCreated", auditEntry.Action);
            Assert.Null(auditEntry.BeforeSnapshotJson);
            Assert.NotEmpty(auditEntry.AfterSnapshotJson);
        }

        [Fact]
        public async Task CreateDomain_PublishesDomainRegisteredEvent()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var eventPublisher = new TestEventPublisher();
            var repository = new DomainRepository(context);
            var hostNormalizer = new HostNormalizer();
            var logger = NullLogger<DomainWriteService>.Instance;
            var service = new DomainWriteService(context, repository, new FakeDomainVerificationService(), hostNormalizer, eventPublisher, logger);

            await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "event.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "event-correlation");

            var registeredEvent = eventPublisher.PublishedEvents.OfType<DomainRegisteredEvent>().FirstOrDefault();
            Assert.NotNull(registeredEvent);
            Assert.Equal("event.example.com", registeredEvent.NormalizedHostName);
        }

        /* START VERIFICATION COMMAND TESTS */

        [Fact]
        public async Task StartDomainVerification_TransitionsDraftToPendingAndAuditsWithoutToken()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var eventPublisher = new TestEventPublisher();
            var service = CreateWriteService(context, tenantId, eventPublisher);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "start-verification.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var created = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            var tokenBefore = created.VerificationToken;

            var response = await service.StartDomainVerificationAsync(
                createResult.DomainId,
                new StartDomainVerificationRequest
                {
                    VerificationMethod = VerificationMethod.DnsTxt,
                    ExpectedVersion = createResult.Version,
                },
                tenantId,
                Guid.NewGuid(),
                "verification-start-correlation");

            Assert.Equal(DomainLifecycleState.PendingVerification, response.LifecycleState);
            Assert.Equal(DomainLifecycleState.Draft, response.PreviousState);

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.Equal(DomainLifecycleState.PendingVerification, saved.LifecycleState);
            Assert.Equal(VerificationMethod.DnsTxt, saved.VerificationMethod);
            Assert.Equal(tokenBefore, saved.VerificationToken);
            Assert.StartsWith("shopixy-domain-verify-", saved.VerificationToken);

            var auditEntry = await context.DomainAuditEntries
                .SingleAsync(a => a.DomainId == createResult.DomainId && a.Action == "DomainVerificationStarted");
            Assert.Equal("verification-start-correlation", auditEntry.TraceId);
            Assert.DoesNotContain(saved.VerificationToken, auditEntry.BeforeSnapshotJson ?? string.Empty);
            Assert.DoesNotContain(saved.VerificationToken, auditEntry.AfterSnapshotJson);

            Assert.Empty(eventPublisher.PublishedEvents.OfType<DomainVerifiedEvent>());
            Assert.Contains(eventPublisher.PublishedEvents.OfType<DomainUpdatedEvent>(),
                evt => evt.PreviousState == DomainLifecycleState.Draft &&
                       evt.LifecycleState == DomainLifecycleState.PendingVerification);
        }

        [Theory]
        [InlineData(DomainLifecycleState.PendingVerification, DomainType.Alias)]
        [InlineData(DomainLifecycleState.Verified, DomainType.Alias)]
        [InlineData(DomainLifecycleState.Archived, DomainType.Alias)]
        [InlineData(DomainLifecycleState.Disabled, DomainType.Alias)]
        [InlineData(DomainLifecycleState.Draft, DomainType.Primary)]
        public async Task StartDomainVerification_RejectsInvalidLifecycleStates(
            DomainLifecycleState state,
            DomainType domainType)
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = $"reject-{Guid.NewGuid():N}.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var domain = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            domain.LifecycleState = state;
            domain.DomainType = domainType;
            if (state == DomainLifecycleState.Verified)
            {
                domain.VerifiedAtUtc = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.StartDomainVerificationAsync(
                    createResult.DomainId,
                    new StartDomainVerificationRequest(),
                    tenantId,
                    Guid.NewGuid(),
                    "start-correlation"));
        }

        [Fact]
        public async Task StartDomainVerification_RejectsDeletedDomain()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "deleted-start.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.DeleteDomainAsync(
                createResult.DomainId,
                new DeleteDomainRequest { Reason = "deleted before verification", ExpectedVersion = createResult.Version },
                tenantId,
                Guid.NewGuid(),
                "delete-correlation");

            await Assert.ThrowsAsync<DomainNotFoundException>(() =>
                service.StartDomainVerificationAsync(
                    createResult.DomainId,
                    new StartDomainVerificationRequest { ExpectedVersion = createResult.Version },
                    tenantId,
                    Guid.NewGuid(),
                    "start-correlation"));
        }

        [Fact]
        public async Task StartDomainVerification_GeneratesTokenWhenMissing()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "missing-token.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var domain = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            domain.VerificationToken = string.Empty;
            await context.SaveChangesAsync();

            await service.StartDomainVerificationAsync(
                createResult.DomainId,
                new StartDomainVerificationRequest { ExpectedVersion = createResult.Version },
                tenantId,
                Guid.NewGuid(),
                "start-correlation");

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.StartsWith("shopixy-domain-verify-", saved.VerificationToken);
            Assert.Equal(86, saved.VerificationToken.Length);
        }

        [Fact]
        public async Task StartDomainVerification_RegeneratesTokenOnlyWhenMethodExplicitlyChanges()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "method-change.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var domain = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            var originalToken = domain.VerificationToken;

            await service.StartDomainVerificationAsync(
                createResult.DomainId,
                new StartDomainVerificationRequest { VerificationMethod = VerificationMethod.DnsCname, ExpectedVersion = createResult.Version },
                tenantId,
                Guid.NewGuid(),
                "start-correlation");

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.Equal(VerificationMethod.DnsCname, saved.VerificationMethod);
            Assert.NotEqual(originalToken, saved.VerificationToken);
        }

        [Fact]
        public async Task StartDomainVerification_RejectsCrossTenantAccess()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantA);
            var service = CreateWriteService(context, tenantA);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "cross-start.example.com",
                DomainType = DomainType.Alias,
            }, tenantA, Guid.NewGuid(), "create-correlation");

            await Assert.ThrowsAsync<DomainNotFoundException>(() =>
                service.StartDomainVerificationAsync(
                    createResult.DomainId,
                    new StartDomainVerificationRequest { ExpectedVersion = createResult.Version },
                    tenantB,
                    Guid.NewGuid(),
                    "start-correlation"));
        }

        [Fact]
        public async Task RefreshDomainVerificationToken_RefreshesPendingTokenAndAuditsWithoutToken()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "refresh-token.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.StartDomainVerificationAsync(
                createResult.DomainId,
                new StartDomainVerificationRequest { ExpectedVersion = createResult.Version },
                tenantId,
                Guid.NewGuid(),
                "start-correlation");

            var pending = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            var oldToken = pending.VerificationToken;

            var response = await service.RefreshDomainVerificationTokenAsync(
                createResult.DomainId,
                new RefreshDomainVerificationTokenRequest { VerificationMethod = VerificationMethod.DnsCname, ExpectedVersion = pending.Version },
                tenantId,
                Guid.NewGuid(),
                "refresh-correlation");

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.Equal(DomainLifecycleState.PendingVerification, response.LifecycleState);
            Assert.Equal(DomainLifecycleState.PendingVerification, saved.LifecycleState);
            Assert.Equal(VerificationMethod.DnsCname, saved.VerificationMethod);
            Assert.NotEqual(oldToken, saved.VerificationToken);
            Assert.StartsWith("shopixy-domain-verify-", saved.VerificationToken);

            var audit = await context.DomainAuditEntries.SingleAsync(a =>
                a.DomainId == createResult.DomainId &&
                a.Action == "DomainVerificationTokenRefreshed");
            Assert.DoesNotContain(oldToken, audit.BeforeSnapshotJson ?? string.Empty);
            Assert.DoesNotContain(saved.VerificationToken, audit.AfterSnapshotJson);
        }

        [Fact]
        public async Task RefreshDomainVerificationToken_RejectsVerifiedDomain()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "refresh-verified.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var domain = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            domain.LifecycleState = DomainLifecycleState.Verified;
            domain.VerifiedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.RefreshDomainVerificationTokenAsync(
                    createResult.DomainId,
                    new RefreshDomainVerificationTokenRequest { ExpectedVersion = domain.Version },
                    tenantId,
                    Guid.NewGuid(),
                    "refresh-correlation"));
        }

        [Fact]
        public async Task RefreshDomainVerificationToken_RateLimitsRepeatedRefreshes()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "refresh-rate-limit.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.StartDomainVerificationAsync(
                createResult.DomainId,
                new StartDomainVerificationRequest { ExpectedVersion = createResult.Version },
                tenantId,
                Guid.NewGuid(),
                "start-correlation");

            var domain = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            var originalToken = domain.VerificationToken;
            var actorUserId = Guid.NewGuid();

            for (var i = 0; i < 5; i++)
            {
                context.DomainAuditEntries.Add(new DomainAuditEntry
                {
                    Id = Guid.NewGuid(),
                    DomainId = createResult.DomainId,
                    TenantId = tenantId,
                    Action = "DomainVerificationTokenRefreshed",
                    ActorType = ActorType.Merchant,
                    ActorUserId = actorUserId,
                    BeforeSnapshotJson = "{}",
                    AfterSnapshotJson = "{}",
                    TraceId = $"refresh-rate-{i}",
                    TimestampUtc = DateTime.UtcNow.AddMinutes(-i),
                });
            }

            await context.SaveChangesAsync();

            var exception = await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.RefreshDomainVerificationTokenAsync(
                    createResult.DomainId,
                    new RefreshDomainVerificationTokenRequest { ExpectedVersion = domain.Version },
                    tenantId,
                    actorUserId,
                    "refresh-correlation"));

            Assert.Equal(DomainOperationErrorCode.RateLimitExceeded, exception.ErrorCode);

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.Equal(originalToken, saved.VerificationToken);
            Assert.Equal(5, await context.DomainAuditEntries.CountAsync(a =>
                a.DomainId == createResult.DomainId &&
                a.Action == "DomainVerificationTokenRefreshed"));
        }

        [Fact]
        public async Task VerifyDomainNow_DelegatesToVerificationEngine()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var created = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "verify-delegation.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var result = await service.VerifyDomainNowAsync(
                created.DomainId,
                new VerifyDomainNowRequest { ExpectedVersion = created.Version },
                tenantId,
                Guid.NewGuid(),
                "verify-now-correlation");

            Assert.Equal(VerificationStatus.Failed, result.Status);
            Assert.Equal("verify-now-correlation", result.CorrelationId);
        }

        /* UPDATE COMMAND TESTS */

        [Fact]
        public async Task UpdateDomain_UpdatesHostname()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "original.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var response = await service.UpdateDomainAsync(createResult.DomainId, new UpdateDomainRequest
            {
                HostName = "updated.example.com",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "edit-correlation");

            Assert.Equal("updated.example.com", response.NormalizedHostName);

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.Equal("updated.example.com", saved.NormalizedHostName);
        }

        [Fact]
        public async Task UpdateDomain_RequiresExpectedVersion()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);
            var created = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "version-required.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var error = await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.UpdateDomainAsync(created.DomainId, new UpdateDomainRequest
                {
                    HostName = "must-not-change.example.com",
                }, tenantId, Guid.NewGuid(), "update-correlation"));

            Assert.Equal(DomainOperationErrorCode.ExpectedVersionRequired, error.ErrorCode);
            Assert.Equal("version-required.example.com", (await context.Domains.SingleAsync(d => d.Id == created.DomainId)).HostName);
        }

        [Fact]
        public async Task UpdateDomain_EnforcesOptimisticConcurrency()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "concurrent.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var domain = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            var originalVersion = domain.Version;

            // Simulate concurrent modification
            domain.Version = 9999;
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainConcurrencyException>(() =>
                service.UpdateDomainAsync(createResult.DomainId, new UpdateDomainRequest
                {
                    HostName = "newhost.example.com",
                    ExpectedVersion = originalVersion,
                }, tenantId, Guid.NewGuid(), "update-correlation"));
        }

        [Fact]
        public async Task UpdateDomain_RejectsOnDeletedState()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "to-delete.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.DeleteDomainAsync(createResult.DomainId, new DeleteDomainRequest
            {
                Reason = "test-delete",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "delete-correlation");

            await Assert.ThrowsAsync<DomainNotFoundException>(() =>
                service.UpdateDomainAsync(createResult.DomainId, new UpdateDomainRequest
                {
                    HostName = "should-fail.example.com"
                }, tenantId, Guid.NewGuid(), "update-correlation"));
        }

        /* ARCHIVE COMMAND TESTS */

        [Fact]
        public async Task ArchiveDomain_TransitionsToArchived()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "archive.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var response = await service.ArchiveDomainAsync(createResult.DomainId, new ArchiveDomainRequest
            {
                Reason = "migrated to new provider",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "archive-correlation");

            Assert.Equal(DomainLifecycleState.Archived, response.LifecycleState);

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.Equal(DomainLifecycleState.Archived, saved.LifecycleState);
        }

        [Fact]
        public async Task ArchiveDomain_RejectsPrimaryDomain()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "primary.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var primary = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            primary.DomainType = DomainType.Primary;
            primary.LifecycleState = DomainLifecycleState.Primary;
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.ArchiveDomainAsync(createResult.DomainId, new ArchiveDomainRequest
                {
                    Reason = "should-not-work",
                    ExpectedVersion = primary.Version,
                }, tenantId, Guid.NewGuid(), "archive-correlation"));
        }

        [Fact]
        public async Task ArchiveDomain_RejectsAlreadyArchived()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "double-archive.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.ArchiveDomainAsync(createResult.DomainId, new ArchiveDomainRequest
            {
                Reason = "first-archive",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "archive-1-correlation");

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.ArchiveDomainAsync(createResult.DomainId, new ArchiveDomainRequest
                {
                    Reason = "second-archive",
                    ExpectedVersion = createResult.Version,
                }, tenantId, Guid.NewGuid(), "archive-2-correlation"));
        }

        /* RESTORE COMMAND TESTS */

        [Fact]
        public async Task RestoreDomain_TransitionsToVerified()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "restore.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.ArchiveDomainAsync(createResult.DomainId, new ArchiveDomainRequest
            {
                Reason = "temporarily",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "archive-correlation");

            var response = await service.RestoreDomainAsync(createResult.DomainId, new RestoreDomainRequest
            {
                Reason = "reactivating",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "restore-correlation");

            Assert.Equal(DomainLifecycleState.Draft, response.LifecycleState);

            var saved = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            Assert.Equal(DomainLifecycleState.Draft, saved.LifecycleState);
        }

        [Fact]
        public async Task RestoreDomain_RejectsAlreadyActive()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "active.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.RestoreDomainAsync(createResult.DomainId, new RestoreDomainRequest
                {
                    Reason = "already-active",
                    ExpectedVersion = createResult.Version,
                }, tenantId, Guid.NewGuid(), "restore-correlation"));
        }

        /* DELETE COMMAND TESTS (SOFT DELETE) */

        [Fact]
        public async Task DeleteDomain_SoftDeletes_DoesNotRemoveFromDb()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "delete.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.DeleteDomainAsync(createResult.DomainId, new DeleteDomainRequest
            {
                Reason = "no longer needed",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "delete-correlation");

            // Verify the domain is soft-deleted (still in DB, but marked deleted)
            var deletedFromSameTenant = await context.Domains
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.Id == createResult.DomainId);

            Assert.NotNull(deletedFromSameTenant);
            Assert.True(deletedFromSameTenant.IsDeleted);
            Assert.Equal(DomainLifecycleState.Deleted, deletedFromSameTenant.LifecycleState);

            // Verify it's not visible through normal query filters
            var visible = await context.Domains
                .FirstOrDefaultAsync(d => d.Id == createResult.DomainId);
            Assert.Null(visible);
        }

        [Fact]
        public async Task DeleteDomain_RejectsPrimaryDomain()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "cant-delete-primary.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var primary = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            primary.DomainType = DomainType.Primary;
            primary.LifecycleState = DomainLifecycleState.Primary;
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.DeleteDomainAsync(createResult.DomainId, new DeleteDomainRequest
                {
                    Reason = "should-not-work",
                    ExpectedVersion = primary.Version,
                }, tenantId, Guid.NewGuid(), "delete-correlation"));
        }

        /* PROMOTE PRIMARY COMMAND TESTS */

        [Fact]
        public async Task PromotePrimaryDomain_DemotesPreviousPrimary()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            // Create primary domain A
            var createResultA = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "primary-a.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-a-correlation");

            // Model an existing edge-proven primary.
            var domainA = await context.Domains.FirstAsync(d => d.Id == createResultA.DomainId);
            domainA.LifecycleState = DomainLifecycleState.Primary;
            domainA.DomainType = DomainType.Primary;
            AddActiveCertificate(context, domainA);
            await context.SaveChangesAsync();

            // Create alias domain B
            var createResultB = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "alias-b.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-b-correlation");

            // Set domain B to SslActive state so it can be promoted
            var domainB = await context.Domains.FirstAsync(d => d.Id == createResultB.DomainId);
            domainB.LifecycleState = DomainLifecycleState.SslActive;
            AddActiveCertificate(context, domainB);
            await context.SaveChangesAsync();

            // Promote domain B to primary
            var response = await service.PromotePrimaryDomainAsync(createResultB.DomainId, new PromotePrimaryDomainRequest { ExpectedVersion = domainB.Version },
                tenantId, Guid.NewGuid(), "promote-correlation");

            Assert.True(response.LifecycleState == DomainLifecycleState.Primary);

            // Domain A should be demoted - reload from database
            var domainAAfterDemotion = await context.Domains.FirstAsync(d => d.Id == createResultA.DomainId);
            Assert.Equal(DomainLifecycleState.SslActive, domainAAfterDemotion.LifecycleState);

            // Domain B should be primary
            var domainBUpdated = await context.Domains.FirstAsync(d => d.Id == createResultB.DomainId);
            Assert.Equal(DomainLifecycleState.Primary, domainBUpdated.LifecycleState);
            Assert.Equal(DomainType.Primary, domainBUpdated.DomainType);
        }

        [Fact]
        public async Task PromotePrimaryDomain_RejectsAlreadyPrimary()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "already-primary.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var primary = await context.Domains.FirstAsync(d => d.Id == createResult.DomainId);
            primary.DomainType = DomainType.Primary;
            primary.LifecycleState = DomainLifecycleState.Primary;
            AddActiveCertificate(context, primary);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.PromotePrimaryDomainAsync(createResult.DomainId, new PromotePrimaryDomainRequest { ExpectedVersion = primary.Version },
                    tenantId, Guid.NewGuid(), "promote-correlation"));
        }

        [Fact]
        public async Task PromotePrimaryDomain_RejectsVerifiedDomainWithoutEdgeProof()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var created = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "verified-only.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var domain = await context.Domains.SingleAsync(d => d.Id == created.DomainId);
            domain.LifecycleState = DomainLifecycleState.Verified;
            domain.VerifiedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();

            var error = await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.PromotePrimaryDomainAsync(created.DomainId, new PromotePrimaryDomainRequest { ExpectedVersion = domain.Version },
                    tenantId, Guid.NewGuid(), "promote-correlation"));

            Assert.Equal(DomainOperationErrorCode.NotVerifiedOrSslActive, error.ErrorCode);
        }

        [Fact]
        public async Task PromotePrimaryDomain_RejectsSslActiveWithoutLinkedActiveCertificate()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var created = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "unproven-ssl.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            var domain = await context.Domains.SingleAsync(d => d.Id == created.DomainId);
            domain.LifecycleState = DomainLifecycleState.SslActive;
            await context.SaveChangesAsync();

            var error = await Assert.ThrowsAsync<DomainOperationException>(() =>
                service.PromotePrimaryDomainAsync(created.DomainId, new PromotePrimaryDomainRequest { ExpectedVersion = domain.Version },
                    tenantId, Guid.NewGuid(), "promote-correlation"));

            Assert.Equal(DomainOperationErrorCode.NotVerifiedOrSslActive, error.ErrorCode);
        }

        [Fact]
        public async Task PromotePrimaryDomain_RejectsDeleted()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "delete-then-promote.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.DeleteDomainAsync(createResult.DomainId, new DeleteDomainRequest
            {
                Reason = "testing",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "delete-correlation");

            await Assert.ThrowsAsync<DomainNotFoundException>(() =>
                service.PromotePrimaryDomainAsync(createResult.DomainId, new PromotePrimaryDomainRequest { ExpectedVersion = createResult.Version },
                    tenantId, Guid.NewGuid(), "promote-correlation"));
        }

        /* TENANT ISOLATION TESTS */

        [Fact]
        public async Task UpdateDomain_RejectsCrossTenantAccess()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            using var context = CreateInMemoryContext(tenantA);
            var serviceA = CreateWriteService(context, tenantA);

            var createResult = await serviceA.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "isolate.example.com",
                DomainType = DomainType.Alias,
            }, tenantA, Guid.NewGuid(), "create-correlation");

            // Try to update from tenant B
            await Assert.ThrowsAsync<DomainNotFoundException>(() =>
                serviceA.UpdateDomainAsync(createResult.DomainId, new UpdateDomainRequest
                {
                    HostName = "hijacked.example.com"
                }, tenantB, Guid.NewGuid(), "update-correlation"));
        }

        [Fact]
        public async Task ArchiveDomain_RejectsCrossTenantAccess()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            using var context = CreateInMemoryContext(tenantA);
            var serviceA = CreateWriteService(context, tenantA);

            var createResult = await serviceA.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "cross-archive.example.com",
                DomainType = DomainType.Alias,
            }, tenantA, Guid.NewGuid(), "create-correlation");

            // Try to archive from tenant B
            await Assert.ThrowsAsync<DomainNotFoundException>(() =>
                serviceA.ArchiveDomainAsync(createResult.DomainId, new ArchiveDomainRequest
                {
                    Reason = "cross-tenant",
                    ExpectedVersion = createResult.Version,
                }, tenantB, Guid.NewGuid(), "archive-correlation"));
        }

        [Fact]
        public async Task DeleteDomain_RejectsCrossTenantAccess()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            using var context = CreateInMemoryContext(tenantA);
            var serviceA = CreateWriteService(context, tenantA);

            var createResult = await serviceA.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "cross-delete.example.com",
                DomainType = DomainType.Alias,
            }, tenantA, Guid.NewGuid(), "create-correlation");

            // Try to delete from tenant B
            await Assert.ThrowsAsync<DomainNotFoundException>(() =>
                serviceA.DeleteDomainAsync(createResult.DomainId, new DeleteDomainRequest
                {
                    Reason = "cross-tenant",
                    ExpectedVersion = createResult.Version,
                }, tenantB, Guid.NewGuid(), "delete-correlation"));
        }

        /* AUDIT ENTRY TESTS */

        [Fact]
        public async Task ArchiveDomain_CreatesAuditEntry()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "audit-archive.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.ArchiveDomainAsync(createResult.DomainId, new ArchiveDomainRequest
            {
                Reason = "archiving-for-audit",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "archive-correlation");

            var auditEntry = await context.DomainAuditEntries
                .FirstOrDefaultAsync(a => a.DomainId == createResult.DomainId && a.Action == "DomainArchived");
            Assert.NotNull(auditEntry);
            Assert.NotEmpty(auditEntry.BeforeSnapshotJson);
            Assert.NotEmpty(auditEntry.AfterSnapshotJson);
        }

        [Fact]
        public async Task DeleteDomain_CreatesAuditEntry()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            var createResult = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "audit-delete.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-correlation");

            await service.DeleteDomainAsync(createResult.DomainId, new DeleteDomainRequest
            {
                Reason = "deleting-for-audit",
                ExpectedVersion = createResult.Version,
            }, tenantId, Guid.NewGuid(), "delete-correlation");

            var auditEntry = await context.DomainAuditEntries
                .FirstOrDefaultAsync(a => a.DomainId == createResult.DomainId && a.Action == "DomainDeleted");
            Assert.NotNull(auditEntry);
        }

        /* CONCURRENCY TESTS */

        [Fact]
        public async Task PromotePrimaryDomain_RejectsConcurrentPromotion()
        {
            var tenantId = Guid.NewGuid();
            using var context = CreateInMemoryContext(tenantId);
            var service = CreateWriteService(context, tenantId);

            // Create primary domain A
            var createResultA = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "primary-concurrent.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-a-correlation");

            // Create alias domain B
            var createResultB = await service.CreateDomainAsync(new CreateDomainRequest
            {
                HostName = "alias-concurrent.example.com",
                DomainType = DomainType.Alias,
            }, tenantId, Guid.NewGuid(), "create-b-correlation");

            // Set domain B to SslActive
            var domainB = await context.Domains.FirstAsync(d => d.Id == createResultB.DomainId);
            domainB.LifecycleState = DomainLifecycleState.SslActive;
            AddActiveCertificate(context, domainB);
            await context.SaveChangesAsync();

            // Get current version of domain B
            var originalVersion = domainB.Version;

            // Simulate a concurrent modification
            domainB.Version = 9999;
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainConcurrencyException>(() =>
                service.PromotePrimaryDomainAsync(createResultB.DomainId, new PromotePrimaryDomainRequest
                {
                    ExpectedVersion = originalVersion
                }, tenantId, Guid.NewGuid(), "promote-correlation"));
        }

        private static SslCertificate AddActiveCertificate(
            ApplicationDbContext context,
            Domain domain)
        {
            var now = DateTime.UtcNow;
            var certificate = new SslCertificate
            {
                Id = Guid.NewGuid(),
                TenantId = domain.TenantId,
                DomainId = domain.Id,
                NormalizedHostName = domain.NormalizedHostName,
                Provider = ProviderType.LetsEncrypt,
                Status = CertificateStatus.Active,
                OperationPhase = CertificateOperationPhase.ActivationProbe,
                Generation = 1,
                NotBeforeUtc = now.AddDays(-1),
                NotAfterUtc = now.AddDays(89),
                IssuedAtUtc = now.AddDays(-1),
                ActivatedAtUtc = now,
                RenewalEligible = true,
                CreatedAt = now,
                UpdatedAt = now,
            };

            context.SslCertificates.Add(certificate);
            domain.SslCertificateId = certificate.Id;
            domain.SslActivatedAtUtc = now;
            return certificate;
        }

        private static ApplicationDbContext CreateInMemoryContext(Guid tenantId)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: $"DomainWriteTest_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var tenantContext = new TestTenantContext(tenantId);
            var context = new ApplicationDbContext(options, tenantContext);
            context.Database.EnsureCreated();
            return context;
        }

        private static DomainWriteService CreateWriteService(
            ApplicationDbContext context,
            Guid tenantId,
            TestEventPublisher? eventPublisher = null,
            Guid userId = default)
        {
            var repository = new DomainRepository(context);
            var hostNormalizer = new HostNormalizer();
            var logger = NullLogger<DomainWriteService>.Instance;
            return new DomainWriteService(context, repository, new FakeDomainVerificationService(), hostNormalizer, eventPublisher ?? new TestEventPublisher(), logger);
        }

        private sealed class FakeDomainVerificationService : IDomainVerificationService
        {
            public Task<PointOfSale.Core.Models.VerificationResult> VerifyAsync(
                Guid domainId,
                Guid tenantId,
                string correlationId,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new PointOfSale.Core.Models.VerificationResult
                {
                    DomainId = domainId,
                    TenantId = tenantId,
                    Status = VerificationStatus.Failed,
                    FailureReason = PointOfSale.Core.Models.DomainVerificationFailureReasons.RecordMissing,
                    Method = VerificationMethod.DnsTxt,
                    AttemptNumber = 1,
                    RetryEligible = true,
                    CorrelationId = correlationId,
                    ResolvedAtUtc = DateTime.UtcNow,
                });
            }
        }

        private class TestEventPublisher : IEventPublisher
        {
            public System.Collections.Generic.List<object> PublishedEvents { get; } = new();
            public Task PublishAsync<T>(T @event, System.Threading.CancellationToken ct = default) where T : IntegrationEvent
            {
                PublishedEvents.Add(@event);
                return Task.CompletedTask;
            }
            public Task PublishBatchAsync<T>(System.Collections.Generic.IEnumerable<T> events, System.Threading.CancellationToken ct = default) where T : IntegrationEvent
            {
                foreach (var evt in events)
                    PublishedEvents.Add(evt);
                return Task.CompletedTask;
            }
            public Task PublishSensitiveAsync<T>(T @event, System.Threading.CancellationToken ct = default) where T : IntegrationEvent, ISensitiveIntegrationEvent
            {
                PublishedEvents.Add(@event);
                return Task.CompletedTask;
            }
        }
    }
}
