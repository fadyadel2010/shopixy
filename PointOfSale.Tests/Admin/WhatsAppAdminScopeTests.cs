using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class WhatsAppAdminScopeTests
{
    [Fact]
    public async Task EnableAccount_UsesTrustedTenantScopeForLookupAndToggle()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var accountService = new Mock<IWhatsAppAccountService>(MockBehavior.Strict);
        var account = new WhatsAppAccount
        {
            Id = accountId,
            TenantId = tenantId,
            Status = WhatsAppAccountStatus.Connected,
            Enabled = false,
            DisplayName = "Ops",
            PhoneNumber = "+201000000000"
        };

        accountService
            .Setup(service => service.GetTenantAccountAsync(tenantId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        accountService
            .Setup(service => service.ToggleAccountStatusAsync(tenantId, accountId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var dbContext = CreateDbContext(tenantContext);
        var controller = CreateController(tenantContext, dbContext, accountService.Object);

        var result = await controller.EnableAccount(accountId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        accountService.VerifyAll();
    }

    [Fact]
    public async Task DeleteTemplate_UsesTrustedTenantScopeForLookupAndDelete()
    {
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var accountService = new Mock<IWhatsAppAccountService>(MockBehavior.Strict);
        var template = new WhatsAppMessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Type = "OrderConfirmation",
            Language = "en",
            Content = "Template"
        };

        accountService
            .Setup(service => service.GetTenantTemplateAsync(tenantId, templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        accountService
            .Setup(service => service.DeleteTemplateAsync(tenantId, templateId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var dbContext = CreateDbContext(tenantContext);
        var controller = CreateController(tenantContext, dbContext, accountService.Object);

        var result = await controller.DeleteTemplate(templateId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        accountService.VerifyAll();
    }

    [Fact]
    public async Task GetTemplates_WhenNoOverrides_ReturnsVisibleSystemDefaults()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var accountService = new Mock<IWhatsAppAccountService>(MockBehavior.Strict);
        accountService
            .Setup(service => service.GetTemplatesAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await using var dbContext = CreateDbContext(tenantContext);
        var controller = CreateController(tenantContext, dbContext, accountService.Object);

        var action = await controller.GetTemplates(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var templates = Assert.IsType<List<WhatsAppTemplateDto>>(ok.Value);
        Assert.Equal(8, templates.Count);
        Assert.All(templates, template => Assert.True(template.IsDefault));
        Assert.Equal(2, templates.Count(template => template.Type == "OrderConfirmation"));
        Assert.Equal(2, templates.Count(template => template.Type == "Otp"));
        Assert.Equal(2, templates.Count(template => template.Type == "AbandonedCartRecoveryFirst"));
        Assert.Equal(2, templates.Count(template => template.Type == "AbandonedCartRecoverySecond"));
        accountService.VerifyAll();
    }

    [Fact]
    public async Task CreateAccount_NormalizesLocalPhoneUsingSelectedCountry()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var accountService = new Mock<IWhatsAppAccountService>(MockBehavior.Strict);
        var phoneNormalizer = new Mock<IPhoneNumberNormalizer>(MockBehavior.Strict);
        WhatsAppAccount? createdAccount = null;

        phoneNormalizer
            .Setup(service => service.Normalize("01022174803", "EG"))
            .Returns("+201022174803");
        accountService
            .Setup(service => service.GetAccountsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        accountService
            .Setup(service => service.CreateAccountAsync(It.IsAny<WhatsAppAccount>(), It.IsAny<CancellationToken>()))
            .Callback<WhatsAppAccount, CancellationToken>((account, _) => createdAccount = account)
            .ReturnsAsync((WhatsAppAccount account, CancellationToken _) => account);

        await using var dbContext = CreateDbContext(tenantContext);
        var controller = CreateController(
            tenantContext,
            dbContext,
            accountService.Object,
            phoneNormalizer.Object);

        var action = await controller.CreateAccount(new CreateWhatsAppAccountDto
        {
            DisplayName = "Main",
            PhoneNumber = "01022174803",
            CountryCode = "EG",
            UseForOrderConfirmation = true,
            UseForOtpVerification = true
        }, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(action.Result);
        Assert.NotNull(createdAccount);
        Assert.Equal("+201022174803", createdAccount.PhoneNumber);
        accountService.VerifyAll();
        phoneNormalizer.VerifyAll();
    }

    private static ApplicationDbContext CreateDbContext(ITenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantContext);

    private static WhatsAppAdminController CreateController(
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        IWhatsAppAccountService accountService,
        IPhoneNumberNormalizer? phoneNumberNormalizer = null) =>
        new(
            accountService,
            Mock.Of<IWhatsAppTransport>(),
            dbContext,
            tenantContext,
            Mock.Of<IWhatsAppKillSwitch>(),
            Mock.Of<IWhatsAppMessageLogService>(),
            phoneNumberNormalizer ?? Mock.Of<IPhoneNumberNormalizer>(),
            NullLogger<WhatsAppAdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}
