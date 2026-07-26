using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Controllers;

public sealed class SettingsControllerCurrencyTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IEventPublisher> _eventPublisher = new();
    private readonly SettingsController _controller;

    public SettingsControllerCurrencyTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SettingsCurrency_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _dbContext.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Settings Currency Tenant",
            Slug = $"settings-currency-{_tenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        _dbContext.Tenants.Add(new Tenant
        {
            Id = _otherTenantId,
            Name = "Other Tenant",
            Slug = $"settings-currency-{_otherTenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        _dbContext.SaveChanges();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(context => context.TenantId).Returns(_tenantId);
        tenantContext.Setup(context => context.RequiredTenantId).Returns(_tenantId);

        _eventPublisher
            .Setup(publisher => publisher.PublishAsync(It.IsAny<TenantCurrencyChangedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _controller = new SettingsController(
            _dbContext,
            NullLogger<SettingsController>.Instance,
            tenantContext.Object,
            _eventPublisher.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task UpdateSettings_WritesIsoCurrencyCodeToTenant_NotDisplaySymbol()
    {
        var result = await _controller.UpdateSettings(new SettingsDto
        {
            CompanyName = "Shop",
            CompanyAddress = "Address",
            CompanyPhone = "123",
            CompanyEmail = "owner@example.com",
            CurrencyCode = "EGP",
            CurrencySymbol = "E\u00A3",
            EnableTax = true,
            TaxRate = 14m,
            ReceiptHeader = "Welcome",
            ReceiptFooter = "Thanks",
            ShowLogoOnReceipt = true
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SettingsDto>(ok.Value);
        
        // Clear change tracker to ensure we're reading persisted data, not tracked in-memory state
        _dbContext.ChangeTracker.Clear();
        
        var tenant = await _dbContext.Tenants.SingleAsync(t => t.Id == _tenantId);

        Assert.Equal("EGP", tenant.Currency);
        Assert.Equal("EGP", response.CurrencyCode);
        Assert.Equal("E\u00A3", response.CurrencySymbol);
        Assert.Equal("EGP", await SettingValueAsync(_tenantId, "Tax.CurrencyCode"));
        Assert.Equal("E\u00A3", await SettingValueAsync(_tenantId, "Tax.CurrencySymbol"));
        Assert.Null(await SettingValueAsync(_otherTenantId, "Tax.CurrencyCode"));
    }

    [Fact]
    public async Task UpdateSettings_WhenLegacySymbolIsSubmitted_NormalizesToIsoCurrencyCode()
    {
        var result = await _controller.UpdateSettings(new SettingsDto
        {
            CompanyName = "Shop",
            CurrencyCode = string.Empty,
            CurrencySymbol = "$",
            EnableTax = true,
            ReceiptHeader = "Welcome",
            ReceiptFooter = "Thanks",
            ShowLogoOnReceipt = true
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SettingsDto>(ok.Value);
        
        // Clear change tracker to ensure we're reading persisted data, not tracked in-memory state
        _dbContext.ChangeTracker.Clear();
        
        var tenant = await _dbContext.Tenants.SingleAsync(t => t.Id == _tenantId);

        Assert.Equal("USD", tenant.Currency);
        Assert.Equal("USD", response.CurrencyCode);
        Assert.Equal("$", response.CurrencySymbol);
    }

    [Fact]
    public async Task GetSettings_ReadsOnlyCurrentTenantScopedRows()
    {
        _dbContext.SystemSettings.AddRange(
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Key = "Company.Name",
                Value = "Tenant A"
            },
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _otherTenantId,
                Key = "Company.Name",
                Value = "Tenant B"
            });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetSettings();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SettingsDto>(ok.Value);

        Assert.Equal("Tenant A", response.CompanyName);
    }

    [Fact]
    public async Task GetSettings_IgnoresSoftDeletedDuplicateTenantRows()
    {
        _dbContext.SystemSettings.AddRange(
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Key = "Company.Name",
                Value = "Active Tenant Name",
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Key = "Company.Name",
                Value = "Deleted Tenant Name",
                IsDeleted = true
            });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetSettings();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SettingsDto>(ok.Value);

        Assert.Equal("Active Tenant Name", response.CompanyName);
    }

    [Fact]
    public async Task GetSettings_WhenActiveDuplicateTenantRowsExist_UsesNewestRowDeterministically()
    {
        var olderCreatedAt = DateTime.UtcNow.AddMinutes(-10);
        var newerCreatedAt = DateTime.UtcNow.AddMinutes(-5);
        var newestUpdatedAt = DateTime.UtcNow.AddMinutes(-1);

        _dbContext.SystemSettings.AddRange(
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Key = "Company.Name",
                Value = "Older Tenant Name",
                CreatedAt = olderCreatedAt,
                UpdatedAt = olderCreatedAt,
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Key = "Company.Name",
                Value = "Newest Tenant Name",
                CreatedAt = newerCreatedAt,
                UpdatedAt = newestUpdatedAt,
                IsDeleted = false
            });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetSettings();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SettingsDto>(ok.Value);

        Assert.Equal("Newest Tenant Name", response.CompanyName);
    }

    [Fact]
    public async Task GetSettings_IgnoresMalformedWhitespaceKeys()
    {
        _dbContext.SystemSettings.AddRange(
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Key = "Company.Name",
                Value = "Valid Tenant Name",
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Key = "   ",
                Value = "Broken Whitespace Key",
                IsDeleted = false
            });
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetSettings();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SettingsDto>(ok.Value);

        Assert.Equal("Valid Tenant Name", response.CompanyName);
    }

    [Fact]
    public async Task UpdateSettings_PersistsTaxRateInvariantly_AndTaxAuthorityReadsSameValueUnderCommaCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var commaCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = commaCulture;
            CultureInfo.CurrentUICulture = commaCulture;

            await _controller.UpdateSettings(new SettingsDto
            {
                CompanyName = "Shop",
                CompanyAddress = "Address",
                CompanyPhone = "123",
                CompanyEmail = "owner@example.com",
                CurrencyCode = "EGP",
                CurrencySymbol = "E\u00A3",
                EnableTax = true,
                TaxRate = 14.5m,
                ReceiptHeader = "Welcome",
                ReceiptFooter = "Thanks",
                ShowLogoOnReceipt = true
            });

            Assert.Equal("14.5", await SettingValueAsync(_tenantId, "Tax.Rate"));

            var taxService = new TaxCalculationService(
                _dbContext,
                Options.Create(new StorefrontPricingOptions()),
                NullLogger<TaxCalculationService>.Instance);

            var tax = await taxService.CalculateAsync(_tenantId, 100m, "EG", CancellationToken.None);
            Assert.Equal(0.145m, tax.TaxRate);
            Assert.Equal(14.5m, tax.TaxAmount);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private async Task<string?> SettingValueAsync(Guid tenantId, string key)
        => await _dbContext.SystemSettings
            .IgnoreQueryFilters()
            .Where(setting => !setting.IsDeleted && setting.TenantId == tenantId && setting.Key == key)
            .Select(setting => setting.Value)
            .SingleOrDefaultAsync();

    public void Dispose() => _dbContext.Dispose();
}
