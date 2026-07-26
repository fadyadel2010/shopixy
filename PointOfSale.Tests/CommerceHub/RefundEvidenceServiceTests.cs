using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.Tests.CommerceHub;

public class RefundEvidenceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public RefundEvidenceServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateUploadLeaseAsync_ShouldReject_WhenContentTypeNotAllowed()
    {
        var customer = await SeedCustomerAsync();
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CreateUploadLeaseAsync(
                customer.Id,
                new RefundEvidenceUploadUrlRequestDto
                {
                    FileName = "evidence.pdf",
                    ContentType = "application/pdf",
                    FileSizeBytes = 1024,
                    Sha256 = CreateValidSha256()
                }));

        Assert.Equal("REFUND_MEDIA_TYPE_NOT_ALLOWED", exception.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateUploadLeaseAsync_ShouldReject_WhenFileTooLarge()
    {
        var customer = await SeedCustomerAsync();
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CreateUploadLeaseAsync(
                customer.Id,
                new RefundEvidenceUploadUrlRequestDto
                {
                    FileName = "evidence.jpg",
                    ContentType = "image/jpeg",
                    FileSizeBytes = (10 * 1024 * 1024) + 1,
                    Sha256 = CreateValidSha256()
                }));

        Assert.Equal("REFUND_MEDIA_TOO_LARGE", exception.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task ValidateOwnedMediaAsync_ShouldReject_WhenMoreThanFiveFiles()
    {
        var customer = await SeedCustomerAsync();
        var service = CreateService();
        var mediaUrls = Enumerable.Range(0, 6)
            .Select(_ => Guid.NewGuid().ToString())
            .ToArray();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.ValidateOwnedMediaAsync(customer.Id, mediaUrls));

        Assert.Equal("REFUND_MEDIA_LIMIT_EXCEEDED", exception.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateUploadLeaseAsync_ShouldReject_WhenDuplicateTemporaryUploadExists()
    {
        var customer = await SeedCustomerAsync();
        var existingMedia = await SeedMediaAsync(customer.Id, media =>
        {
            media.ContentType = "image/jpeg";
            media.MediaType = "image";
            media.ExpectedSizeBytes = 2048;
            media.ContentHash = CreateValidSha256();
            media.IsTemporary = true;
            media.ExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        });

        var storageMock = new Mock<IR2StorageService>();
        storageMock.Setup(storage => storage.GetObjectMetadataAsync(existingMedia.ObjectKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2ObjectMetadata("image/jpeg", 2048, null, "test-etag"));

        var service = CreateService(storageMock);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.CreateUploadLeaseAsync(
                customer.Id,
                new RefundEvidenceUploadUrlRequestDto
                {
                    FileName = "duplicate.jpg",
                    ContentType = "image/jpeg",
                    FileSizeBytes = 2048,
                    Sha256 = existingMedia.ContentHash!
                }));

        Assert.Equal("REFUND_MEDIA_DUPLICATE_UPLOAD", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        storageMock.Verify(storage => storage.CreateUploadUrlAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateOwnedMediaAsync_ShouldReject_WhenUploadedSizeDoesNotMatchApprovedSize()
    {
        var customer = await SeedCustomerAsync();
        var media = await SeedMediaAsync(customer.Id, item =>
        {
            item.ContentType = "image/jpeg";
            item.MediaType = "image";
            item.ExpectedSizeBytes = 1024;
            item.ContentHash = CreateValidSha256();
            item.IsTemporary = true;
            item.ExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        });

        var storageMock = new Mock<IR2StorageService>();
        storageMock.Setup(storage => storage.GetObjectMetadataAsync(media.ObjectKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2ObjectMetadata("image/jpeg", 2048, null, "test-etag"));

        var service = CreateService(storageMock);

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.ValidateOwnedMediaAsync(customer.Id, new[] { media.Id.ToString() }));

        Assert.Equal("REFUND_MEDIA_SIZE_MISMATCH", exception.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);

        var flaggedMedia = await _dbContext.CommerceHubRefundEvidenceMedia.SingleAsync(item => item.Id == media.Id);
        Assert.True(flaggedMedia.IsFlagged);
    }

    [Fact]
    public async Task GetCustomerMediaAccessUrlAsync_ShouldClampSignedUrlExpiryToFifteenMinutes()
    {
        var customer = await SeedCustomerAsync();
        var media = await SeedMediaAsync(customer.Id, item =>
        {
            item.IsTemporary = false;
            item.ExpiresAtUtc = DateTime.UtcNow.AddDays(30);
        });

        var observedExpiry = TimeSpan.Zero;
        var storageMock = new Mock<IR2StorageService>();
        storageMock.Setup(storage => storage.GetObjectMetadataAsync(media.ObjectKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2ObjectMetadata("image/jpeg", media.SizeBytes, null, "test-etag"));
        storageMock.Setup(storage => storage.CreateDownloadUrlAsync(media.ObjectKey, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, TimeSpan, CancellationToken>((_, expiry, _) => observedExpiry = expiry)
            .ReturnsAsync("https://signed.example/media");

        var service = CreateService(storageMock, new RefundSettings
        {
            EvidenceAccessUrlExpiryMinutes = 60
        });

        var url = await service.GetCustomerMediaAccessUrlAsync(customer.Id, media.Id);

        Assert.Equal("https://signed.example/media", url);
        Assert.Equal(TimeSpan.FromMinutes(15), observedExpiry);
    }

    [Fact]
    public async Task GetCustomerMediaAccessUrlAsync_ShouldReject_WhenCustomerDoesNotOwnMedia()
    {
        var owner = await SeedCustomerAsync("owner@example.com");
        var otherCustomer = await SeedCustomerAsync("other@example.com");
        var media = await SeedMediaAsync(owner.Id);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.GetCustomerMediaAccessUrlAsync(otherCustomer.Id, media.Id));

        Assert.Equal("REFUND_MEDIA_NOT_FOUND", exception.Code);
        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private RefundEvidenceService CreateService(
        Mock<IR2StorageService>? storageMock = null,
        RefundSettings? settings = null)
    {
        storageMock ??= CreateDefaultStorageMock();

        return new RefundEvidenceService(
            _dbContext,
            storageMock.Object,
            new FileValidationService(),
            Options.Create(settings ?? new RefundSettings()),
            Mock.Of<ILogger<RefundEvidenceService>>());
    }

    private static Mock<IR2StorageService> CreateDefaultStorageMock()
    {
        var mock = new Mock<IR2StorageService>();
        mock.Setup(storage => storage.CreateUploadUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2UploadUrlResult(
                "https://upload.example/object",
                "evidence.jpg",
                "commercehub/refunds/test/evidence.jpg",
                "PUT",
                "image/jpeg"));
        mock.Setup(storage => storage.GetObjectMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2ObjectMetadata("image/jpeg", 1024, null, "test-etag"));
        mock.Setup(storage => storage.GetObjectBytesAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        mock.Setup(storage => storage.CreateDownloadUrlAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://signed.example/object");
        return mock;
    }

    private async Task<CommerceHubCustomer> SeedCustomerAsync(string email = "customer@example.com")
    {
        var customer = CommerceHubCustomer.Create(email, "Evidence Customer", "01000000000", "hash");
        _dbContext.CommerceHubCustomers.Add(customer);
        await _dbContext.SaveChangesAsync();
        return customer;
    }

    private async Task<CommerceHubRefundEvidenceMedia> SeedMediaAsync(
        Guid customerId,
        Action<CommerceHubRefundEvidenceMedia>? configure = null)
    {
        var media = new CommerceHubRefundEvidenceMedia
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ObjectKey = $"commercehub/refunds/{customerId:N}/staged/{Guid.NewGuid():N}.jpg",
            OriginalFileName = "evidence.jpg",
            ContentType = "image/jpeg",
            MediaType = "image",
            SizeBytes = 1024,
            ExpectedSizeBytes = 1024,
            ContentHash = CreateValidSha256(),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            UploadedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            IsTemporary = true,
            IsFlagged = false
        };

        configure?.Invoke(media);

        _dbContext.CommerceHubRefundEvidenceMedia.Add(media);
        await _dbContext.SaveChangesAsync();
        return media;
    }

    private static string CreateValidSha256() => new('a', 64);
}
