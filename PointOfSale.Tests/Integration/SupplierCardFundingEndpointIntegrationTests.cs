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

public sealed class SupplierCardFundingEndpointIntegrationTests
{
    [Fact]
    public async Task ZeroBalanceFunding_ReturnsStableInsufficientFundsAndRollsBack()
    {
        await using var fixture = await FundingHttpFixture.StartAsync(0m);
        var before = await fixture.CaptureAsync();

        using var response = await fixture.FundAsync("zero-balance", 100m, "funding-zero-balance");

        await AssertInsufficientFundsAsync(response, "funding-zero-balance", 0m, 100m);
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task OneCentShortFunding_ReturnsStableInsufficientFundsAndRollsBack()
    {
        await using var fixture = await FundingHttpFixture.StartAsync(99.99m);
        var before = await fixture.CaptureAsync();

        using var response = await fixture.FundAsync("one-cent-short", 100m, "funding-one-cent-short");

        await AssertInsufficientFundsAsync(response, "funding-one-cent-short", 99.99m, 100m);
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task ExactBalanceFunding_CommitsOneBalancedPosting()
    {
        await using var fixture = await FundingHttpFixture.StartAsync(100m);
        var before = await fixture.CaptureAsync();

        using var response = await fixture.FundAsync("exact-balance", 100m, "funding-exact-balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var journalId = body.RootElement.GetProperty("journalId").GetGuid();
        var after = await fixture.CaptureAsync();
        Assert.Equal(before.Fundings + 1, after.Fundings);
        Assert.Equal(before.Journals + 1, after.Journals);
        Assert.Equal(before.LedgerLines + 2, after.LedgerLines);
        Assert.Equal(before.Outbox + 1, after.Outbox);
        Assert.Equal(100m, after.CardCachedBalance);
        Assert.Equal(100m, after.CardLedgerBalance);
        Assert.Equal(0m, after.SourceCachedBalance);
        Assert.Equal(0m, after.SourceLedgerBalance);
        await fixture.AssertBalancedFundingJournalAsync(journalId, 100m);
        await fixture.AssertProcessedFundingOutboxAsync(
            body.RootElement.GetProperty("id").GetGuid(),
            journalId,
            100m);
    }

    [Fact]
    public async Task FailedRequestReplay_ReturnsSameBusinessResultAndNeverPersists()
    {
        await using var fixture = await FundingHttpFixture.StartAsync(0m);
        var before = await fixture.CaptureAsync();

        using var first = await fixture.FundAsync("failed-replay", 100m, "funding-failed-replay-1");
        using var second = await fixture.FundAsync("failed-replay", 100m, "funding-failed-replay-2");

        await AssertInsufficientFundsAsync(first, "funding-failed-replay-1", 0m, 100m);
        await AssertInsufficientFundsAsync(second, "funding-failed-replay-2", 0m, 100m);
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task ChangedRequestWithCommittedKey_ReturnsConflictWithoutMutation()
    {
        await using var fixture = await FundingHttpFixture.StartAsync(200m);
        using var committed = await fixture.FundAsync("payload-conflict", 100m, "funding-payload-first");
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        var beforeConflict = await fixture.CaptureAsync();

        using var conflict = await fixture.FundAsync("payload-conflict", 99m, "funding-payload-conflict");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var body = await ReadJsonAsync(conflict);
        Assert.Equal("CONFLICT", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("funding-payload-conflict", body.RootElement.GetProperty("traceId").GetString());
        Assert.Equal(beforeConflict, await fixture.CaptureAsync());
    }

    [Fact]
    public async Task ConcurrentFundings_CannotOverdrawTheSource()
    {
        await using var fixture = await FundingHttpFixture.StartAsync(100m);
        var before = await fixture.CaptureAsync();

        var responses = await Task.WhenAll(
            fixture.FundAsync("concurrent-a", 80m, "funding-concurrent-a"),
            fixture.FundAsync("concurrent-b", 80m, "funding-concurrent-b"));
        using var first = responses[0];
        using var second = responses[1];

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertInsufficientFundsAsync(
            conflict,
            conflict.Headers.GetValues("X-Request-ID").Single(),
            20m,
            80m);
        var after = await fixture.CaptureAsync();
        Assert.Equal(before.Fundings + 1, after.Fundings);
        Assert.Equal(before.Journals + 1, after.Journals);
        Assert.Equal(before.LedgerLines + 2, after.LedgerLines);
        Assert.Equal(before.Outbox + 1, after.Outbox);
        Assert.Equal(80m, after.CardLedgerBalance);
        Assert.Equal(20m, after.SourceLedgerBalance);
    }

    [Fact]
    public async Task ForeignTenantCannotUseInstrumentAuthority()
    {
        await using var fixture = await FundingHttpFixture.StartAsync(100m);
        var before = await fixture.CaptureAsync();

        using var response = await fixture.FundAsync(
            "foreign-tenant",
            10m,
            "funding-foreign-tenant",
            Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await fixture.CaptureAsync());
    }

    private static async Task AssertInsufficientFundsAsync(
        HttpResponseMessage response,
        string traceId,
        decimal available,
        decimal required)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(nameof(PointOfSale.Core.Exceptions.Accounting.InsufficientFundsException), rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", rawBody, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(rawBody);
        Assert.Equal("INSUFFICIENT_FUNDS", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("The selected account does not have sufficient available funds.", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(traceId, body.RootElement.GetProperty("traceId").GetString());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("1096", data.GetProperty("accountCode").GetString());
        Assert.Equal(available, data.GetProperty("availableBalance").GetDecimal());
        Assert.Equal(required, data.GetProperty("requiredAmount").GetDecimal());
        Assert.Equal(required - available, data.GetProperty("shortfall").GetDecimal());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed class FundingHttpFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TestDatabaseScope _databaseScope;
        private readonly string _connectionString;

        private FundingHttpFixture(
            WebApplication app,
            Uri baseAddress,
            TestDatabaseScope databaseScope,
            string connectionString,
            Guid tenantId,
            Guid sourceAccountId,
            Guid cardAccountId,
            Guid sourceId,
            Guid instrumentId)
        {
            _app = app;
            BaseAddress = baseAddress;
            _databaseScope = databaseScope;
            _connectionString = connectionString;
            TenantId = tenantId;
            SourceAccountId = sourceAccountId;
            CardAccountId = cardAccountId;
            SourceId = sourceId;
            InstrumentId = instrumentId;
        }

        private Uri BaseAddress { get; }
        private Guid TenantId { get; }
        private Guid SourceAccountId { get; }
        private Guid CardAccountId { get; }
        private Guid SourceId { get; }
        private Guid InstrumentId { get; }

        public static async Task<FundingHttpFixture> StartAsync(decimal sourceBalance)
        {
            var databaseScope = TestDatabaseScope.Open();
            try
            {
                await using var connectionContext = databaseScope.CreateDbContext();
                var connectionString = connectionContext.Database.GetDbConnection().ConnectionString;
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = Environments.Development
                });
                builder.WebHost.UseUrls($"http://127.0.0.1:{GetFreeTcpPort()}");
                builder.Logging.ClearProviders();
                builder.Services.AddLogging();
                builder.Services.AddScoped<ApiErrorResponseNormalizationFilter>();
                builder.Services.AddControllers(options =>
                    {
                        options.Filters.AddService<ApiErrorResponseNormalizationFilter>();
                    })
                    .AddApplicationPart(typeof(SupplierCardFundingInstrumentsController).Assembly);
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

                var app = builder.Build();
                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseAuthentication();
                app.Use(async (context, next) =>
                {
                    if (Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var tenantId))
                    {
                        context.RequestServices.GetRequiredService<MutableTenantContext>().TenantId = tenantId;
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
                var seed = await SeedAsync(connectionString, sourceBalance);
                return new FundingHttpFixture(
                    app,
                    new Uri(address),
                    databaseScope,
                    connectionString,
                    seed.TenantId,
                    seed.SourceAccountId,
                    seed.CardAccountId,
                    seed.SourceId,
                    seed.InstrumentId);
            }
            catch
            {
                await databaseScope.DisposeAsync();
                throw;
            }
        }

        public async Task<HttpResponseMessage> FundAsync(
            string idempotencyKey,
            decimal amount,
            string traceId,
            Guid? tenantId = null)
        {
            using var client = CreateClient(tenantId ?? TenantId);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"api/supplier-card-funding-instruments/{InstrumentId}/fundings");
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Add("X-Request-Id", traceId);
            request.Content = JsonContent.Create(new FundSupplierCardRequest(
                1,
                SourceId,
                amount,
                DateTime.UtcNow.Date,
                "HTTP-TEST",
                "Disposable endpoint integration test"));
            return await client.SendAsync(request);
        }

        public async Task<FinancialSnapshot> CaptureAsync()
        {
            await using var db = CreateDbContext();
            var cardCached = await db.LedgerAccounts.IgnoreQueryFilters()
                .Where(account => account.TenantId == TenantId && account.Id == CardAccountId)
                .Select(account => account.CurrentBalance)
                .SingleAsync();
            var sourceCached = await db.LedgerAccounts.IgnoreQueryFilters()
                .Where(account => account.TenantId == TenantId && account.Id == SourceAccountId)
                .Select(account => account.CurrentBalance)
                .SingleAsync();
            var cardLedger = await db.LedgerEntries.IgnoreQueryFilters()
                .Where(entry => entry.TenantId == TenantId && entry.AccountId == CardAccountId)
                .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount)) ?? 0m;
            var sourceLedger = await db.LedgerEntries.IgnoreQueryFilters()
                .Where(entry => entry.TenantId == TenantId && entry.AccountId == SourceAccountId)
                .SumAsync(entry => (decimal?)(entry.DebitAmount - entry.CreditAmount)) ?? 0m;
            return new FinancialSnapshot(
                await db.SupplierCardFundings.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.JournalEntries.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.LedgerEntries.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                await db.AccountingOutboxMessages.IgnoreQueryFilters().CountAsync(row => row.TenantId == TenantId),
                cardCached,
                sourceCached,
                cardLedger,
                sourceLedger);
        }

        public async Task AssertBalancedFundingJournalAsync(Guid journalId, decimal amount)
        {
            await using var db = CreateDbContext();
            var lines = await db.LedgerEntries.IgnoreQueryFilters()
                .Where(line => line.TenantId == TenantId && line.JournalEntryId == journalId)
                .OrderBy(line => line.LineNumber)
                .ToListAsync();
            Assert.Collection(
                lines,
                line =>
                {
                    Assert.Equal(CardAccountId, line.AccountId);
                    Assert.Equal(amount, line.DebitAmount);
                    Assert.Equal(0m, line.CreditAmount);
                },
                line =>
                {
                    Assert.Equal(SourceAccountId, line.AccountId);
                    Assert.Equal(0m, line.DebitAmount);
                    Assert.Equal(amount, line.CreditAmount);
                });
            Assert.Equal(lines.Sum(line => line.DebitAmount), lines.Sum(line => line.CreditAmount));
        }

        public async Task AssertProcessedFundingOutboxAsync(Guid fundingId, Guid journalId, decimal amount)
        {
            await using var db = CreateDbContext();
            var message = await db.AccountingOutboxMessages.IgnoreQueryFilters()
                .SingleAsync(row =>
                    row.TenantId == TenantId &&
                    row.EventType == "SupplierCardFunding" &&
                    row.BusinessEntityId == fundingId);
            Assert.Equal("SupplierCardFunding", message.SourceType);
            Assert.Equal($"OUTBOX:SUPPLIER_CARD_FUNDING:{TenantId}:{fundingId}", message.IdempotencyKey);
            Assert.Equal(journalId, message.JournalId);
            Assert.NotNull(message.ProcessedAtUtc);
            Assert.False(message.IsFailed);
            using var payload = JsonDocument.Parse(message.PayloadJson);
            Assert.Equal(fundingId, payload.RootElement.GetProperty("Id").GetGuid());
            Assert.Equal(amount, payload.RootElement.GetProperty("Amount").GetDecimal());
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _databaseScope.DisposeAsync();
        }

        private HttpClient CreateClient(Guid tenantId)
        {
            var client = new HttpClient { BaseAddress = BaseAddress };
            client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId.ToString("D"));
            client.DefaultRequestHeaders.Add("X-Test-Role", "Owner");
            client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString("D"));
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

        private static async Task<SeedIdentity> SeedAsync(string connectionString, decimal sourceBalance)
        {
            var tenantId = Guid.NewGuid();
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(connectionString)
                    .EnableDetailedErrors()
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                    .Options,
                new TestTenantContext(tenantId));
            var sourceAccount = Account.Create(tenantId, "1096", "Funding source", AccountType.Asset, preventNegative: true);
            sourceAccount.Id = Guid.NewGuid();
            var offsetAccount = Account.Create(tenantId, "3096", "Test opening offset", AccountType.Equity);
            offsetAccount.Id = Guid.NewGuid();
            var cardAccount = Account.Create(tenantId, "1097", "Supplier card asset", AccountType.Asset, preventNegative: true);
            cardAccount.Id = Guid.NewGuid();
            cardAccount.DesignateAsSupplierCardFunding();
            var source = TreasuryAccount.CreateBank(tenantId, sourceAccount.Id, "Funding source");
            source.Id = Guid.NewGuid();
            var instrument = SupplierCardFundingInstrument.Create(
                tenantId,
                "Endpoint test card",
                cardAccount.Id,
                source.Id,
                "Test issuer",
                "1234",
                true);
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Supplier Card Endpoint Test",
                Currency = "EGP",
                IsActive = true,
                Status = TenantStatus.Active
            });
            db.LedgerAccounts.AddRange(sourceAccount, offsetAccount, cardAccount);
            db.TreasuryAccounts.Add(source);
            db.SupplierCardFundingInstruments.Add(instrument);
            if (sourceBalance > 0m)
            {
                var journal = JournalEntry.Create(
                    tenantId,
                    $"JE-SCF-ENDPOINT-{Guid.NewGuid():N}",
                    DateTime.UtcNow.Date,
                    "TestOpening",
                    Guid.NewGuid(),
                    null,
                    "Disposable funding source opening",
                    $"TEST-SCF-ENDPOINT-OPENING:{tenantId}",
                    null);
                sourceAccount.ApplyEntry(sourceBalance, 0m);
                offsetAccount.ApplyEntry(0m, sourceBalance);
                db.JournalEntries.Add(journal);
                db.LedgerEntries.AddRange(
                    LedgerEntry.CreateDebit(tenantId, journal.Id, sourceAccount.Id, sourceBalance, sourceBalance, 1),
                    LedgerEntry.CreateCredit(tenantId, journal.Id, offsetAccount.Id, sourceBalance, sourceBalance, 2));
            }

            using (LedgerMutationScope.Authorize())
            {
                await db.SaveChangesAsync();
            }

            return new SeedIdentity(tenantId, sourceAccount.Id, cardAccount.Id, source.Id, instrument.Id);
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
        Guid SourceAccountId,
        Guid CardAccountId,
        Guid SourceId,
        Guid InstrumentId);

    private sealed record FinancialSnapshot(
        int Fundings,
        int Journals,
        int LedgerLines,
        int Outbox,
        decimal CardCachedBalance,
        decimal SourceCachedBalance,
        decimal CardLedgerBalance,
        decimal SourceLedgerBalance);
}
