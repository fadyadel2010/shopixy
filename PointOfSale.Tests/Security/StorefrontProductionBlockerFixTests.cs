using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Events;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Tests.Security;

public sealed class StorefrontProductionBlockerFixTests
{
    [Fact]
    public async Task StorefrontRefreshTokenService_IssueToken_QueuesIssuedEventBeforeCommit()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var dbContext = CreateInMemoryDbContext(tenantId);

        dbContext.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = customerId,
            TenantId = tenantId,
            Email = "checkout.customer@example.test",
            NormalizedEmail = "checkout.customer@example.test",
            Provider = AuthProvider.Local,
            IsActive = true,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var eventPublisher = new SessionEventPublisherBoundaryProbe(dbContext);
        var service = CreateStorefrontRefreshTokenService(dbContext, eventPublisher);

        var result = await service.IssueTokenAsync(
            tenantId,
            customerId,
            "127.0.0.1",
            "test-agent",
            accessTokenJti: Guid.NewGuid().ToString("D"),
            accessTokenExpiresAtUtc: DateTime.UtcNow.AddMinutes(30),
            cancellationToken: CancellationToken.None);

        Assert.True(result.Success, result.Error ?? result.Message);
        Assert.True(eventPublisher.IssuedEventSawPendingToken);
        Assert.NotNull(result.RawRefreshToken);
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow);
        Assert.Equal(customerId, (await dbContext.OnlineCustomerRefreshTokens.SingleAsync()).OnlineCustomerId);
    }

    [Fact]
    public async Task CheckoutSessionController_Recovery_ReturnsStructuredCheckoutState()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var shippingMethodId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        var checkoutSessionService = new Mock<PointOfSale.Core.Interfaces.ICheckoutSessionService>();
        var cartService = new Mock<ICartService>();
        var tokenService = CreateCheckoutSessionTokenService(out _);
        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = cartId,
            SessionId = "guest-session-123",
            RecoveryToken = "raw-recovery-nonce",
            Status = CheckoutSessionStatus.Active,
            CustomerName = "Ada Lovelace",
            Phone = "01000000000",
            Email = "ada@example.com",
            City = "Cairo",
            Area = "Nasr City",
            Address = "42 Test Street",
            Notes = "Leave at door",
            ItemsSnapshotJson = "[{\"variantId\":null,\"productName\":\"Recovered Product\",\"variantName\":null,\"quantity\":2,\"price\":150.0,\"imageUrl\":\"https://cdn.example.com/item.png\"}]",
            CartTotal = 300m,
            Currency = "EGP",
            ShippingMethodId = shippingMethodId,
            PaymentMethodCode = "MANUALWALLET",
            WalletType = "vodafone_cash",
            WalletPhoneNumber = "01011112222",
            ProofImageUrl = "https://cdn.example.com/proof.png",
            CreatedAtUtc = DateTime.UtcNow
        };
        var signedRecoveryToken = tokenService.CreateRecoveryToken(session);

        checkoutSessionService
            .Setup(service => service.GetByIdAsync(session.Id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        checkoutSessionService
            .Setup(service => service.ResolveRecoverySessionAsync(session.Id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        cartService
            .Setup(service => service.GetCartAsync(
                tenantId,
                It.Is<CartOwner>(owner => owner.OwnerType == "session" && owner.OwnerId == "guest-session-123"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartDto
            {
                CartId = cartId,
                TenantId = tenantId,
                OwnerType = "session",
                OwnerId = "guest-session-123",
                Currency = "EGP",
                Subtotal = 300m,
                DiscountTotal = 30m,
                CartPromotionDiscountTotal = 20m,
                GrandTotal = 250m,
                Items =
                [
                    new CartItemDto
                    {
                        ProductId = Guid.NewGuid(),
                        Name = "Recovered Product",
                        Quantity = 2,
                        UnitPrice = 150m,
                        ThumbnailImage = "https://cdn.example.com/item.png"
                    }
                ],
                AppliedCouponCode = "SAVE10",
                AppliedCoupon = new AppliedCouponInfo
                {
                    CouponId = Guid.NewGuid(),
                    Code = "SAVE10",
                    Type = "percentage",
                    Value = 10m,
                    Amount = 30m
                },
                AppliedCartPromotion = new AppliedCartPromotionInfo
                {
                    PromotionId = promotionId,
                    Name = "Spring Sale",
                    Description = "Auto-applied promotion",
                    DiscountType = "fixed",
                    DiscountValue = 20m,
                    DiscountAmount = 20m,
                    Badge = "20 OFF"
                }
            });

        var controller = new CheckoutSessionController(
            checkoutSessionService.Object,
            tokenService,
            cartService.Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.RecoverByToken(signedRecoveryToken, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<CheckoutRecoveryResponse>(ok.Value);
        Assert.Equal(cartId, payload.CartId);
        Assert.Equal("guest-session-123", payload.GuestSessionId);
        Assert.Null(payload.CheckoutToken);
        Assert.Equal("Ada Lovelace", payload.Address.FullName);
        Assert.Equal("42 Test Street", payload.Address.AddressLine);
        Assert.Equal("Leave at door", payload.Notes);
        Assert.Equal(shippingMethodId, payload.ShippingMethod?.Id);
        Assert.Equal("MANUALWALLET", payload.PaymentMethod?.Code);
        Assert.Equal("SAVE10", payload.CouponCode);
        Assert.Equal(30m, payload.CouponDiscount);
        Assert.Equal("percentage", payload.CouponType);
        Assert.Equal(10m, payload.CouponValue);
        Assert.Equal(promotionId, payload.PromotionId);
        Assert.Equal(20m, payload.CartPromotionDiscount);
        Assert.Equal("Spring Sale", payload.PromotionName);
        Assert.Equal("Auto-applied promotion", payload.PromotionDescription);
        Assert.Equal("fixed", payload.PromotionDiscountType);
        Assert.Equal(20m, payload.PromotionDiscountValue);
        Assert.Equal("20 OFF", payload.PromotionBadge);
        var serializedPayload = JsonSerializer.Serialize(payload);
        Assert.DoesNotContain("walletType", serializedPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("walletPhoneNumber", serializedPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proofImageUrl", serializedPayload, StringComparison.OrdinalIgnoreCase);
        var item = Assert.Single(payload.Cart);
        Assert.Equal("Recovered Product", item.ProductName);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public async Task CheckoutSessionController_Recovery_FailsClosedWhenLiveCartCannotBeRevalidated()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkoutSessionService = new Mock<PointOfSale.Core.Interfaces.ICheckoutSessionService>();
        var cartService = new Mock<ICartService>();
        var tokenService = CreateCheckoutSessionTokenService(out _);
        var session = new CheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = cartId,
            SessionId = "guest-session-123",
            RecoveryToken = "raw-recovery-nonce",
            Status = CheckoutSessionStatus.Active,
            ItemsSnapshotJson = "[{\"variantId\":null,\"productName\":\"Stale Product\",\"variantName\":null,\"quantity\":2,\"price\":150.0,\"imageUrl\":\"https://cdn.example.com/stale.png\"}]",
            CartTotal = 300m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow
        };
        var signedRecoveryToken = tokenService.CreateRecoveryToken(session);

        checkoutSessionService
            .Setup(service => service.GetByIdAsync(session.Id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        checkoutSessionService
            .Setup(service => service.ResolveRecoverySessionAsync(session.Id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        cartService
            .Setup(service => service.GetCartAsync(
                tenantId,
                It.Is<CartOwner>(owner => owner.OwnerType == "session" && owner.OwnerId == "guest-session-123"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Live cart unavailable"));

        var controller = new CheckoutSessionController(
            checkoutSessionService.Object,
            tokenService,
            cartService.Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.RecoverByToken(signedRecoveryToken, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(conflict.Value);
        Assert.Equal("RECOVERY_CART_REVALIDATION_FAILED", error.Code);
        var json = JsonSerializer.Serialize(conflict.Value);
        Assert.DoesNotContain("Stale Product", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckoutSessionController_Recovery_RejectsExpiredSignedToken()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var checkoutSessionService = new Mock<PointOfSale.Core.Interfaces.ICheckoutSessionService>(MockBehavior.Strict);
        var tokenService = CreateCheckoutSessionTokenService(out var dataProtectionProvider);
        var expiredToken = CreateExpiredRecoveryToken(dataProtectionProvider, tenantId, sessionId, "expired-nonce");

        var controller = new CheckoutSessionController(
            checkoutSessionService.Object,
            tokenService,
            Mock.Of<ICartService>(),
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.RecoverByToken(expiredToken, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("SESSION_EXPIRED", error.Code);
        
        checkoutSessionService.VerifyNoOtherCalls();
    }

    [Fact]
    public void RecoverCheckoutPage_MustRestoreRecoveredState_And_RecoveredGuestSession()
    {
        // This test verifies the RecoverCheckoutPage implements the critical recovery flow.
        // Anchors assertions to the actual recovery control flow to prevent false positives.
        
        var source = File.ReadAllText(StorefrontPath("src", "pages", "RecoverCheckoutPage.tsx"));

        // STEP 1: Verify the recovery flow sequence
        // This pattern ensures the correct order: recoverCheckout → setSessionId → buildRecoveredCart → setState
        var recoverCheckoutIndex = source.IndexOf("recoverCheckout(token)", StringComparison.Ordinal);
        var setSessionIdIndex = source.IndexOf("setSessionId(session.guestSessionId)", StringComparison.Ordinal);
        var buildRecoveredCartIndex = source.IndexOf("buildRecoveredCart(session)", StringComparison.Ordinal);
        var checkoutSetStateIndex = source.IndexOf("useCheckoutStore.setState(", StringComparison.Ordinal);

        Assert.True(recoverCheckoutIndex >= 0, "Expected RecoverCheckoutPage to call recoverCheckout(token).");
        Assert.True(setSessionIdIndex > recoverCheckoutIndex, "Expected recovered guest session to be restored after recoverCheckout.");
        Assert.True(buildRecoveredCartIndex > setSessionIdIndex, "Expected recovered cart snapshot to be built after restoring the guest session.");
        Assert.True(checkoutSetStateIndex > buildRecoveredCartIndex, "Expected checkout state hydration after building the recovered cart.");
        
        // STEP 2: Verify the checkout store state includes the secure recovery fields
        var checkoutStateStart = source.IndexOf("useCheckoutStore.setState({", StringComparison.Ordinal);
        var checkoutSessionResetIndex = source.IndexOf("checkoutSessionId: null", StringComparison.Ordinal);
        var shippingMethodIndex = source.IndexOf(
            "shippingMethodId: session.shippingMethod?.id ?? session.shippingMethodId ?? null",
            StringComparison.Ordinal);
        var selectedPaymentIndex = source.IndexOf(
            "selectedPaymentMethodCode: recoveredPaymentMethodCode",
            StringComparison.Ordinal);
        var manualWalletTypeIndex = source.IndexOf("manualWalletType: null", StringComparison.Ordinal);
        var manualWalletPhoneIndex = source.IndexOf("manualWalletPhoneNumber: ''", StringComparison.Ordinal);
        var manualWalletProofIndex = source.IndexOf("manualWalletProofFileKey: null", StringComparison.Ordinal);
        var manualWalletTokenIndex = source.IndexOf("manualWalletUploadToken: null", StringComparison.Ordinal);

        Assert.True(checkoutStateStart >= 0, "Expected RecoverCheckoutPage to hydrate checkout state.");
        Assert.True(checkoutSessionResetIndex > checkoutStateStart, "Expected recovered state to reset checkoutSessionId.");
        Assert.True(shippingMethodIndex > checkoutSessionResetIndex, "Expected recovered shipping method to be restored after resetting checkoutSessionId.");
        Assert.True(selectedPaymentIndex > shippingMethodIndex, "Expected recovered payment method to be restored after shipping method.");
        Assert.True(manualWalletTypeIndex > selectedPaymentIndex, "Expected manual wallet type to be cleared during recovery.");
        Assert.True(manualWalletPhoneIndex > manualWalletTypeIndex, "Expected manual wallet phone to be cleared during recovery.");
        Assert.True(manualWalletProofIndex > manualWalletPhoneIndex, "Expected manual wallet proof key to be cleared during recovery.");
        Assert.True(manualWalletTokenIndex > manualWalletProofIndex, "Expected manual wallet upload token to be cleared during recovery.");
        Assert.DoesNotContain("manualWalletProofUrl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("proofImageUrl", source, StringComparison.Ordinal);
        
        // STEP 3: Verify cart restoration treats the backend recovery response as a candidate
        // and forces cart-page convergence through the live cart endpoint.
        var cartRecoveryPattern =
            @"useCartStore\.getState\(\)\.restoreRecoveredCart\s*\(\s*\w+\s*\)";

        Assert.Matches(cartRecoveryPattern, source);
        var cartStoreSource = File.ReadAllText(StorefrontPath("src", "store", "cartStore.ts"));
        Assert.Contains("isInitialized: false", cartStoreSource, StringComparison.Ordinal);
        Assert.Contains("skipNextFetch: false", cartStoreSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverCheckoutPage_MustRestoreRecoveredCouponState_IntoCart()
    {
        var source = File.ReadAllText(StorefrontPath("src", "pages", "RecoverCheckoutPage.tsx"));
        var buildRecoveredCartStart = source.IndexOf("function buildRecoveredCart", StringComparison.Ordinal);
        var recoveredCartReturnIndex = source.IndexOf("return {", buildRecoveredCartStart, StringComparison.Ordinal);

        var appliedCouponCodeIndex = source.IndexOf("appliedCouponCode: session.couponCode ?? null", recoveredCartReturnIndex, StringComparison.Ordinal);
        var appliedCouponIndex = source.IndexOf("appliedCoupon,", appliedCouponCodeIndex, StringComparison.Ordinal);
        var cartPromotionDiscountTotalIndex = source.IndexOf("cartPromotionDiscountTotal: cartPromotionDiscount", appliedCouponIndex, StringComparison.Ordinal);
        var appliedCartPromotionIndex = source.IndexOf("appliedCartPromotion,", cartPromotionDiscountTotalIndex, StringComparison.Ordinal);
        var totalDiscountIndex = source.IndexOf("totalDiscount,", appliedCartPromotionIndex, StringComparison.Ordinal);
        var grandTotalFieldIndex = source.IndexOf("grandTotal,", recoveredCartReturnIndex, StringComparison.Ordinal);

        Assert.True(buildRecoveredCartStart >= 0, "Expected to find buildRecoveredCart in RecoverCheckoutPage.tsx.");
        Assert.True(appliedCouponCodeIndex >= 0, "Expected buildRecoveredCart to set appliedCouponCode from session.couponCode.");
        Assert.True(appliedCouponIndex > appliedCouponCodeIndex, "Expected buildRecoveredCart to include appliedCoupon after appliedCouponCode.");
        Assert.True(cartPromotionDiscountTotalIndex > appliedCouponIndex, "Expected buildRecoveredCart to set cartPromotionDiscountTotal after appliedCoupon.");
        Assert.True(appliedCartPromotionIndex > cartPromotionDiscountTotalIndex, "Expected buildRecoveredCart to include appliedCartPromotion after cartPromotionDiscountTotal.");
        Assert.True(totalDiscountIndex > appliedCartPromotionIndex, "Expected buildRecoveredCart to include totalDiscount after appliedCartPromotion.");
        Assert.True(grandTotalFieldIndex >= 0, "Expected buildRecoveredCart to include grandTotal.");
    }

    [Fact]
    public void RecoverCartPage_MustHydrateRecoveredCartSnapshot_BeforeRedirectingToCart()
    {
        var source = File.ReadAllText(StorefrontPath("src", "pages", "RecoverCartPage.tsx"));

        var recoverCartIndex = source.IndexOf("const response = await recoverCart(token);", StringComparison.Ordinal);
        var snapshotValidationIndex = source.IndexOf("if (!isRecoveredCartSnapshot(response.cartSnapshot)", StringComparison.Ordinal);
        var setSessionIdIndex = source.IndexOf("setSessionId(response.sessionId);", StringComparison.Ordinal);
        var restoreCartIndex = source.IndexOf(
            "useCartStore.getState().restoreRecoveredCart(response.cartSnapshot, response.message);",
            StringComparison.Ordinal);
        var navigateIndex = source.IndexOf("navigate('/cart', { replace: true });", StringComparison.Ordinal);

        Assert.True(recoverCartIndex >= 0, "Expected RecoverCartPage to call recoverCart(token).");
        Assert.True(snapshotValidationIndex > recoverCartIndex, "Expected RecoverCartPage to validate the recovered cart snapshot before using it.");
        Assert.True(setSessionIdIndex > snapshotValidationIndex, "Expected RecoverCartPage to restore the recovered session id after snapshot validation.");
        Assert.True(restoreCartIndex > setSessionIdIndex, "Expected RecoverCartPage to hydrate the cart store before navigating away.");
        Assert.True(navigateIndex > restoreCartIndex, "Expected RecoverCartPage to navigate only after cart hydration succeeds.");
    }

    [Fact]
    public void CartRecoveryUx_MustShowRestoredCartConfirmation_AndAllowDismissal()
    {
        var cartStoreSource = File.ReadAllText(StorefrontPath("src", "store", "cartStore.ts"));
        var cartPageSource = File.ReadAllText(StorefrontPath("src", "pages", "CartPage.tsx"));

        Assert.Contains("recoveryMessage: message ?? 'Your cart has been restored.'", cartStoreSource, StringComparison.Ordinal);
        Assert.Contains("clearRecoveryMessage", cartStoreSource, StringComparison.Ordinal);
        Assert.Contains("selectCartRecoveryMessage", cartPageSource, StringComparison.Ordinal);
        Assert.Contains("clearRecoveryMessage", cartPageSource, StringComparison.Ordinal);
        Assert.Contains("<span>{recoveryMessage}</span>", cartPageSource, StringComparison.Ordinal);
        Assert.Contains("t('common.dismiss')", cartPageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutStore_MustNotClearCartBeforePaymentConfirmation()
    {
        var source = File.ReadAllText(StorefrontPath("src", "store", "checkoutStore.ts"));

        Assert.DoesNotContain("await useCartStore.getState().clearCart()", source, StringComparison.Ordinal);
        Assert.Contains("persistActiveCheckoutOrderContext", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontManualWalletController_UploadProof_MustRequireCheckoutBoundTokenAuthentication()
    {
        var controllerAttributes = typeof(StorefrontManualWalletController).GetCustomAttributes(inherit: true);
        Assert.DoesNotContain(controllerAttributes, attribute => attribute is AllowAnonymousAttribute);

        var method = typeof(StorefrontManualWalletController).GetMethod(nameof(StorefrontManualWalletController.UploadProof));
        Assert.NotNull(method);

        Assert.DoesNotContain(
            method!.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true),
            _ => true);

        var authorize = method.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(authorize);
        Assert.Equal(
            ManualWalletUploadTokenAuthenticationDefaults.SchemeName,
            authorize!.AuthenticationSchemes);
    }

    [Fact]
    public async Task AdminManualPaymentController_GetProofImage_FailsClosed_ForExternalAbsoluteProofUrl_Even_When_PathLooksTrusted()
    {
        var tenantId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();

        await using var dbContext = CreateInMemoryDbContext(tenantId);
        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = paymentIntentId,
            TenantId = tenantId,
            OrderId = Guid.NewGuid(),
            Amount = 100m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Provider = PaymentProvider.ManualWallet,
            PaymentReference = "PAY-EXT-1",
            IdempotencyKey = "ext-proof-1",
            ProofImageUrl = $"https://evil.example.com/tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
        });
        await dbContext.SaveChangesAsync();

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var storageService = new Mock<IR2StorageService>(MockBehavior.Strict);
        var reviewApplicationService = new Mock<IManualPaymentReviewApplicationService>(MockBehavior.Strict);
        var admissionAuthority = new Mock<IManualPaymentCapabilityAdmissionAuthority>();
        admissionAuthority
            .Setup(service => service.AdmitAsync(
                tenantId,
                ManualPaymentOperationalCapability.ProofAccess,
                It.IsAny<ManualPaymentChannel?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManualPaymentTenantConfiguration(
                tenantId,
                Guid.NewGuid(),
                true,
                ManualPaymentEnvironment.Sandbox,
                ManualPaymentChannel.BankTransfer,
                Array.Empty<ManualPaymentChannelConfiguration>(),
                true,
                true,
                true,
                "Transfer manually.",
                true,
                TimeSpan.FromHours(2),
                "test-manual-config"));
        var controller = new AdminManualPaymentController(
            reviewApplicationService.Object,
            admissionAuthority.Object,
            Mock.Of<IManualPaymentExpirationAuthority>(),
            Mock.Of<IManualPaymentRetentionAuthority>(),
            tenantContext,
            dbContext,
            storageService.Object,
            Options.Create(new R2Options()),
            NullLogger<AdminManualPaymentController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
                        },
                        "AdminJwt"))
                }
            }
        };

        var result = await controller.GetProofImage(paymentIntentId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        reviewApplicationService.VerifyNoOtherCalls();
        storageService.VerifyNoOtherCalls();
    }

    [Fact]
    public void CheckoutPage_MustPreserveRecoverableCheckoutState_WhileClearingSensitiveSessionData()
    {
        var source = File.ReadAllText(StorefrontPath("src", "pages", "CheckoutPage.tsx"));

        // CheckoutPage must NOT clear localStorage checkout state (preserves recoverable state)
        Assert.DoesNotContain("localStorage.removeItem('checkout-state')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("clearPersistedCheckoutStateStorage()", source, StringComparison.Ordinal);
        
        // CheckoutPage must NOT directly clear sensitive session storage (store owns cleanup)
        // The store (checkoutStore.ts) is responsible for clearing sensitive data on successful checkout
        Assert.DoesNotContain("clearSensitiveCheckoutSessionStorage", source, StringComparison.Ordinal);
        
        // Verify CheckoutPage calls submitCheckout (which handles cleanup internally)
        Assert.Matches(@"submitCheckout\s*\(\s*\)", source);
    }

    [Fact]
    public void CheckoutStore_MustClearSensitiveSessionData_OnSuccessfulCheckout()
    {
        var source = File.ReadAllText(StorefrontPath("src", "store", "checkoutStore.ts"));
        var submitCheckoutStart = source.IndexOf("submitCheckout: async () => {", StringComparison.Ordinal);
        Assert.True(submitCheckoutStart >= 0, "Expected to find submitCheckout function in checkoutStore.ts.");
        
        // Extract submitCheckout function body using brace-depth counting
        // This prevents false positives from internal object closures (e.g., "orderResult: { ... },")
        var searchStart = submitCheckoutStart + "submitCheckout: async () => {".Length;
        var braceDepth = 1; // We're already inside the opening brace
        var submitCheckoutEnd = searchStart;
        
        for (var i = searchStart; i < source.Length && braceDepth > 0; i++)
        {
            if (source[i] == '{')
            {
                braceDepth++;
            }
            else if (source[i] == '}')
            {
                braceDepth--;
                if (braceDepth == 0)
                {
                    submitCheckoutEnd = i;
                    break;
                }
            }
        }
        
        Assert.True(submitCheckoutEnd > searchStart, "Expected to find the end of submitCheckout function.");
        var submitCheckoutBody = source.Substring(submitCheckoutStart, submitCheckoutEnd - submitCheckoutStart + 1);
        
        // Now search within the extracted function body to verify the cleanup sequence
        var orderResultIndex = submitCheckoutBody.IndexOf("orderResult: {", StringComparison.Ordinal);
        var addressResetIndex = submitCheckoutBody.IndexOf("address: INITIAL_ADDRESS", orderResultIndex, StringComparison.Ordinal);
        var proofResetIndex = submitCheckoutBody.IndexOf("manualWalletProofFileKey: null", addressResetIndex, StringComparison.Ordinal);
        var tokenResetIndex = submitCheckoutBody.IndexOf("manualWalletUploadToken: null", proofResetIndex, StringComparison.Ordinal);
        var sessionResetIndex = submitCheckoutBody.IndexOf("checkoutSessionId: null", tokenResetIndex, StringComparison.Ordinal);
        var clearSensitiveStorageIndex = submitCheckoutBody.IndexOf("clearSensitiveCheckoutSessionStorage();", sessionResetIndex, StringComparison.Ordinal);

        Assert.True(orderResultIndex >= 0, "Expected to set orderResult during successful checkout.");
        Assert.True(addressResetIndex > orderResultIndex, "Expected address to be cleared after orderResult is set.");
        Assert.True(proofResetIndex > addressResetIndex, "Expected manual wallet proof key to be cleared after address reset.");
        Assert.True(tokenResetIndex > proofResetIndex, "Expected manual wallet upload token to be cleared after proof reset.");
        Assert.True(sessionResetIndex > tokenResetIndex, "Expected checkout session id to be cleared after upload token reset.");
        Assert.True(clearSensitiveStorageIndex > sessionResetIndex, "Expected sensitive session storage to be cleared after in-memory state reset.");
    }

    [Fact]
    public void ThemeEditorPage_MustHydrateMaterializedThemes_And_SaveDraftBeforePublish()
    {
        var source = File.ReadAllText(PosWebPath("src", "modules", "storefront", "pages", "ThemeEditorPage.jsx"));

        Assert.Contains("storeConfigService.getThemePresets()", source, StringComparison.Ordinal);
        Assert.Contains("config?.draftTheme", source, StringComparison.Ordinal);
        Assert.Contains("config?.liveTheme", source, StringComparison.Ordinal);

        var persistBeforePublishPattern =
            @"await\s+persistDraftTheme\s*\(\s*\{\s*silent\s*:\s*true\s*\}\s*\)" +
            @"[\s\S]{1,500}" +
            @"await\s+storeConfigService\.publishTheme\s*\(";

        Assert.Matches(persistBeforePublishPattern, source);
    }

    [Fact]
    public void PaymentResolutionPages_MustFinalizeCartOnlyAfterConfirmedSuccess()
    {
        var pendingSource = File.ReadAllText(StorefrontPath("src", "pages", "CheckoutPendingPage.tsx"));
        var confirmationSource = File.ReadAllText(StorefrontPath("src", "pages", "OrderConfirmationPage.tsx"));
        var completionSource = File.ReadAllText(StorefrontPath("src", "features", "checkout", "checkoutCompletion.ts"));

        Assert.Contains("navigateRef.current(nextAction.href, { replace: true });", pendingSource, StringComparison.Ordinal);
        Assert.Contains("void finalizeSuccessfulCheckout(currentOrderId);", pendingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("await finalizeSuccessfulCheckout(currentOrderId);", pendingSource, StringComparison.Ordinal);
        Assert.Contains("void finalizeSuccessfulCheckout(orderId);", confirmationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("return false;", completionSource, StringComparison.Ordinal);
        Assert.Contains("await useCartStore.getState().clearCart()", completionSource, StringComparison.Ordinal);
        Assert.Contains("emitCartUnexpectedClearBlocked(orderId, reason);", completionSource, StringComparison.Ordinal);
        Assert.Contains("if (!hasMatchingActiveOrder)", completionSource, StringComparison.Ordinal);
        Assert.Contains("clearActiveCheckoutOrderContextIfMatches(orderId);", completionSource, StringComparison.Ordinal);
        Assert.Contains("emitCheckoutFinalizeFailure(orderId, failureReason);", completionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentResolutionPages_MustKeepQuarantinedCapturedMoney_OutOfRetrySafeFailure()
    {
        var flowSource = File.ReadAllText(StorefrontPath("src", "features", "checkout", "paymentFlow.ts"));
        var pendingSource = File.ReadAllText(StorefrontPath("src", "pages", "CheckoutPendingPage.tsx"));
        var confirmationSource = File.ReadAllText(StorefrontPath("src", "pages", "OrderConfirmationPage.tsx"));
        var pendingLocaleSource = File.ReadAllText(StorefrontPath("src", "i18n", "locales", "en.json"));

        Assert.Contains("'MANUAL_REVIEW'", flowSource, StringComparison.Ordinal);
        Assert.Contains("return 'manual_review';", flowSource, StringComparison.Ordinal);
        Assert.Contains("return { kind: 'manual_review' };", flowSource, StringComparison.Ordinal);

        var pendingManualReviewStart = pendingSource.IndexOf("} else if (nextAction.kind === 'manual_review') {", StringComparison.Ordinal);
        var pendingFailedStart = pendingSource.IndexOf("} else if (nextAction.kind === 'failed') {", StringComparison.Ordinal);
        Assert.True(pendingManualReviewStart >= 0, "Expected CheckoutPendingPage to handle a manual-review action.");
        Assert.True(pendingFailedStart > pendingManualReviewStart, "Expected failed handling to remain separate from manual review.");

        var pendingManualReviewSegment = pendingSource.Substring(pendingManualReviewStart, pendingFailedStart - pendingManualReviewStart);
        Assert.Contains("setRequiresManualReview(true);", pendingManualReviewSegment, StringComparison.Ordinal);
        Assert.DoesNotContain("clearActiveCheckoutOrderContextIfMatches", pendingManualReviewSegment, StringComparison.Ordinal);
        Assert.Contains("t('checkout.pending.reviewDescription')", pendingSource, StringComparison.Ordinal);
        Assert.Contains("Do not retry payment from checkout.", pendingLocaleSource, StringComparison.Ordinal);

        var confirmationManualReviewStart = confirmationSource.IndexOf("if (screen === 'manual_review') {", StringComparison.Ordinal);
        var confirmationFailedStart = confirmationSource.IndexOf("if (screen === 'failed') {", StringComparison.Ordinal);
        Assert.True(confirmationManualReviewStart >= 0, "Expected OrderConfirmationPage to render a manual-review screen.");
        Assert.True(confirmationFailedStart > confirmationManualReviewStart, "Expected failed handling to remain separate from manual review.");

        var confirmationManualReviewSegment = confirmationSource.Substring(
            confirmationManualReviewStart,
            confirmationFailedStart - confirmationManualReviewStart);
        Assert.Contains("title=\"Payment requires review\"", confirmationManualReviewSegment, StringComparison.Ordinal);
        Assert.Contains("Do not retry payment from checkout", confirmationManualReviewSegment, StringComparison.Ordinal);
        Assert.Contains("primaryLabel=\"Refresh status\"", confirmationManualReviewSegment, StringComparison.Ordinal);
        Assert.DoesNotContain("navigate('/checkout')", confirmationManualReviewSegment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckoutController_WhenManualWalletIsNotConfigured_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        const string requestId = "11111111-1111-1111-1111-111111111111";
        var checkoutService = new Mock<ICheckoutService>();

        checkoutService
            .Setup(service => service.ProcessCheckoutAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                requestId,
                It.IsAny<CheckoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CheckoutManualWalletNotConfiguredException(tenantId));

        var controller = CreateCheckoutController(
            checkoutService.Object,
            CreateAuthenticationService());
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        controller.Request.Headers["X-Session-Id"] = "guest-session";
        controller.Request.Headers["Idempotency-Key"] = requestId;
        controller.Request.Headers["X-Request-Id"] = requestId;

        var request = ValidCheckoutRequest();
        request.PaymentMethod = "MANUALWALLET";
        request.WalletType = "VodafoneCash";
        request.WalletPhoneNumber = "01011112222";
        request.ProofFileKey = "tenants/test/manual-wallet-proofs/proof.png";

        var result = await controller.Checkout(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("MANUAL_WALLET_NOT_CONFIGURED", error.Code);
    }

    [Fact]
    public void CheckoutService_MustRequireManualWalletConfiguration_BeforeCreatingIntent()
    {
        // This test verifies CheckoutService validates manual wallet configuration BEFORE creating payment intent.
        // It ensures the guard logic is in the correct control flow, not just present somewhere in the file.
        
        var source = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));

        // STEP 1: Verify the guard method exists and contains failure logging
        // Match the method signature and ensure LogManualWalletConfigurationFailure is called within it
        var guardMethodPattern = @"ResolveRequiredManualWalletDetailsAsync\s*\([^)]*\)\s*\{[\s\S]*?LogManualWalletConfigurationFailure\s*\([\s\S]*?\}";
        Assert.Matches(guardMethodPattern, source);
        
        // STEP 2: Verify the checkout flow calls the guard and assigns results BEFORE creating payment intent
        // This pattern ensures:
        // - ResolveRequiredManualWalletDetailsAsync is called
        // - Its result is captured in configuredWallet
        // - merchantWalletNumber is assigned from configuredWallet.MerchantWalletNumber
        // - walletInstructions is assigned from configuredWallet.WalletInstructions
        // - These assignments happen BEFORE CreatePaymentIntentAsync
        var guardCallIndex = source.IndexOf("var configuredWallet = await ResolveRequiredManualWalletDetailsAsync(", StringComparison.Ordinal);
        var merchantAssignmentIndex = source.IndexOf("merchantWalletNumber = configuredWallet.MerchantWalletNumber", StringComparison.Ordinal);
        var instructionsAssignmentIndex = source.IndexOf("walletInstructions = configuredWallet.WalletInstructions", StringComparison.Ordinal);
        var sessionCreationIndex = source.IndexOf("var session = await CreateCheckoutPaymentSessionAsync(", StringComparison.Ordinal);

        Assert.True(guardCallIndex >= 0, "Expected checkout flow to call ResolveRequiredManualWalletDetailsAsync.");
        Assert.True(merchantAssignmentIndex > guardCallIndex, "Expected merchant wallet number to come from configured SQL payment method.");
        Assert.True(instructionsAssignmentIndex > merchantAssignmentIndex, "Expected wallet instructions to come from configured SQL payment method.");
        Assert.True(sessionCreationIndex > instructionsAssignmentIndex, "Expected manual wallet configuration to be resolved before provider session creation.");
    }

    [Fact]
    public async Task StorefrontAccountController_RefreshTokenHash_UsesCentralizedCookieName()
    {
        const string refreshToken = "refresh-token-value";
        using var sha256 = SHA256.Create();
        var expectedHash = Convert.ToBase64String(
            sha256.ComputeHash(Encoding.UTF8.GetBytes(refreshToken)));

        await using var dbContext = CreateInMemoryDbContext();
        var controller = new StorefrontAccountController(
            dbContext,
            Mock.Of<IStorefrontRefreshTokenService>(),
            Mock.Of<IStorefrontJwtBlacklist>(),
            Mock.Of<Microsoft.AspNetCore.Identity.IPasswordHasher<OnlineCustomer>>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IOrderReadRepository>(),
            Mock.Of<PointOfSale.API.Services.Storefront.IStorefrontOrderProjectionRepairService>(),
            Mock.Of<IPhoneRiskProfileService>(),
            NullLogger<StorefrontAccountController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Request.Headers["Cookie"] =
            $"{AuthCookieConfiguration.StorefrontRefreshTokenCookieName}={refreshToken}";

        var method = typeof(StorefrontAccountController).GetMethod(
            "GetCurrentRefreshTokenHash",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var hash = Assert.IsType<string>(method!.Invoke(controller, null));
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public async Task StorefrontOnlineCustomerController_EmailExists_ReturnsGenericMessage_WithoutLookup()
    {
        var tenantId = Guid.NewGuid();
        var customerService = new Mock<IStorefrontOnlineCustomerService>(MockBehavior.Strict);
        var captchaGuard = new Mock<IStorefrontCaptchaGuard>();
        captchaGuard
            .Setup(guard => guard.ValidateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontCaptchaValidationResult.Success());

        var controller = new StorefrontOnlineCustomerController(
            customerService.Object,
            captchaGuard.Object,
            NullLogger<StorefrontOnlineCustomerController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = CreateTenantRequestServices(tenantId)
                }
            }
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var first = await controller.CheckEmailExists(
            new AccountRecoveryRequest { Email = "known@example.com" },
            CancellationToken.None);
        var second = await controller.CheckEmailExists(
            new AccountRecoveryRequest { Email = "unknown@example.com" },
            CancellationToken.None);

        var firstOk = Assert.IsType<OkObjectResult>(first);
        var secondOk = Assert.IsType<OkObjectResult>(second);
        var firstPayload = Assert.IsType<AccountRecoveryMessageResponse>(firstOk.Value);
        var secondPayload = Assert.IsType<AccountRecoveryMessageResponse>(secondOk.Value);

        Assert.Equal("If the email exists, we sent instructions.", firstPayload.Message);
        Assert.Equal(firstPayload.Message, secondPayload.Message);

        customerService.VerifyNoOtherCalls();
    }

    [Fact]
    public void StorefrontOnlineCustomerController_EmailExists_MustUsePublicReadRateLimit()
    {
        var method = typeof(StorefrontOnlineCustomerController).GetMethod(
            nameof(StorefrontOnlineCustomerController.CheckEmailExists),
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true);
        Assert.NotNull(attribute);
        Assert.Equal("PublicReadRateLimit", attribute!.PolicyName);
    }

    [Fact]
    public async Task StorefrontPasswordResetService_WhenRateLimited_ReturnsGenericMessage()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateInMemoryDbContext();
        var rateLimiter = new Mock<IStorefrontAuthRateLimiter>();
        rateLimiter
            .Setup(limiter => limiter.CheckPasswordResetAsync(
                tenantId,
                It.IsAny<string>(),
                "127.0.0.1"))
            .ReturnsAsync(RateLimitResult.Deny(4, 3, 900));

        var service = new StorefrontPasswordResetService(
            dbContext,
            Options.Create(new StorefrontJwtSettings()),
            Mock.Of<IStorefrontRefreshTokenService>(),
            Mock.Of<IStorefrontJwtBlacklist>(),
            rateLimiter.Object,
            Mock.Of<Microsoft.AspNetCore.Identity.IPasswordHasher<OnlineCustomer>>(),
            Mock.Of<IEventPublisher>(),
            NullLogger<StorefrontPasswordResetService>.Instance);

        var result = await service.ForgotPasswordAsync(
            tenantId,
            "person@example.com",
            "127.0.0.1",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("If the email exists, we sent instructions.", result.Message);
        Assert.False(await dbContext.OnlineCustomerPasswordResetTokens.AnyAsync());
    }

    private static CheckoutController CreateCheckoutController(
        ICheckoutService checkoutService,
        IAuthenticationService authenticationService)
    {
        var services = new ServiceCollection()
            .AddSingleton(authenticationService)
            .BuildServiceProvider();

        return new CheckoutController(
            checkoutService,
            Mock.Of<ICheckoutAttachmentService>(),
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services
                }
            }
        };
    }

    private static IAuthenticationService CreateAuthenticationService(
        Guid? storefrontCustomerId = null,
        Guid? customerJwtCustomerId = null)
    {
        var authenticationService = new Mock<IAuthenticationService>();

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(CreateAuthenticateResult(storefrontCustomerId, "StorefrontJwt"));

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                CustomerSecurityConstants.AuthenticationScheme))
            .ReturnsAsync(CreateAuthenticateResult(
                customerJwtCustomerId,
                CustomerSecurityConstants.AuthenticationScheme));

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                JwtBearerDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());

        return authenticationService.Object;
    }

    private static AuthenticateResult CreateAuthenticateResult(Guid? customerId, string scheme)
    {
        if (!customerId.HasValue)
        {
            return AuthenticateResult.NoResult();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("CustomerId", customerId.Value.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString("D"))
            },
            scheme));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, scheme));
    }

    private static CheckoutRequestDto ValidCheckoutRequest() => new()
    {
        CartId = Guid.NewGuid(),
        ShippingMethodId = Guid.NewGuid(),
        PaymentMethod = "STRIPE",
        ExpectedPricing = new CheckoutExpectedPricingDto
        {
            Subtotal = 100m,
            Shipping = 0m,
            Tax = 0m,
            Discount = 0m,
            Total = 100m,
            CouponDiscount = 0m,
            PromotionDiscount = 0m,
            CartPromotionDiscount = 0m,
            PaymentFee = 0m,
            ManualDiscount = 0m
        },
        ShippingAddress = new AddressDto
        {
            Name = "Test User",
            Phone = "01000000000",
            Street = "123 Test St",
            City = "Cairo",
            Country = "EG"
        }
    };

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ApplicationDbContext CreateInMemoryDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }

    private static StorefrontRefreshTokenService CreateStorefrontRefreshTokenService(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher)
    {
        var jwtBlacklist = new Mock<IStorefrontJwtBlacklist>();
        var rateLimiter = new Mock<IStorefrontAuthRateLimiter>();
        var phoneRiskProfileService = new Mock<IPhoneRiskProfileService>();

        return new StorefrontRefreshTokenService(
            dbContext,
            Options.Create(new StorefrontJwtSettings
            {
                Secret = new string('s', 64),
                Issuer = "StorefrontAPI",
                Audience = "StorefrontClient",
                AccessTokenExpirationMinutes = 30,
                RefreshTokenExpirationDays = 7
            }),
            jwtBlacklist.Object,
            rateLimiter.Object,
            eventPublisher,
            phoneRiskProfileService.Object,
            NullLogger<StorefrontRefreshTokenService>.Instance);
    }

    private sealed class SessionEventPublisherBoundaryProbe(ApplicationDbContext dbContext) : IEventPublisher
    {
        public bool IssuedEventSawPendingToken { get; private set; }

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            if (@event is OnlineCustomerRefreshTokenIssuedEvent issuedEvent)
            {
                IssuedEventSawPendingToken = dbContext.ChangeTracker
                    .Entries<OnlineCustomerRefreshToken>()
                    .Any(entry => entry.Entity.Id == issuedEvent.TokenId && entry.State == EntityState.Added);

                if (!IssuedEventSawPendingToken)
                {
                    throw new InvalidOperationException("Refresh-token issued event was not queued before the token mutation was committed.");
                }
            }

            return Task.CompletedTask;
        }

        public async Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                await PublishAsync(@event, cancellationToken);
            }
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent => PublishAsync(@event, cancellationToken);
    }

    private static IServiceProvider CreateTenantRequestServices(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();
    }

    private static ICheckoutSessionTokenService CreateCheckoutSessionTokenService(out IDataProtectionProvider dataProtectionProvider)
    {
        dataProtectionProvider = new EphemeralDataProtectionProvider();
        return new CheckoutSessionTokenService(
            dataProtectionProvider,
            Options.Create(new CheckoutSessionSecurityOptions
            {
                RecoveryTokenTtlMinutes = 20,
                PaymentTokenTtlMinutes = 20,
                ManualWalletUploadTokenTtlMinutes = 20
            }),
            NullLogger<CheckoutSessionTokenService>.Instance);
    }

    private static string CreateExpiredRecoveryToken(
        IDataProtectionProvider dataProtectionProvider,
        Guid tenantId,
        Guid sessionId,
        string nonce)
    {
        var protector = dataProtectionProvider.CreateProtector("CheckoutSessionTokens.v1");
        var payload = JsonSerializer.Serialize(new
        {
            tenantId,
            subject = sessionId.ToString("N"),
            purpose = "checkout-recovery",
            nonce,
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var protectedPayload = protector.Protect(payload);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }
                .Concat(parts)
                .ToArray()));

    private static string StorefrontPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Storefront" }
                .Concat(parts)
                .ToArray()));

    private static string PosWebPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.Web" }
                .Concat(parts)
                .ToArray()));
}
