using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Moq;
using PointOfSale.API.Models;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Storefront;
using PointOfSale.API.Services.Auth;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.EventBus;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Tests.Shipping;
using StackExchange.Redis;

namespace PointOfSale.Tests.Security;

// Every write uses the CURRENT configured development database and an explicit
// outer rollback. No schema creation/migration, replacement database or external bus.
public sealed class StorefrontRegistrationPhoneConfiguredTests
{
    private static readonly Guid TenantId = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");

    [RegistrationPhoneConfiguredTheory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\r\n ", null)]
    [InlineData("+1 (202) 555-0105", "+12025550105")]
    [InlineData("0012025550105", "+12025550105")]
    public async Task OptionalPhone_PersistsCanonicalValueWithTenantAndAtomicOutbox(string? phone, string? expected)
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = Request(phone);
        var result = await Service(scope.ServiceProvider).RegisterAsync(TenantId, request);
        Assert.True(result.Success);
        Assert.Empty(result.Response!.AccessToken);
        Assert.Empty(result.Response.RefreshToken);
        var customer = await db.OnlineCustomers.SingleAsync(c => c.Email == request.Email);
        Assert.Equal(expected, customer.Phone);
        Assert.Equal(expected, result.Response.Customer.Phone);
        Assert.Equal(TenantId, customer.TenantId);
        Assert.Equal(AuthProvider.Local, customer.Provider);
        Assert.Null(customer.ProviderUserId);
        Assert.NotNull(customer.PasswordHash);
        Assert.False(customer.EmailVerified);
        Assert.False(customer.PhoneVerified);
        Assert.Single(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>()
            .Where(m => m.Body.Contains(request.Email)).ToListAsync());
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
        Assert.False(await db.OnlineCustomers.AnyAsync(c => c.Id == customer.Id));
        Assert.False(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(m => m.Body.Contains(request.Email)));
    }

    [RegistrationPhoneConfiguredTheory]
    [InlineData("12345")]
    [InlineData("not-a-phone")]
    [InlineData("+1+2025550105")]
    [InlineData("+1202555010512345678901234567890123456789012345678901234567890")]
    public async Task InvalidPhone_IsControlledAndCreatesNoCustomerOrVerification(string phone)
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var verification = new Mock<IStorefrontEmailVerificationService>(MockBehavior.Strict);
        var request = Request(phone);
        var result = await Service(scope.ServiceProvider, verification: verification.Object).RegisterAsync(TenantId, request);
        Assert.False(result.Success);
        Assert.Equal("INVALID_PHONE", result.Error);
        Assert.False(await db.OnlineCustomers.AnyAsync(c => c.Email == request.Email));
        Assert.Empty(db.ChangeTracker.Entries<OnlineCustomer>());
        verification.VerifyNoOtherCalls();
        await transaction.RollbackAsync();
    }

    [RegistrationPhoneConfiguredFact]
    public async Task DuplicatePhone_IsPermittedForSeparateEmailIdentitiesAndEquivalentSpellings()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var first = Request("+1 (202) 555-0105");
        var second = Request("+12025550105");
        Assert.True((await Service(scope.ServiceProvider).RegisterAsync(TenantId, first)).Success);
        Assert.True((await Service(scope.ServiceProvider).RegisterAsync(TenantId, second)).Success);
        var customers = await db.OnlineCustomers.Where(c => c.Email == first.Email || c.Email == second.Email).ToListAsync();
        Assert.Equal(2, customers.Count);
        Assert.All(customers, c => Assert.Equal("+12025550105", c.Phone));
        Assert.All(customers, c => Assert.Equal(TenantId, c.TenantId));
        Assert.NotEqual(customers[0].Id, customers[1].Id);
        await transaction.RollbackAsync();
    }

    [RegistrationPhoneConfiguredFact]
    public async Task PotentiallyCommittedRetry_UsesExistingEmailIdentityWithoutSecondCustomerOrDispatch()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var verification = new Mock<IStorefrontEmailVerificationService>();
        verification.Setup(v => v.RequestVerificationAsync(TenantId, It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorefrontEmailVerificationResult { Success = true });
        var request = Request("+12025550105");
        var service = Service(scope.ServiceProvider, verification: verification.Object);
        var first = await service.RegisterAsync(TenantId, request);
        Assert.True(first.Success);
        // Models a lost response: retry the SAME normalized identity, never a new email.
        request.Email = "  " + request.Email.ToUpperInvariant() + "  ";
        var retry = await service.RegisterAsync(TenantId, request);
        Assert.Equal("EMAIL_EXISTS", retry.Error);
        Assert.Single(await db.OnlineCustomers.Where(c => c.Id == first.Response!.Customer.Id).ToListAsync());
        verification.Verify(v => v.RequestVerificationAsync(TenantId, It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()), Times.Once);
        await transaction.RollbackAsync();
    }

    [RegistrationPhoneConfiguredFact]
    public async Task VerificationOutage_DoesNotMisreportCreatedAccountOrAllowDuplicateRetry()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var verification = new Mock<IStorefrontEmailVerificationService>();
        verification.Setup(v => v.RequestVerificationAsync(TenantId, It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic local dispatch outage"));
        var service = Service(scope.ServiceProvider, verification: verification.Object);
        var request = Request(null);
        var result = await service.RegisterAsync(TenantId, request);
        Assert.True(result.Success);
        Assert.False(result.Response!.VerificationEmailSent);
        Assert.False(result.Response.Customer.EmailVerified);
        Assert.Equal("EMAIL_EXISTS", (await service.RegisterAsync(TenantId, request)).Error);
        Assert.Single(await db.OnlineCustomers.Where(c => c.Email == request.Email).ToListAsync());
        await transaction.RollbackAsync();
    }

    [RegistrationPhoneConfiguredFact]
    public async Task PersistenceFailure_RollsBackCustomerAndRegistrationOutboxTogether()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var request = Request("+12025550105");
        request.DisplayName = new string('Q', 201); // Actual configured varchar(200) failure.
        var verification = new Mock<IStorefrontEmailVerificationService>(MockBehavior.Strict);
        var result = await Service(scope.ServiceProvider, verification: verification.Object).RegisterAsync(TenantId, request);
        Assert.Equal("REGISTRATION_FAILED", result.Error);
        db.ChangeTracker.Clear();
        Assert.False(await db.OnlineCustomers.AnyAsync(c => c.Email == request.Email));
        Assert.False(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().AnyAsync(m => m.Body.Contains(request.Email)));
        verification.VerifyNoOtherCalls();
        await transaction.RollbackAsync();
    }

    [RegistrationPhoneConfiguredFact]
    public async Task PhoneRiskLookup_IsTenantScopedAndUsesCanonicalSpelling()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var foreignId = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id != TenantId).Select(t => t.Id).FirstAsync();
        tenant.SetTenantId(foreignId);
        db.PhoneRiskProfiles.Add(new PhoneRiskProfile
        {
            Id = Guid.NewGuid(), TenantId = foreignId, PhoneNumber = "+12025550106", IsBlocked = true,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        tenant.SetTenantId(TenantId);
        var allowed = await Service(scope.ServiceProvider).RegisterAsync(TenantId, Request("+1 (202) 555-0106"));
        Assert.True(allowed.Success);
        Assert.Equal(TenantId, (await db.OnlineCustomers.SingleAsync(c => c.Id == allowed.Response!.Customer.Id)).TenantId);
        tenant.SetTenantId(foreignId);
        var request = Request("+1 (202) 555-0106");
        var blocked = await Service(scope.ServiceProvider).RegisterAsync(foreignId, request);
        Assert.Equal("PHONE_BLOCKED", blocked.Error);
        Assert.False(await db.OnlineCustomers.AnyAsync(c => c.Email == request.Email));
        await transaction.RollbackAsync();
    }

    [RegistrationPhoneConfiguredFact]
    public async Task EmailVerification_CommitsCustomerConsumedTokenAndOutboxInOneBoundary()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (customerId, rawToken) = await VerificationFixture(scope.ServiceProvider);
        var verification = VerificationService(scope.ServiceProvider);
        var result = await verification.VerifyEmailAsync(TenantId, rawToken);
        Assert.True(result.Success);
        db.ChangeTracker.Clear();
        var customer = await db.OnlineCustomers.SingleAsync(c => c.Id == customerId);
        var token = await db.OnlineCustomerEmailVerifications.SingleAsync(v => v.OnlineCustomerId == customerId);
        Assert.True(customer.EmailVerified);
        Assert.True(token.IsUsed);
        Assert.NotNull(customer.EmailVerifiedAtUtc);
        Assert.Equal(customer.EmailVerifiedAtUtc, token.UsedAtUtc);
        Assert.Equal(TenantId, token.TenantId);
        Assert.Single(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>()
            .Where(m => m.MessageType.Contains(nameof(OnlineCustomerEmailVerifiedEvent)) && m.Body.Contains(customerId.ToString())).ToListAsync());
        Assert.Equal("TOKEN_USED", (await verification.VerifyEmailAsync(TenantId, rawToken)).Error);
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
        Assert.False(await db.OnlineCustomers.AnyAsync(c => c.Id == customerId));
    }

    [RegistrationPhoneConfiguredTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmailVerification_PublishOrSqlFailure_DoesNotPartiallyVerifyOrConsumeToken(bool publishFailure)
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (customerId, rawToken) = await VerificationFixture(scope.ServiceProvider);
        IEventPublisher? publisher = null;
        if (publishFailure)
        {
            var failing = new Mock<IEventPublisher>();
            failing.Setup(p => p.PublishAsync(It.IsAny<OnlineCustomerEmailVerifiedEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Synthetic verification publish failure"));
            publisher = failing.Object;
        }
        else
        {
            (await db.OnlineCustomers.SingleAsync(c => c.Id == customerId)).DisplayName = new string('Q', 201);
        }
        var result = await VerificationService(scope.ServiceProvider, publisher).VerifyEmailAsync(TenantId, rawToken);
        Assert.Equal("VERIFICATION_FAILED", result.Error);
        db.ChangeTracker.Clear();
        Assert.False((await db.OnlineCustomers.SingleAsync(c => c.Id == customerId)).EmailVerified);
        Assert.False((await db.OnlineCustomerEmailVerifications.SingleAsync(v => v.OnlineCustomerId == customerId)).IsUsed);
        Assert.False(await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>()
            .AnyAsync(m => m.MessageType.Contains(nameof(OnlineCustomerEmailVerifiedEvent)) && m.Body.Contains(customerId.ToString())));
        await transaction.RollbackAsync();
    }

    [RegistrationPhoneConfiguredFact]
    public async Task NormalizationDependencyFailure_IsUnavailableNotInvalidPhoneOrUnhandled()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        var risk = new Mock<IPhoneRiskProfileService>();
        risk.Setup(r => r.NormalizePhoneAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Tenant country configuration unavailable", "phoneNumber",
                new PhoneNumberNormalizationException("Missing configuration", reason: PhoneNormalizationFailureReason.MissingCountryCode)));
        var result = await Service(scope.ServiceProvider, risk: risk.Object).RegisterAsync(TenantId, Request("2025550105"));
        Assert.Equal("REGISTRATION_FAILED", result.Error);
    }

    private static StorefrontRegisterRequest Request(string? phone) => new()
    {
        Email = $"regphone20261020-rollback-{Guid.NewGuid():N}@example.test",
        Password = "SyntheticRollbackOnly1!", DisplayName = "QA Registration Phone Rollback", Phone = phone
    };

    private static async Task<(Guid CustomerId, string RawToken)> VerificationFixture(IServiceProvider services)
    {
        var result = await Service(services).RegisterAsync(TenantId, Request("+12025550105"));
        Assert.True(result.Success);
        var rawToken = Guid.NewGuid().ToString("N");
        var db = services.GetRequiredService<ApplicationDbContext>();
        db.OnlineCustomerEmailVerifications.Add(new OnlineCustomerEmailVerification
        {
            Id = Guid.NewGuid(), TenantId = TenantId, OnlineCustomerId = result.Response!.Customer.Id,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant(),
            CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        return (result.Response.Customer.Id, rawToken);
    }

    private static StorefrontEmailVerificationService VerificationService(IServiceProvider services, IEventPublisher? publisher = null) => new(
        services.GetRequiredService<ApplicationDbContext>(), Mock.Of<IConnectionMultiplexer>(),
        publisher ?? services.GetRequiredService<IEventPublisher>(), Mock.Of<IEmailSender>(),
        Options.Create(new StorefrontEmailVerificationSettings()), null!, NullLogger<StorefrontEmailVerificationService>.Instance);

    private static StorefrontAuthService Service(IServiceProvider services,
        IStorefrontEmailVerificationService? verification = null, IPhoneRiskProfileService? risk = null) => new(
        services.GetRequiredService<ApplicationDbContext>(),
        Options.Create(new StorefrontJwtSettings { Secret = "rollback-only-registration-test-secret-123456789" }),
        new PasswordHasher<OnlineCustomer>(), services.GetRequiredService<IEventPublisher>(),
        risk ?? services.GetRequiredService<IPhoneRiskProfileService>(), NullLogger<StorefrontAuthService>.Instance,
        verification ?? Mock.Of<IStorefrontEmailVerificationService>(v =>
            v.RequestVerificationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()) ==
            Task.FromResult(new StorefrontEmailVerificationResult { Success = true })));

    private static ServiceProvider Provider()
    {
        using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var connectionString = configured.Database.GetConnectionString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => { var tenant = new TenantContext(); tenant.SetTenantId(TenantId); return tenant; });
        services.AddScoped<ITenantContext>(s => s.GetRequiredService<TenantContext>());
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IPhoneNumberNormalizer>(s => new PhoneNumberNormalizer(
            s.GetRequiredService<ApplicationDbContext>(), null, NullLogger<PhoneNumberNormalizer>.Instance));
        services.AddScoped<IPhoneRiskProfileService>(s => new PhoneRiskProfileService(
            s.GetRequiredService<ApplicationDbContext>(), s.GetRequiredService<IPhoneNumberNormalizer>(),
            Mock.Of<IOrderVelocityService>(), NullLogger<PhoneRiskProfileService>.Instance));
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<ApplicationDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); });
            x.UsingInMemory();
        });
        return services.BuildServiceProvider();
    }
}

public sealed class RegistrationPhoneConfiguredFactAttribute : FactAttribute
{
    public RegistrationPhoneConfiguredFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("REGISTRATION_PHONE_CONFIGURED") != "1")
            Skip = "Requires opt-in to current-development PostgreSQL rollback-only registration tests.";
    }
}

public sealed class RegistrationPhoneConfiguredTheoryAttribute : TheoryAttribute
{
    public RegistrationPhoneConfiguredTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("REGISTRATION_PHONE_CONFIGURED") != "1")
            Skip = "Requires opt-in to current-development PostgreSQL rollback-only registration tests.";
    }
}
