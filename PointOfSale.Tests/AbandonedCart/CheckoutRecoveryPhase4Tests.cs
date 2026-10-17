using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Tests.AbandonedCart;

public sealed class CheckoutRecoveryPhase4Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recovery_RequiresExactCurrentCart_AndUsesCurrentTotals(bool replacedCart)
    {
        var session = new CheckoutSession { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CartId = Guid.NewGuid(), SessionId = "P4QA", RecoveryToken = "test-only", CartTotal = 99m, Currency = "EGP" };
        var sessions = new Mock<ICheckoutSessionService>();
        sessions.Setup(x => x.GetByIdAsync(session.Id, session.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        sessions.Setup(x => x.ResolveRecoverySessionAsync(session.Id, session.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var cart = new CartDto { CartId = replacedCart ? Guid.NewGuid() : session.CartId, TenantId = session.TenantId, OwnerType = "session", OwnerId = session.SessionId, GrandTotal = 25m, Currency = "EGP" };
        var carts = new Mock<ICartService>();
        carts.Setup(x => x.GetCartAsync(session.TenantId, It.IsAny<CartOwner>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(cart);
        var tokens = new CheckoutSessionTokenService(new EphemeralDataProtectionProvider(), Options.Create(new CheckoutSessionSecurityOptions()), NullLogger<CheckoutSessionTokenService>.Instance);
        var controller = new CheckoutSessionController(sessions.Object, tokens, carts.Object, new CommerceReliabilityTelemetry(), NullLogger<CheckoutSessionController>.Instance) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers["X-Tenant-Id"] = session.TenantId.ToString();
        var result = await controller.RecoverByToken(tokens.CreateRecoveryToken(session), CancellationToken.None);
        if (replacedCart) Assert.IsType<ConflictObjectResult>(result);
        else Assert.Equal(25m, Assert.IsType<CheckoutRecoveryResponse>(Assert.IsType<OkObjectResult>(result).Value).CartTotal);
    }
}
