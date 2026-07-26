using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Platform;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.Platform;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.Security;

public sealed class TenantRegistrationFreshDatabaseTests
{
    [Fact]
    public async Task RegisterTenantAsync_EmptyApplicationDatabase_CreatesFirstTenant()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        Assert.Empty(context.Tenants);

        var service = CreateService(context);
        var result = await service.RegisterTenantAsync(Request("first-store"));

        Assert.True(result.Success, result.Error?.Message);
        Assert.NotNull(result.Response);
        Assert.Equal("first-store", result.Response!.Subdomain);
        Assert.Equal(FinancialCurrencyGuard.PlatformBaseCurrencyCode, await context.Tenants.Select(t => t.Currency).SingleAsync());
        Assert.Equal(1, await context.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await context.StoreConfigs.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void ApplicationDbContext_ModelSeedsRequiredPlatformCurrency()
    {
        using var context = CreateDbContext();

        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var currencyEntity = designTimeModel.FindEntityType(typeof(Currency));
        Assert.NotNull(currencyEntity);

        var seed = Assert.Single(currencyEntity!.GetSeedData());
        Assert.Equal(FinancialCurrencyGuard.PlatformBaseCurrencyCode, seed[nameof(Currency.Code)]);
        Assert.Equal("Egyptian Pound", seed[nameof(Currency.Name)]);
        Assert.Equal("EGP", seed[nameof(Currency.Symbol)]);
        Assert.Equal(2, seed[nameof(Currency.DecimalPlaces)]);
    }

    [Fact]
    public void BaselineAndUpgradeMigrations_SeedRequiredPlatformCurrency()
    {
        var baseline = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260723214642_Baseline_Recovery3.cs"));
        var upgrade = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260726172030_PlatformCurrencyReferenceSeed.cs"));

        Assert.Contains("table: \"Currencies\"", baseline, StringComparison.Ordinal);
        Assert.Contains("\"EGP\"", baseline, StringComparison.Ordinal);
        Assert.Contains("FK_Tenants_Currencies_Currency", baseline, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (\"Code\") DO UPDATE", upgrade, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantRegistrationService_LogsStructuredDiagnosticsAndKeepsTransactionBoundary()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "PointOfSale.API",
            "Services",
            "Platform",
            "TenantRegistrationService.cs"));

        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync", source, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", source, StringComparison.Ordinal);
        Assert.Contains("ExceptionType", source, StringComparison.Ordinal);
        Assert.Contains("InnerExceptionType", source, StringComparison.Ordinal);
        Assert.Contains("InnerExceptionMessage", source, StringComparison.Ordinal);
        Assert.Contains("StackTrace", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterTenantAsync_WhenLateBootstrapStepFails_ReturnsFriendlyError()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();

        var governance = new Mock<IUserGovernanceService>();
        governance
            .Setup(service => service.EnsureRegistrationAllowedAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        governance
            .Setup(service => service.EnsureProfileExistsAsync(
                UserGovernanceRole.Vendor,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("profile bootstrap failed"));

        var service = CreateService(context, governance.Object);
        var result = await service.RegisterTenantAsync(Request("rollback-store"));

        Assert.False(result.Success);
        Assert.Equal("REGISTRATION_FAILED", result.Error?.Error);
        Assert.Equal("We couldn't create your store right now. Please try again.", result.Error?.Message);
    }

    private static TenantRegistrationService CreateService(
        ApplicationDbContext context,
        IUserGovernanceService? governanceService = null)
    {
        var emailVerification = new Mock<IEmailVerificationService>();
        emailVerification
            .Setup(service => service.CreateVerificationTokenAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("verification-token");

        var accountSeeder = new Mock<ITenantAccountSeeder>();
        accountSeeder
            .Setup(service => service.SeedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(publisher => publisher.PublishAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "tenant-registration-fresh-database-test"
        };

        return new TenantRegistrationService(
            context,
            new PasswordHasher<User>(),
            emailVerification.Object,
            Options.Create(new EmailVerificationSettings { Enabled = false }),
            Options.Create(new JwtSettings
            {
                Secret = "fresh-registration-test-secret-key-1234567890",
                Issuer = "PointOfSale.Tests",
                Audience = "PointOfSale.Tests",
                AccessTokenExpirationMinutes = 15,
                RefreshTokenExpirationDays = 7
            }),
            NullLogger<TenantRegistrationService>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Platform:Domain"] = "example.test",
                    ["Platform:PosUrl"] = "https://pos.example.test"
                })
                .Build(),
            accountSeeder.Object,
            governanceService ?? new NoOpUserGovernanceService(),
            eventPublisher.Object,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private static TenantRegistrationRequest Request(string subdomain) =>
        new()
        {
            BusinessName = "Fresh Store",
            Email = $"{subdomain}@example.test",
            Password = "P@ssword123!",
            Subdomain = subdomain,
            Country = "EG",
            Currency = FinancialCurrencyGuard.PlatformBaseCurrencyCode
        };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be located.");
    }
}
