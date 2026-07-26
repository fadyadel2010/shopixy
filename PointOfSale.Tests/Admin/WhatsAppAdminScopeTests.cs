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

    private static ApplicationDbContext CreateDbContext(ITenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantContext);

    private static WhatsAppAdminController CreateController(
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        IWhatsAppAccountService accountService) =>
        new(
            accountService,
            Mock.Of<IWhatsAppWebEngine>(),
            new ServiceCollection().BuildServiceProvider(),
            dbContext,
            tenantContext,
            Mock.Of<IWhatsAppKillSwitch>(),
            Mock.Of<IWhatsAppMessageLogService>(),
            NullLogger<WhatsAppAdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}
