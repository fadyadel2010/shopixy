using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Jobs;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Checkout;

public sealed class CheckoutAttachmentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public CheckoutAttachmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateUploadLease_UsesServerGeneratedTenantScopedObjectKey()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock(objectKey: $"tenants/{tenantId:N}/checkout/{cartId:N}/fields/{field.Id:N}/attachments/2026/08/09/server.jpg");
        var service = CreateService(storage, cartId, tenantId);

        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentUploadLeaseRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                FileName = "../../customer-image.jpg",
                ContentType = "image/jpeg",
                FileSizeBytes = 1024
            });

        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.StartsWith($"tenants/{tenantId:N}/checkout/{cartId:N}/fields/{field.Id:N}/attachments/", attachment.ObjectKey);
        Assert.DoesNotContain("customer-image", attachment.ObjectKey, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("customer-image.jpg", attachment.OriginalFileName);
        Assert.Equal(tenantId, attachment.TenantId);
        Assert.Equal(cartId, attachment.CartId);
        Assert.Equal(field.Id, attachment.CheckoutFieldDefinitionId);
        Assert.Equal("guest-session", attachment.OwnerId);
        Assert.Equal("session", attachment.OwnerType);
        Assert.Equal("guest-session", attachment.SessionId);
        Assert.True(lease.ExpiresAtUtc < lease.AttachmentExpiresAtUtc);
    }

    [Fact]
    public void CheckoutAttachment_HasTenantAndSoftDeleteQueryFilter()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(CheckoutAttachment));

        Assert.NotNull(entityType?.GetQueryFilter());
    }

    [Fact]
    public async Task CreateUploadLease_RejectsInvalidFieldType()
    {
        var tenantId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Text);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.CreateUploadLeaseAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                NewLeaseRequest(field.Id, Guid.NewGuid(), "image.jpg", "image/jpeg", 1024)));

        Assert.Equal("CHECKOUT_ATTACHMENT_FIELD_TYPE_INVALID", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUploadLease_RejectsWrongTenantField()
    {
        var field = await SeedFieldAsync(Guid.NewGuid(), CheckoutFieldType.Image);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.CreateUploadLeaseAsync(
                Guid.NewGuid(),
                "guest-session",
                "session",
                "guest-session",
                null,
                NewLeaseRequest(field.Id, Guid.NewGuid(), "image.jpg", "image/jpeg", 1024)));

        Assert.Equal("CHECKOUT_ATTACHMENT_FIELD_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUploadLease_RejectsInvalidMimeType()
    {
        var tenantId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.CreateUploadLeaseAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                NewLeaseRequest(field.Id, Guid.NewGuid(), "script.txt", "text/plain", 1024)));

        Assert.Equal("CHECKOUT_ATTACHMENT_TYPE_NOT_ALLOWED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUploadLease_RejectsOversizedFile()
    {
        var tenantId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image, maxFileSize: 512);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.CreateUploadLeaseAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                NewLeaseRequest(field.Id, Guid.NewGuid(), "image.jpg", "image/jpeg", 1024)));

        Assert.Equal("CHECKOUT_ATTACHMENT_TOO_LARGE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUploadLease_RejectsDisabledField()
    {
        var tenantId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.File, isEnabled: false);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.CreateUploadLeaseAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                new CheckoutAttachmentUploadLeaseRequestDto
                {
                    CartId = Guid.NewGuid(),
                    FieldId = field.Id,
                    FileName = "evidence.pdf",
                    ContentType = "application/pdf",
                    FileSizeBytes = 1024
                }));

        Assert.Equal("CHECKOUT_ATTACHMENT_FIELD_DISABLED", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUploadLease_RejectsNonexistentCartAndDoesNotCreateOrphanLease()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage);

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.CreateUploadLeaseAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024)));

        Assert.Equal("CHECKOUT_ATTACHMENT_CART_NOT_AUTHORIZED", ex.ErrorCode);
        Assert.Empty(await _dbContext.CheckoutAttachments.IgnoreQueryFilters().ToListAsync());
        storage.Verify(
            item => item.CreateUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateUploadLease_RejectsCartBelongingToAnotherGuestSession()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId, ownerId: "other-session", ownerType: "session");

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.CreateUploadLeaseAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024)));

        Assert.Equal("CHECKOUT_ATTACHMENT_CART_NOT_AUTHORIZED", ex.ErrorCode);
        Assert.Empty(await _dbContext.CheckoutAttachments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ConfirmUpload_RejectsMetadataSizeMismatch()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock(contentLength: 2048);
        var service = CreateService(storage, cartId, tenantId);

        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentUploadLeaseRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                FileName = "image.jpg",
                ContentType = "image/jpeg",
                FileSizeBytes = 1024
            });

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ConfirmUploadAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                new CheckoutAttachmentConfirmRequestDto
                {
                    CartId = cartId,
                    FieldId = field.Id,
                    AttachmentId = lease.AttachmentId
                }));

        Assert.Equal("CHECKOUT_ATTACHMENT_SIZE_MISMATCH", ex.ErrorCode);
        _dbContext.ChangeTracker.Clear();
        var failed = await _dbContext.CheckoutAttachments
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.Equal(CheckoutAttachmentStatus.Failed, failed.Status);
    }

    [Fact]
    public async Task ConfirmUpload_ContentTypeMismatch_IsPersistedAsTerminalFailure()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock(contentType: "application/pdf");
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ConfirmUploadAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                new CheckoutAttachmentConfirmRequestDto
                {
                    CartId = cartId,
                    FieldId = field.Id,
                    AttachmentId = lease.AttachmentId
                }));

        Assert.Equal("CHECKOUT_ATTACHMENT_CONTENT_TYPE_MISMATCH", ex.ErrorCode);
        _dbContext.ChangeTracker.Clear();
        var failed = await _dbContext.CheckoutAttachments
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.Equal(CheckoutAttachmentStatus.Failed, failed.Status);
    }

    [Fact]
    public async Task ConfirmUpload_FinalizesAttachmentAndPreservesMetadata()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        var confirmed = await service.ConfirmUploadAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentConfirmRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                AttachmentId = lease.AttachmentId
            });

        Assert.Equal("FINALIZED", confirmed.Status);
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.Equal(CheckoutAttachmentStatus.Finalized, attachment.Status);
        Assert.NotNull(attachment.FinalObjectKey);
        Assert.NotEqual(attachment.ObjectKey, attachment.FinalObjectKey);
        Assert.Equal("image.jpg", attachment.OriginalFileName);
        Assert.Equal("image/jpeg", attachment.ContentType);
        Assert.Equal(1024, attachment.SizeBytes);
        Assert.Contains(lease.AttachmentId.ToString(), attachment.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FINALIZED", attachment.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmUpload_RejectsMismatchedCartBeforeFinalization()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ConfirmUploadAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                new CheckoutAttachmentConfirmRequestDto
                {
                    CartId = Guid.NewGuid(),
                    FieldId = field.Id,
                    AttachmentId = lease.AttachmentId
                }));

        Assert.Equal("CHECKOUT_ATTACHMENT_CART_NOT_AUTHORIZED", ex.ErrorCode);
        storage.Verify(
            item => item.CopyObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfirmUpload_RetryIsIdempotentAndDoesNotCreateAnotherFinalObject()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        var request = new CheckoutAttachmentConfirmRequestDto
        {
            CartId = cartId,
            FieldId = field.Id,
            AttachmentId = lease.AttachmentId
        };

        var first = await service.ConfirmUploadAsync(tenantId, "guest-session", "session", "guest-session", null, request);
        var second = await service.ConfirmUploadAsync(tenantId, "guest-session", "session", "guest-session", null, request);

        Assert.Equal("FINALIZED", first.Status);
        Assert.Equal("FINALIZED", second.Status);
        storage.Verify(
            item => item.CopyObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfirmUpload_CopyFailureLeavesAttachmentFailedWithoutFinalObject()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        storage.Setup(item => item.CopyObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("copy failed"));
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmUploadAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                new CheckoutAttachmentConfirmRequestDto
                {
                    CartId = cartId,
                    FieldId = field.Id,
                    AttachmentId = lease.AttachmentId
                }));

        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.Equal(CheckoutAttachmentStatus.Failed, attachment.Status);
        Assert.Null(attachment.FinalObjectKey);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsCrossTenantAttachment()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var fieldA = await SeedFieldAsync(tenantA, CheckoutFieldType.File);
        var fieldB = await SeedFieldAsync(tenantB, CheckoutFieldType.File);
        var service = CreateService(validCartId: cartId, tenantId: tenantA);
        var lease = await service.CreateUploadLeaseAsync(
            tenantA,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentUploadLeaseRequestDto
            {
                CartId = cartId,
                FieldId = fieldA.Id,
                FileName = "evidence.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 1024
            });

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantB,
                "guest-session",
                "session",
                "guest-session",
                null,
                cartId,
                Guid.NewGuid(),
                fieldB,
                new[] { lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsWrongCheckoutSession()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                "guest-session",
                "session",
                "other-session",
                null,
                cartId,
                Guid.NewGuid(),
                field,
                new[] { lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_OWNER_MISMATCH", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsWrongCustomer()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId, ownerId: customerA.ToString("N"), ownerType: "customer");
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            customerA.ToString("N"),
            "customer",
            null,
            customerA,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                customerA.ToString("N"),
                "customer",
                null,
                customerB,
                cartId,
                Guid.NewGuid(),
                field,
                new[] { lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_OWNER_MISMATCH", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsWrongCart()
    {
        var tenantId = Guid.NewGuid();
        var leaseCartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: leaseCartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, leaseCartId, "image.jpg", "image/jpeg", 1024));

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                field,
                new[] { lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsDuplicateAttachmentIds()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                cartId,
                Guid.NewGuid(),
                field,
                new[] { lease.AttachmentId, lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_DUPLICATE", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsTooManyFiles()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.MultiImage, maxFiles: 1);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var first = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "first.jpg", "image/jpeg", 1024));
        var second = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "second.jpg", "image/jpeg", 1024));

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                cartId,
                Guid.NewGuid(),
                field,
                new[] { first.AttachmentId, second.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_TOO_MANY", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsExpiredAttachment()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        attachment.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                cartId,
                Guid.NewGuid(),
                field,
                new[] { lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_EXPIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidateAndAttach_ConfirmsAndMarksAttachmentAttached()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentUploadLeaseRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                FileName = "image.jpg",
                ContentType = "image/jpeg",
                FileSizeBytes = 1024
            });
        await service.ConfirmUploadAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentConfirmRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                AttachmentId = lease.AttachmentId
            });

        var attachments = await service.ValidateAndAttachAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            cartId,
            orderId,
            field,
            new[] { lease.AttachmentId });

        Assert.Single(attachments);
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.Equal(CheckoutAttachmentStatus.Attached, attachment.Status);
        Assert.Equal(orderId, attachment.OrderId);
        Assert.Equal(1024, attachment.SizeBytes);
        Assert.Contains("ATTACHED", attachment.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsAlreadyAttachedAttachment_WithoutRebindingOrder()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        await service.ConfirmUploadAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentConfirmRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                AttachmentId = lease.AttachmentId
            });
        await service.ValidateAndAttachAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            cartId,
            firstOrderId,
            field,
            new[] { lease.AttachmentId });

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                cartId,
                secondOrderId,
                field,
                new[] { lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_INVALID_STATE", ex.ErrorCode);
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.Equal(CheckoutAttachmentStatus.Attached, attachment.Status);
        Assert.Equal(firstOrderId, attachment.OrderId);
    }

    [Fact]
    public async Task ValidateAndAttach_RejectsFinalizedAttachmentMissingFinalKey_WithoutMarkingAttached()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var service = CreateService(validCartId: cartId, tenantId: tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        attachment.Status = CheckoutAttachmentStatus.Finalized;
        attachment.FinalObjectKey = null;
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<CheckoutDynamicFieldValidationException>(() =>
            service.ValidateAndAttachAsync(
                tenantId,
                "guest-session",
                "session",
                "guest-session",
                null,
                cartId,
                Guid.NewGuid(),
                field,
                new[] { lease.AttachmentId }));

        Assert.Equal("CHECKOUT_ATTACHMENT_INVALID_STATE", ex.ErrorCode);
        Assert.Null(attachment.OrderId);
        Assert.Equal(CheckoutAttachmentStatus.Finalized, attachment.Status);
    }

    [Fact]
    public async Task ValidateAndAttach_DoesNotRevalidateStagedObjectAfterFinalization()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        await service.ConfirmUploadAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentConfirmRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                AttachmentId = lease.AttachmentId
            });

        await service.ValidateAndAttachAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            cartId,
            Guid.NewGuid(),
            field,
            new[] { lease.AttachmentId });

        storage.Verify(
            item => item.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemovePendingAttachment_MarksDeletedAndDeletesObject()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));

        await service.RemovePendingAttachmentAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            cartId,
            field.Id,
            lease.AttachmentId);

        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        Assert.True(attachment.IsDeleted);
        Assert.Equal(CheckoutAttachmentStatus.Deleted, attachment.Status);
        storage.Verify(item => item.DeleteObjectAsync(attachment.ObjectKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovePendingAttachment_RemovesUnattachedFinalizedAttachmentAndFinalObject()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        await service.ConfirmUploadAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentConfirmRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                AttachmentId = lease.AttachmentId
            });
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        var finalObjectKey = attachment.FinalObjectKey;

        await service.RemovePendingAttachmentAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            cartId,
            field.Id,
            lease.AttachmentId);

        Assert.True(attachment.IsDeleted);
        Assert.Equal(CheckoutAttachmentStatus.Deleted, attachment.Status);
        storage.Verify(item => item.DeleteObjectAsync(attachment.ObjectKey, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(item => item.DeleteObjectAsync(finalObjectKey!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CleanupJob_ExpiresOrphanedPendingAttachment()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        attachment.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();
        var cleanup = new CheckoutAttachmentCleanupJob(
            _dbContext,
            storage.Object,
            Mock.Of<ILogger<CheckoutAttachmentCleanupJob>>());

        await cleanup.RunAsync();

        Assert.True(attachment.IsDeleted);
        Assert.Equal(CheckoutAttachmentStatus.Expired, attachment.Status);
        storage.Verify(item => item.DeleteObjectAsync(attachment.ObjectKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CleanupJob_DoesNotDeleteUnexpiredFinalizedAttachment()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        await service.ConfirmUploadAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentConfirmRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                AttachmentId = lease.AttachmentId
            });
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        attachment.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10);
        await _dbContext.SaveChangesAsync();
        var cleanup = new CheckoutAttachmentCleanupJob(
            _dbContext,
            storage.Object,
            Mock.Of<ILogger<CheckoutAttachmentCleanupJob>>());

        await cleanup.RunAsync();

        Assert.False(attachment.IsDeleted);
        Assert.Equal(CheckoutAttachmentStatus.Finalized, attachment.Status);
        storage.Verify(item => item.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CleanupJob_ExpiresUnattachedFinalizedAttachmentAndDeletesFinalObject()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        await service.ConfirmUploadAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            new CheckoutAttachmentConfirmRequestDto
            {
                CartId = cartId,
                FieldId = field.Id,
                AttachmentId = lease.AttachmentId
            });
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        var finalObjectKey = attachment.FinalObjectKey;
        attachment.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();
        var cleanup = new CheckoutAttachmentCleanupJob(
            _dbContext,
            storage.Object,
            Mock.Of<ILogger<CheckoutAttachmentCleanupJob>>());

        await cleanup.RunAsync();

        Assert.True(attachment.IsDeleted);
        Assert.Equal(CheckoutAttachmentStatus.Expired, attachment.Status);
        storage.Verify(item => item.DeleteObjectAsync(attachment.ObjectKey, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(item => item.DeleteObjectAsync(finalObjectKey!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CleanupJob_ClaimsAttachmentBeforeDeletingStorageObject()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        attachment.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();
        CheckoutAttachmentStatus? statusObservedAtDelete = null;
        storage.Setup(item => item.DeleteObjectAsync(attachment.ObjectKey, It.IsAny<CancellationToken>()))
            .Callback(() => statusObservedAtDelete = attachment.Status)
            .Returns(Task.CompletedTask);
        var cleanup = new CheckoutAttachmentCleanupJob(
            _dbContext,
            storage.Object,
            Mock.Of<ILogger<CheckoutAttachmentCleanupJob>>());

        await cleanup.RunAsync();

        Assert.Equal(CheckoutAttachmentStatus.Deleting, statusObservedAtDelete);
        Assert.Equal(CheckoutAttachmentStatus.Expired, attachment.Status);
    }

    [Fact]
    public async Task CleanupJob_RetryAfterStorageFailure_RemainsRecoverable()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var field = await SeedFieldAsync(tenantId, CheckoutFieldType.Image);
        var storage = CreateStorageMock();
        var service = CreateService(storage, cartId, tenantId);
        var lease = await service.CreateUploadLeaseAsync(
            tenantId,
            "guest-session",
            "session",
            "guest-session",
            null,
            NewLeaseRequest(field.Id, cartId, "image.jpg", "image/jpeg", 1024));
        var attachment = await _dbContext.CheckoutAttachments.IgnoreQueryFilters().SingleAsync(item => item.Id == lease.AttachmentId);
        attachment.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await _dbContext.SaveChangesAsync();
        storage.SetupSequence(item => item.DeleteObjectAsync(attachment.ObjectKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary storage outage"))
            .Returns(Task.CompletedTask);
        var cleanup = new CheckoutAttachmentCleanupJob(
            _dbContext,
            storage.Object,
            Mock.Of<ILogger<CheckoutAttachmentCleanupJob>>());

        await cleanup.RunAsync();
        Assert.False(attachment.IsDeleted);
        Assert.Equal(CheckoutAttachmentStatus.Deleting, attachment.Status);

        await cleanup.RunAsync();
        Assert.True(attachment.IsDeleted);
        Assert.Equal(CheckoutAttachmentStatus.Expired, attachment.Status);
    }

    public void Dispose() => _dbContext.Dispose();

    private CheckoutAttachmentService CreateService(
        Mock<IR2StorageService>? storage = null,
        Guid? validCartId = null,
        Guid? tenantId = null,
        string ownerId = "guest-session",
        string ownerType = "session")
    {
        var cartService = new Mock<ICartService>();
        cartService.Setup(service => service.GetCartAsync(
                It.IsAny<Guid>(),
                It.IsAny<CartOwner>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid resolvedTenantId, CartOwner owner, string _, CancellationToken _) => new CartDto
            {
                CartId = validCartId ?? Guid.NewGuid(),
                TenantId = tenantId ?? resolvedTenantId,
                OwnerType = ownerType,
                OwnerId = ownerId,
                Items = validCartId.HasValue
                    ? new List<CartItemDto> { new() { ProductId = Guid.NewGuid(), Quantity = 1 } }
                    : new List<CartItemDto>(),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                LastUpdatedUtc = DateTime.UtcNow,
                Currency = "EGP"
            });

        return
        new(
            _dbContext,
            (storage ?? CreateStorageMock()).Object,
            cartService.Object,
            new FileValidationService(),
            Mock.Of<ILogger<CheckoutAttachmentService>>());
    }

    private static Mock<IR2StorageService> CreateStorageMock(
        string objectKey = "tenants/test/checkout/cart/fields/field/attachments/server.jpg",
        string contentType = "image/jpeg",
        long contentLength = 1024)
    {
        var mock = new Mock<IR2StorageService>();
        mock.Setup(storage => storage.CreateUploadUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2UploadUrlResult("https://upload.example/object", "server.jpg", objectKey, "PUT", contentType));
        mock.Setup(storage => storage.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2ObjectMetadata(contentType, contentLength, null, "etag"));
        mock.Setup(storage => storage.GetObjectBytesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 });
        mock.Setup(storage => storage.CopyObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sourceKey, string destinationKey, string copiedContentType, CancellationToken token) =>
                new R2ObjectMetadata(copiedContentType, contentLength, null, "final-etag"));
        mock.Setup(storage => storage.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private async Task<CheckoutFieldDefinition> SeedFieldAsync(
        Guid tenantId,
        CheckoutFieldType fieldType,
        bool isEnabled = true,
        long? maxFileSize = null,
        int? maxFiles = null)
    {
        var allowedMimeTypes = fieldType == CheckoutFieldType.File
            ? "[\"application/pdf\",\"image/jpeg\"]"
            : "[\"image/jpeg\"]";
        var resolvedMaxFileSize = maxFileSize ?? 5242880;
        var resolvedMaxFiles = maxFiles ?? 1;
        var field = new CheckoutFieldDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FieldKind = CheckoutFieldKind.Custom,
            FieldType = fieldType,
            Label = "Upload",
            IsEnabled = isEnabled,
            IsRequired = false,
            DisplayOrder = 10,
            ConfigurationSchemaVersion = 1,
            ConfigurationJson = $"{{\"schemaVersion\":1,\"allowedMimeTypes\":{allowedMimeTypes},\"maxFileSize\":{resolvedMaxFileSize},\"maxFiles\":{resolvedMaxFiles}}}",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.CheckoutFieldDefinitions.Add(field);
        await _dbContext.SaveChangesAsync();
        return field;
    }

    private static CheckoutAttachmentUploadLeaseRequestDto NewLeaseRequest(
        Guid fieldId,
        Guid cartId,
        string fileName,
        string contentType,
        long sizeBytes) => new()
    {
        CartId = cartId,
        FieldId = fieldId,
        FileName = fileName,
        ContentType = contentType,
        FileSizeBytes = sizeBytes
    };
}
