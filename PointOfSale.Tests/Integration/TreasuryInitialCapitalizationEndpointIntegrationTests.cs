using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Controllers;
using PointOfSale.API.Infrastructure.Mvc;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class TreasuryInitialCapitalizationEndpointIntegrationTests
{
    [Fact]
    public async Task OwnerGet_ReturnsMaterializedDestinationAndHistoryDtos()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();

        using var destinations = await fixture.GetAsync(
            "api/treasury/initial-capitalizations/destinations",
            "capitalization-destinations");
        Assert.Equal(HttpStatusCode.OK, destinations.StatusCode);
        using var destinationBody = await ReadJsonAsync(destinations);
        var destinationRows = destinationBody.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, destinationRows.Count);
        Assert.All(destinationRows, row =>
        {
            Assert.Contains(row.GetProperty("type").GetString(), new[] { "Cashbox", "Bank" });
            Assert.Equal(0m, row.GetProperty("currentBalance").GetDecimal());
        });
        Assert.Contains(destinationRows, row => row.GetProperty("ledgerAccountCode").GetString() == "1010");
        Assert.Contains(destinationRows, row => row.GetProperty("ledgerAccountCode").GetString() == "1030");

        using var history = await fixture.GetAsync(
            "api/treasury/initial-capitalizations",
            "capitalization-history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var historyBody = await ReadJsonAsync(history);
        Assert.Equal(0, historyBody.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task OwnerPost_CommitsOneDocumentBalancedJournalAndProcessedOutbox()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        var operationId = Guid.NewGuid();

        using var response = await fixture.CreateAsync(operationId, 1_000m, "capitalization-create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var documentId = body.RootElement.GetProperty("id").GetGuid();
        var journalId = body.RootElement.GetProperty("postedJournalId").GetGuid();
        Assert.Equal("Posted", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("3010", body.RootElement.GetProperty("openingEquity").GetProperty("accountCode").GetString());
        Assert.Equal("capitalization-create", body.RootElement.GetProperty("traceId").GetString());

        var snapshot = await fixture.CaptureAsync();
        Assert.Equal(1, snapshot.Documents);
        Assert.Equal(1, snapshot.Allocations);
        Assert.Equal(1, snapshot.Journals);
        Assert.Equal(2, snapshot.LedgerLines);
        Assert.Equal(1, snapshot.Outbox);
        Assert.Equal(1, snapshot.ProcessedOutbox);
        Assert.Equal(1_000m, snapshot.CashCachedBalance);
        Assert.Equal(1_000m, snapshot.CashLedgerBalance);
        Assert.Equal(1_000m, snapshot.EquityCachedBalance);
        Assert.Equal(1_000m, snapshot.EquityLedgerBalance);
        await fixture.AssertBalancedJournalAsync(documentId, journalId, 1_000m);
    }

    [Fact]
    public async Task ExactReplay_ReturnsSameDocumentWithoutMutation()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        var operationId = Guid.NewGuid();
        using var first = await fixture.CreateAsync(operationId, 250m, "capitalization-replay-first");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstBody = await ReadJsonAsync(first);
        var documentId = firstBody.RootElement.GetProperty("id").GetGuid();
        var journalId = firstBody.RootElement.GetProperty("postedJournalId").GetGuid();
        var beforeReplay = await fixture.CaptureAsync();

        using var replay = await fixture.CreateAsync(operationId, 250m, "capitalization-replay-second");

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayBody = await ReadJsonAsync(replay);
        Assert.Equal(documentId, replayBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(journalId, replayBody.RootElement.GetProperty("postedJournalId").GetGuid());
        Assert.Equal(beforeReplay, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task ChangedPayloadWithCommittedOperation_ReturnsStableConflictWithoutMutation()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        var operationId = Guid.NewGuid();
        using var first = await fixture.CreateAsync(operationId, 250m, "capitalization-conflict-first");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var beforeConflict = await fixture.CaptureAsync();

        using var conflict = await fixture.CreateAsync(operationId, 251m, "capitalization-conflict-second");

        await AssertErrorAsync(conflict, HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT", "capitalization-conflict-second");
        Assert.Equal(beforeConflict, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task ConcurrentDifferentOperations_PostExactlyOnce()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();

        var responses = await Task.WhenAll(
            fixture.CreateAsync(Guid.NewGuid(), 600m, "capitalization-concurrent-a"),
            fixture.CreateAsync(Guid.NewGuid(), 700m, "capitalization-concurrent-b"));
        using var first = responses[0];
        using var second = responses[1];

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        using var conflictBody = await ReadJsonAsync(conflict);
        Assert.Contains(
            conflictBody.RootElement.GetProperty("code").GetString(),
            new[] { "INITIAL_CAPITALIZATION_ALREADY_EXISTS", "CONCURRENCY_CONFLICT" });
        var snapshot = await fixture.CaptureAsync();
        Assert.Equal(1, snapshot.Documents);
        Assert.Equal(1, snapshot.Journals);
        Assert.Equal(2, snapshot.LedgerLines);
        Assert.Equal(1, snapshot.Outbox);
        Assert.Equal(1, snapshot.ProcessedOutbox);
        Assert.Contains(snapshot.CashLedgerBalance, new[] { 600m, 700m });
        Assert.Equal(snapshot.CashLedgerBalance, snapshot.EquityLedgerBalance);
    }

    [Fact]
    public async Task ExistingTreasuryMovement_RejectsWithoutMutation()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync(withTreasuryMovement: true);
        var before = await fixture.CaptureAsync();

        using var response = await fixture.CreateAsync(Guid.NewGuid(), 100m, "capitalization-existing-movement");

        await AssertErrorAsync(
            response,
            HttpStatusCode.Conflict,
            "INITIAL_CAPITALIZATION_NOT_ELIGIBLE",
            "capitalization-existing-movement");
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task ForeignTreasuryDestination_RejectsWithoutMutation()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        var before = await fixture.CaptureAsync();

        using var response = await fixture.CreateAsync(
            Guid.NewGuid(),
            100m,
            "capitalization-foreign-destination",
            treasuryAccountId: fixture.ForeignTreasuryAccountId);

        await AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "INVALID_TREASURY_DESTINATION",
            "capitalization-foreign-destination");
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Cashier")]
    [InlineData("Warehouse")]
    [InlineData("SuperAdmin")]
    public async Task NonOwnerRole_CannotPost(string role)
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        var before = await fixture.CaptureAsync();

        using var response = await fixture.CreateAsync(
            Guid.NewGuid(),
            100m,
            $"capitalization-role-{role}",
            role: role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task OwnerClaimBackedByNonOwnerUser_IsRejectedAtServiceBoundary()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        var before = await fixture.CaptureAsync();

        using var response = await fixture.CreateAsync(
            Guid.NewGuid(),
            100m,
            "capitalization-service-role",
            userId: fixture.AdminUserId);

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "AUTHORIZATION_REQUIRED", "capitalization-service-role");
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task AnonymousRequest_CannotReadOrPost()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        using var client = fixture.CreateAnonymousClient();

        using var read = await client.GetAsync("api/treasury/initial-capitalizations");
        using var post = await fixture.CreateAnonymousAsync(Guid.NewGuid(), 100m);

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(EmptySnapshot, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task FullReversalAndReplacement_PreserveLineageAndNetBalances()
    {
        await using var fixture = await CapitalizationHttpFixture.StartAsync();
        var cutoverDate = DateTime.UtcNow.Date.AddDays(-1);
        using var created = await fixture.CreateAsync(Guid.NewGuid(), 400m, "capitalization-before-reversal", cutoverDate: cutoverDate);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdBody = await ReadJsonAsync(created);
        var originalId = createdBody.RootElement.GetProperty("id").GetGuid();
        var reversalOperationId = Guid.NewGuid();

        using var reversed = await fixture.ReverseAsync(
            originalId,
            reversalOperationId,
            "Correct opening cash count",
            "capitalization-reversal");
        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);
        using var reversedBody = await ReadJsonAsync(reversed);
        Assert.Equal("Reversed", reversedBody.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, reversedBody.RootElement.GetProperty("reversalJournalId").GetGuid());

        var afterReversal = await fixture.CaptureAsync();
        Assert.Equal(0m, afterReversal.CashLedgerBalance);
        Assert.Equal(0m, afterReversal.EquityLedgerBalance);
        Assert.Equal(2, afterReversal.Journals);
        Assert.Equal(4, afterReversal.LedgerLines);
        Assert.Equal(2, afterReversal.ProcessedOutbox);

        using var reversalReplay = await fixture.ReverseAsync(
            originalId,
            reversalOperationId,
            "Correct opening cash count",
            "capitalization-reversal-replay");
        Assert.Equal(HttpStatusCode.OK, reversalReplay.StatusCode);
        Assert.Equal(afterReversal, await fixture.CaptureAsync());

        using var replacement = await fixture.CreateAsync(
            Guid.NewGuid(),
            450m,
            "capitalization-replacement",
            predecessorId: originalId,
            cutoverDate: cutoverDate);
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        using var replacementBody = await ReadJsonAsync(replacement);
        Assert.Equal(originalId, replacementBody.RootElement.GetProperty("predecessorCapitalizationId").GetGuid());
        var final = await fixture.CaptureAsync();
        Assert.Equal(2, final.Documents);
        Assert.Equal(3, final.Journals);
        Assert.Equal(6, final.LedgerLines);
        Assert.Equal(450m, final.CashLedgerBalance);
        Assert.Equal(450m, final.EquityLedgerBalance);
    }

    private static readonly FinancialSnapshot EmptySnapshot = new(0, 0, 0, 0, 0, 0, 0m, 0m, 0m, 0m);

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string traceId)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(traceId, body.RootElement.GetProperty("traceId").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class CapitalizationHttpFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TestDatabaseScope _databaseScope;
        private readonly string _connectionString;

        private CapitalizationHttpFixture(
            WebApplication app,
            Uri baseAddress,
            TestDatabaseScope databaseScope,
            string connectionString,
            SeedIdentity seed)
        {
            _app = app;
            BaseAddress = baseAddress;
            _databaseScope = databaseScope;
            _connectionString = connectionString;
            TenantId = seed.TenantId;
            OwnerUserId = seed.OwnerUserId;
            AdminUserId = seed.AdminUserId;
            CashAccountId = seed.CashAccountId;
            EquityAccountId = seed.EquityAccountId;
            TreasuryAccountId = seed.TreasuryAccountId;
            ForeignTreasuryAccountId = seed.ForeignTreasuryAccountId;
        }

        private Uri BaseAddress { get; }
        private Guid TenantId { get; }
        private Guid OwnerUserId { get; }
        public Guid AdminUserId { get; }
        private Guid CashAccountId { get; }
        private Guid EquityAccountId { get; }
        private Guid TreasuryAccountId { get; }
        public Guid ForeignTreasuryAccountId { get; }

        public static async Task<CapitalizationHttpFixture> StartAsync(bool withTreasuryMovement = false)
        {
            var databaseScope = TestDatabaseScope.Open();
            try
            {
                await using var context = databaseScope.CreateDbContext();
                var connectionString = context.Database.GetDbConnection().ConnectionString;
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = Environments.Development
                });
                builder.WebHost.UseUrls($"http://127.0.0.1:{GetFreeTcpPort()}");
                builder.Logging.ClearProviders();
                builder.Services.AddLogging();
                builder.Services.AddScoped<ApiErrorResponseNormalizationFilter>();
                builder.Services.AddScoped<PreventEntityExposureFilter>();
                builder.Services.AddControllers(options =>
                    {
                        options.Filters.AddService<PreventEntityExposureFilter>();
                        options.Filters.AddService<ApiErrorResponseNormalizationFilter>();
                    })
                    .AddApplicationPart(typeof(TreasuryInitialCapitalizationsController).Assembly);
                builder.Services
                    .AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                builder.Services.AddAuthorization();
                builder.Services.AddScoped<MutableTenantContext>();
                builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<MutableTenantContext>());
                builder.Services.AddDbContext<ApplicationDbContext>((_, options) =>
                    options.UseNpgsql(connectionString)
                        .EnableDetailedErrors()
                        .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));
                builder.Services.AddScoped<IFinancialPostingService, FinancialPostingService>();
                builder.Services.AddScoped<ITreasuryInitialCapitalizationService, TreasuryInitialCapitalizationService>();

                var app = builder.Build();
                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseAuthentication();
                app.Use(async (httpContext, next) =>
                {
                    if (Guid.TryParse(httpContext.User.FindFirst("tenant_id")?.Value, out var tenantId))
                    {
                        httpContext.RequestServices.GetRequiredService<MutableTenantContext>().TenantId = tenantId;
                    }

                    await next();
                });
                app.UseMiddleware<GlobalExceptionMiddleware>();
                app.UseAuthorization();
                app.MapControllers();
                await app.StartAsync();

                var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                    .Features.Get<IServerAddressesFeature>()!
                    .Addresses.Single();
                var seed = await SeedAsync(connectionString, withTreasuryMovement);
                return new CapitalizationHttpFixture(app, new Uri(address), databaseScope, connectionString, seed);
            }
            catch
            {
                await databaseScope.DisposeAsync();
                throw;
            }
        }

        public Task<HttpResponseMessage> CreateAsync(
            Guid operationId,
            decimal amount,
            string traceId,
            Guid? treasuryAccountId = null,
            string role = "Owner",
            Guid? userId = null,
            Guid? predecessorId = null,
            DateTime? cutoverDate = null) =>
            SendCreateAsync(
                CreateClient(role, userId ?? OwnerUserId),
                operationId,
                amount,
                traceId,
                treasuryAccountId ?? TreasuryAccountId,
                predecessorId,
                cutoverDate);

        public Task<HttpResponseMessage> CreateAnonymousAsync(Guid operationId, decimal amount) =>
            SendCreateAsync(
                CreateAnonymousClient(),
                operationId,
                amount,
                "capitalization-anonymous",
                TreasuryAccountId,
                null,
                null);

        public async Task<HttpResponseMessage> ReverseAsync(
            Guid documentId,
            Guid operationId,
            string reason,
            string traceId)
        {
            using var client = CreateClient("Owner", OwnerUserId);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"api/treasury/initial-capitalizations/{documentId}/reverse");
            request.Headers.Add("X-Request-Id", traceId);
            request.Content = JsonContent.Create(new
            {
                operationId,
                reason,
                confirmed = true
            });
            return await client.SendAsync(request);
        }

        public HttpClient CreateAnonymousClient() => new() { BaseAddress = BaseAddress };

        public async Task<HttpResponseMessage> GetAsync(string path, string traceId)
        {
            using var client = CreateClient("Owner", OwnerUserId);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Request-Id", traceId);
            return await client.SendAsync(request);
        }

        public async Task<FinancialSnapshot> CaptureAsync()
        {
            await using var db = CreateDbContext();
            var cashCached = await AccountBalanceAsync(db, CashAccountId);
            var equityCached = await AccountBalanceAsync(db, EquityAccountId);
            var cashLedger = await LedgerBalanceAsync(db, CashAccountId, debitNormal: true);
            var equityLedger = await LedgerBalanceAsync(db, EquityAccountId, debitNormal: false);
            return new FinancialSnapshot(
                await db.TreasuryInitialCapitalizations.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.TreasuryInitialCapitalizationAllocations.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.JournalEntries.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.LedgerEntries.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.AccountingOutboxMessages.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.AccountingOutboxMessages.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId && row.ProcessedAtUtc != null),
                cashCached,
                equityCached,
                cashLedger,
                equityLedger);
        }

        public async Task AssertBalancedJournalAsync(Guid documentId, Guid journalId, decimal amount)
        {
            await using var db = CreateDbContext();
            var journal = await db.JournalEntries.IgnoreQueryFilters()
                .SingleAsync(row => row.TenantId == TenantId && row.Id == journalId);
            Assert.Equal(TreasuryInitialCapitalization.AccountingSourceType, journal.SourceType);
            Assert.Equal(documentId, journal.SourceId);
            var lines = await db.LedgerEntries.IgnoreQueryFilters()
                .Where(line => line.TenantId == TenantId && line.JournalEntryId == journalId)
                .OrderBy(line => line.LineNumber)
                .ToListAsync();
            Assert.Equal(amount, lines.Sum(line => line.DebitAmount));
            Assert.Equal(amount, lines.Sum(line => line.CreditAmount));
            Assert.Contains(lines, line => line.AccountId == CashAccountId && line.DebitAmount == amount && line.CreditAmount == 0m);
            Assert.Contains(lines, line => line.AccountId == EquityAccountId && line.DebitAmount == 0m && line.CreditAmount == amount);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _databaseScope.DisposeAsync();
        }

        private async Task<HttpResponseMessage> SendCreateAsync(
            HttpClient client,
            Guid operationId,
            decimal amount,
            string traceId,
            Guid treasuryAccountId,
            Guid? predecessorId,
            DateTime? cutoverDate)
        {
            using (client)
            using (var request = new HttpRequestMessage(HttpMethod.Post, "api/treasury/initial-capitalizations"))
            {
                request.Headers.Add("X-Request-Id", traceId);
                request.Content = JsonContent.Create(new
                {
                    operationId,
                    cutoverDate = cutoverDate ?? DateTime.UtcNow.Date,
                    allocations = new[] { new { treasuryAccountId, amount } },
                    evidenceReference = "TEST-CASH-COUNT",
                    notes = "Disposable endpoint integration test",
                    declarationVersion = TreasuryInitialCapitalization.DeclarationIdentifier,
                    declarationAcknowledged = true,
                    predecessorCapitalizationId = predecessorId
                });
                return await client.SendAsync(request);
            }
        }

        private HttpClient CreateClient(string role, Guid userId)
        {
            var client = new HttpClient { BaseAddress = BaseAddress };
            client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
            client.DefaultRequestHeaders.Add("X-Test-Tenant", TenantId.ToString("D"));
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString("D"));
            return client;
        }

        private ApplicationDbContext CreateDbContext() =>
            new(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(_connectionString)
                    .EnableDetailedErrors()
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                    .Options,
                new TestTenantContext(TenantId));

        private static async Task<SeedIdentity> SeedAsync(string connectionString, bool withTreasuryMovement)
        {
            var tenantId = Guid.NewGuid();
            var foreignTenantId = Guid.NewGuid();
            var ownerUserId = Guid.NewGuid();
            var adminUserId = Guid.NewGuid();
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(connectionString)
                    .EnableDetailedErrors()
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                    .Options,
                new TestTenantContext(tenantId));

            db.Tenants.AddRange(
                CreateTenant(tenantId, "Capitalization Test"),
                CreateTenant(foreignTenantId, "Foreign Capitalization Test"));
            db.Users.AddRange(
                CreateUser(ownerUserId, tenantId, UserRole.Owner, "owner"),
                CreateUser(adminUserId, tenantId, UserRole.Admin, "admin"));

            var cash = Account.Create(tenantId, AccountCodes.CashboxMain, "Main Cashbox", AccountType.Asset, preventNegative: true);
            cash.Id = Guid.NewGuid();
            var bank = Account.Create(tenantId, AccountCodes.BankAccount, "Primary Bank", AccountType.Asset, preventNegative: true);
            bank.Id = Guid.NewGuid();
            var equity = Account.Create(tenantId, AccountCodes.OpeningBalanceEquity, "Opening Balance Equity", AccountType.Equity);
            equity.Id = Guid.NewGuid();
            var testOffset = Account.Create(tenantId, "3098", "Test Seed Offset", AccountType.Equity);
            testOffset.Id = Guid.NewGuid();
            var foreignCash = Account.Create(foreignTenantId, AccountCodes.CashboxMain, "Foreign Cashbox", AccountType.Asset, preventNegative: true);
            foreignCash.Id = Guid.NewGuid();
            var treasury = TreasuryAccount.CreateCashbox(tenantId, cash.Id, "Main Cashbox", isDefault: true);
            treasury.Id = Guid.NewGuid();
            var bankTreasury = TreasuryAccount.CreateBank(tenantId, bank.Id, "Primary Bank", isDefault: true);
            bankTreasury.Id = Guid.NewGuid();
            var foreignTreasury = TreasuryAccount.CreateCashbox(foreignTenantId, foreignCash.Id, "Foreign Cashbox", isDefault: true);
            foreignTreasury.Id = Guid.NewGuid();
            db.LedgerAccounts.AddRange(cash, bank, equity, testOffset, foreignCash);
            db.TreasuryAccounts.AddRange(treasury, bankTreasury, foreignTreasury);

            if (withTreasuryMovement)
            {
                var journal = JournalEntry.Create(
                    tenantId,
                    $"JE-TIC-PRIOR-{Guid.NewGuid():N}",
                    DateTime.UtcNow.Date.AddDays(-2),
                    "TestOpening",
                    Guid.NewGuid(),
                    null,
                    "Existing Treasury movement",
                    $"TEST-TIC-PRIOR:{tenantId}",
                    ownerUserId);
                cash.ApplyEntry(1m, 0m);
                testOffset.ApplyEntry(0m, 1m);
                db.JournalEntries.Add(journal);
                db.LedgerEntries.AddRange(
                    LedgerEntry.CreateDebit(tenantId, journal.Id, cash.Id, 1m, 1m, 1),
                    LedgerEntry.CreateCredit(tenantId, journal.Id, testOffset.Id, 1m, 1m, 2));
            }

            using (LedgerMutationScope.Authorize())
            {
                await db.SaveChangesAsync();
            }

            return new SeedIdentity(
                tenantId,
                ownerUserId,
                adminUserId,
                cash.Id,
                equity.Id,
                treasury.Id,
                foreignTreasury.Id);
        }

        private static Tenant CreateTenant(Guid id, string name) => new()
        {
            Id = id,
            Name = name,
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        };

        private static User CreateUser(Guid id, Guid tenantId, UserRole role, string prefix) => new()
        {
            Id = id,
            TenantId = tenantId,
            Username = $"tic-{prefix}-{id:N}",
            Email = $"tic-{prefix}-{id:N}@test.local",
            PasswordHash = "hashed",
            FirstName = "Capitalization",
            LastName = prefix,
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        private static async Task<decimal> AccountBalanceAsync(ApplicationDbContext db, Guid accountId) =>
            await db.LedgerAccounts.IgnoreQueryFilters()
                .Where(account => account.Id == accountId)
                .Select(account => account.CurrentBalance)
                .SingleAsync();

        private static async Task<decimal> LedgerBalanceAsync(
            ApplicationDbContext db,
            Guid accountId,
            bool debitNormal)
        {
            var signed = await db.LedgerEntries.IgnoreQueryFilters()
                .Where(entry => entry.AccountId == accountId)
                .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount)) ?? 0m;
            return debitNormal ? signed : -signed;
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Auth"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var tenant = Request.Headers["X-Test-Tenant"].SingleOrDefault();
            var role = Request.Headers["X-Test-Role"].SingleOrDefault() ?? "Owner";
            var user = Request.Headers["X-Test-User"].SingleOrDefault() ?? Guid.NewGuid().ToString("D");
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user),
                new(ClaimTypes.Role, role)
            };
            if (!string.IsNullOrWhiteSpace(tenant))
                claims.Add(new Claim("tenant_id", tenant));
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)),
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed record SeedIdentity(
        Guid TenantId,
        Guid OwnerUserId,
        Guid AdminUserId,
        Guid CashAccountId,
        Guid EquityAccountId,
        Guid TreasuryAccountId,
        Guid ForeignTreasuryAccountId);

    private sealed record FinancialSnapshot(
        int Documents,
        int Allocations,
        int Journals,
        int LedgerLines,
        int Outbox,
        int ProcessedOutbox,
        decimal CashCachedBalance,
        decimal EquityCachedBalance,
        decimal CashLedgerBalance,
        decimal EquityLedgerBalance);
}
