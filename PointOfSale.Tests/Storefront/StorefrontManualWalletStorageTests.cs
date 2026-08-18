using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontManualWalletStorageTests
{
    [Fact]
    public async Task GetConfig_ReturnsConfiguredWalletChannelsAndProofPolicy()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var repository = new Mock<IPaymentMethodReadRepository>();
        repository
            .Setup(service => service.GetByCodeAsync(tenantId, "MANUALWALLET", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentMethodDocument
            {
                TenantId = tenantId,
                PaymentMethodId = Guid.NewGuid(),
                Code = "MANUALWALLET",
                IsEnabled = true,
                IsConfigurationValid = true,
                PublicConfiguration = new Dictionary<string, string>
                {
                    ["walletNumber"] = "01000000000"
                }
            });
        var manualOptions = CreateManualPaymentOptions() with
        {
            Proof = new ManualPaymentProofConfiguration
            {
                AcceptedContentTypes = new[] { "image/webp" },
                MaximumUploadBytes = 7 * 1024 * 1024
            }
        };

        var controller = new StorefrontManualWalletController(
            tenantContext,
            Mock.Of<IR2StorageService>(),
            repository.Object,
            CreateAdmittingAuthority(tenantId),
            Mock.Of<IFileValidationService>(),
            Mock.Of<ICheckoutSessionService>(),
            Options.Create(new R2Options()),
            Options.Create(manualOptions),
            NullLogger<StorefrontManualWalletController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.GetConfig(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<StorefrontManualWalletController.ManualWalletConfigResponse>(ok.Value);
        var walletType = Assert.Single(response.WalletTypes);
        Assert.Equal("VODAFONECASH", walletType.Code);
        Assert.Equal("01000000000", response.WalletNumber);
        Assert.Equal(7 * 1024 * 1024, response.MaxFileBytes);
        Assert.Equal(new[] { "image/webp" }, response.AllowedMimeTypes);
        Assert.Equal(new[] { ".webp" }, response.AllowedExtensions);
    }

    [Fact]
    public async Task UploadProof_WhenValidationSucceeds_ReturnsTrustedFileKey()
    {
        var tenantId = Guid.NewGuid();
        var checkoutSessionId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var session = new CheckoutSession
        {
            Id = checkoutSessionId,
            TenantId = tenantId,
            CartId = Guid.NewGuid(),
            SessionId = "guest-session-1",
            Status = CheckoutSessionStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow
        };

        var objectKey = $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{checkoutSessionId:D}/manual-wallet-proof.jpg";
        var storageService = new Mock<IR2StorageService>();
        storageService
            .Setup(service => service.UploadFileAsync(
                It.IsAny<Stream>(),
                "image/jpeg",
                $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{checkoutSessionId:D}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2UploadResult(
                null,
                "manual-wallet-proof.jpg",
                objectKey));

        var fileValidationService = new Mock<IFileValidationService>();
        fileValidationService
            .Setup(service => service.ValidateFileAsync(
                It.IsAny<Stream>(),
                "proof.jpg",
                "image/jpeg",
                It.Is<IReadOnlyCollection<AllowedFileType>>(types =>
                    types.SequenceEqual(FileValidationProfiles.ManualWallet)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileValidationResult.Success(
                ".jpg",
                "image/jpeg",
                "proof.jpg",
                AllowedFileType.Jpeg));

        var checkoutSessionService = new Mock<ICheckoutSessionService>();
        checkoutSessionService
            .Setup(service => service.ResolveActiveSessionAsync(checkoutSessionId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        checkoutSessionService
            .Setup(service => service.SetManualWalletProofAsync(
                checkoutSessionId,
                tenantId,
                objectKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutSession
            {
                Id = checkoutSessionId,
                TenantId = tenantId,
                Status = CheckoutSessionStatus.Active,
                ProofImageUrl = objectKey
            });

        var controller = new StorefrontManualWalletController(
            tenantContext,
            storageService.Object,
            Mock.Of<IPaymentMethodReadRepository>(),
            CreateAdmittingAuthority(tenantId),
            fileValidationService.Object,
            checkoutSessionService.Object,
            Options.Create(new R2Options()),
            Options.Create(CreateManualPaymentOptions()),
            NullLogger<StorefrontManualWalletController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(
                                ManualWalletUploadTokenAuthenticationDefaults.CheckoutSessionIdClaimType,
                                checkoutSessionId.ToString("D")),
                            new Claim(
                                ManualWalletUploadTokenAuthenticationDefaults.RecoveryNonceClaimType,
                                session.RecoveryToken)
                        },
                        ManualWalletUploadTokenAuthenticationDefaults.SchemeName))
                }
            }
        };

        await using var stream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });
        var file = new FormFile(stream, 0, stream.Length, "file", "proof.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var result = await controller.UploadProof(
            new StorefrontManualWalletController.UploadManualWalletProofRequest
            {
                CheckoutSessionId = checkoutSessionId,
                File = file
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StorefrontManualWalletController.ManualWalletUploadProofResponse>(ok.Value);

        Assert.Equal(objectKey, payload.FileKey);
        Assert.DoesNotContain("http", payload.FileKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadProof_WhenSessionWasReconciledAway_ReturnsNotFound_AndDoesNotUpload()
    {
        var tenantId = Guid.NewGuid();
        var checkoutSessionId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var storageService = new Mock<IR2StorageService>(MockBehavior.Strict);
        var fileValidationService = new Mock<IFileValidationService>(MockBehavior.Strict);
        var checkoutSessionService = new Mock<ICheckoutSessionService>();
        checkoutSessionService
            .Setup(service => service.ResolveActiveSessionAsync(checkoutSessionId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutSession?)null);

        var controller = new StorefrontManualWalletController(
            tenantContext,
            storageService.Object,
            Mock.Of<IPaymentMethodReadRepository>(),
            Mock.Of<IManualPaymentCapabilityAdmissionAuthority>(),
            fileValidationService.Object,
            checkoutSessionService.Object,
            Options.Create(new R2Options()),
            Options.Create(CreateManualPaymentOptions()),
            NullLogger<StorefrontManualWalletController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(
                                ManualWalletUploadTokenAuthenticationDefaults.CheckoutSessionIdClaimType,
                                checkoutSessionId.ToString("D")),
                            new Claim(
                                ManualWalletUploadTokenAuthenticationDefaults.RecoveryNonceClaimType,
                                "stale-nonce")
                        },
                        ManualWalletUploadTokenAuthenticationDefaults.SchemeName))
                }
            }
        };

        await using var stream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });
        var file = new FormFile(stream, 0, stream.Length, "file", "proof.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var result = await controller.UploadProof(
            new StorefrontManualWalletController.UploadManualWalletProofRequest
            {
                CheckoutSessionId = checkoutSessionId,
                File = file
            },
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("CHECKOUT_SESSION_NOT_FOUND", payload.Code);
        storageService.VerifyNoOtherCalls();
        fileValidationService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClearProof_WhenSessionWasReconciledAway_ReturnsNotFound_AndDoesNotClear()
    {
        var tenantId = Guid.NewGuid();
        var checkoutSessionId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var storageService = new Mock<IR2StorageService>(MockBehavior.Strict);
        var fileValidationService = new Mock<IFileValidationService>(MockBehavior.Strict);
        var checkoutSessionService = new Mock<ICheckoutSessionService>();
        checkoutSessionService
            .Setup(service => service.ResolveActiveSessionAsync(checkoutSessionId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutSession?)null);

        var controller = new StorefrontManualWalletController(
            tenantContext,
            storageService.Object,
            Mock.Of<IPaymentMethodReadRepository>(),
            Mock.Of<IManualPaymentCapabilityAdmissionAuthority>(),
            fileValidationService.Object,
            checkoutSessionService.Object,
            Options.Create(new R2Options()),
            Options.Create(CreateManualPaymentOptions()),
            NullLogger<StorefrontManualWalletController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(
                                ManualWalletUploadTokenAuthenticationDefaults.CheckoutSessionIdClaimType,
                                checkoutSessionId.ToString("D")),
                            new Claim(
                                ManualWalletUploadTokenAuthenticationDefaults.RecoveryNonceClaimType,
                                "stale-nonce")
                        },
                        ManualWalletUploadTokenAuthenticationDefaults.SchemeName))
                }
            }
        };

        var result = await controller.ClearProof(checkoutSessionId, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("CHECKOUT_SESSION_NOT_FOUND", payload.Code);
        storageService.VerifyNoOtherCalls();
        fileValidationService.VerifyNoOtherCalls();
    }

    private static IManualPaymentCapabilityAdmissionAuthority CreateAdmittingAuthority(Guid tenantId)
    {
        var authority = new Mock<IManualPaymentCapabilityAdmissionAuthority>();
        authority
            .Setup(service => service.AdmitAsync(
                tenantId,
                It.IsAny<ManualPaymentOperationalCapability>(),
                It.IsAny<ManualPaymentChannel?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManualPaymentTenantConfiguration(
                tenantId,
                Guid.NewGuid(),
                true,
                ManualPaymentEnvironment.Sandbox,
                ManualPaymentChannel.VodafoneCash,
                new[]
                {
                    new ManualPaymentChannelConfiguration
                    {
                        Channel = ManualPaymentChannel.VodafoneCash,
                        Enabled = true,
                        DisplayName = "Vodafone Cash",
                        Instructions = "Transfer to the configured wallet."
                    }
                },
                true,
                true,
                true,
                "Transfer manually.",
                true,
                TimeSpan.FromHours(2),
                "test-manual-config"));

        return authority.Object;
    }

    private static ManualPaymentOptions CreateManualPaymentOptions() => new()
    {
        Proof = new ManualPaymentProofConfiguration
        {
            AcceptedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" },
            MaximumUploadBytes = FileValidationProfiles.MaxManualWalletProofBytes
        }
    };
}
