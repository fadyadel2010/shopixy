using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Infrastructure.WhatsApp.Transport;

namespace PointOfSale.Tests.Infrastructure;

public sealed class WhatsAppTransportAuthorityTests
{
    [Fact]
    public async Task PlaywrightTransport_SendMessage_UsesDispatcherAuthorizedAccount()
    {
        var tenantId = Guid.NewGuid();
        var authorizedAccountId = Guid.NewGuid();
        var defaultAccountId = Guid.NewGuid();

        var authorizedAccount = new WhatsAppAccount
        {
            Id = authorizedAccountId,
            TenantId = tenantId,
            PhoneNumber = "+201000000000",
            DisplayName = "Authorized",
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            UseForOtpVerification = true
        };

        var defaultAccount = new WhatsAppAccount
        {
            Id = defaultAccountId,
            TenantId = tenantId,
            PhoneNumber = "+202000000000",
            DisplayName = "Default",
            Status = WhatsAppAccountStatus.Connected,
            Enabled = true,
            UseForOrderConfirmation = true
        };

        var engine = new Mock<IWhatsAppWebEngine>();
        engine
            .Setup(x => x.CheckConnectionAsync(authorizedAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConnectionStatus(true, WhatsAppAccountStatus.Connected, DateTime.UtcNow, "+201000000000"));
        engine
            .Setup(x => x.SendMessageAsync(authorizedAccountId, "+201111111111", "message", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult(true, "provider-message", null));

        var accounts = new Mock<IWhatsAppAccountService>();
        accounts
            .Setup(x => x.GetTenantAccountAsync(tenantId, authorizedAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authorizedAccount);
        accounts
            .Setup(x => x.GetActiveAccountForUseCaseAsync(tenantId, WhatsAppUseCase.OrderConfirmation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultAccount);

        var transport = new PlaywrightTransport(
            engine.Object,
            accounts.Object,
            NullLogger<PlaywrightTransport>.Instance);

        var result = await transport.SendMessageAsync(
            tenantId.ToString(),
            "+201111111111",
            "message",
            WhatsAppMessageType.Text,
            accountId: authorizedAccountId.ToString());

        Assert.True(result.Success);
        engine.Verify(
            x => x.SendMessageAsync(authorizedAccountId, "+201111111111", "message", It.IsAny<CancellationToken>()),
            Times.Once);
        engine.Verify(
            x => x.SendMessageAsync(defaultAccountId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
