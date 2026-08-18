using Microsoft.Extensions.Hosting;
using Moq;
using PointOfSale.API.Services;

namespace PointOfSale.Tests.Storefront;

public sealed class DevelopmentFileStorageServiceTests
{
    [Fact]
    public async Task ServerMediatedUpload_RoundTripsPrivateObjectWithoutPresignedStorage()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"shopixy-storage-{Guid.NewGuid():N}");

        try
        {
            var environment = new Mock<IHostEnvironment>();
            environment.SetupGet(item => item.ContentRootPath).Returns(contentRoot);
            var service = new DevelopmentFileStorageService(environment.Object);
            var bytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x01, 0x02 };
            await using var upload = new MemoryStream(bytes);

            var result = await service.UploadFileAsync(
                upload,
                "image/png",
                "tenants/11111111-1111-1111-1111-111111111111/manual-wallet-proofs/checkout-sessions/22222222-2222-2222-2222-222222222222",
                CancellationToken.None);

            Assert.StartsWith("tenants/11111111-1111-1111-1111-111111111111/manual-wallet-proofs/", result.ObjectKey, StringComparison.Ordinal);
            Assert.Null(result.Url);
            var metadata = await service.GetObjectMetadataAsync(result.ObjectKey, CancellationToken.None);
            Assert.Equal("image/png", metadata.ContentType);
            Assert.Equal(bytes.Length, metadata.ContentLength);
            Assert.Equal(bytes, await service.GetObjectBytesAsync(result.ObjectKey, bytes.Length, CancellationToken.None));

            await service.DeleteObjectAsync(result.ObjectKey, CancellationToken.None);
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.GetObjectMetadataAsync(result.ObjectKey, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PresignedLease_RoundTripsThroughOpaqueSingleUseDevelopmentUrl()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"shopixy-storage-{Guid.NewGuid():N}");

        try
        {
            var environment = new Mock<IHostEnvironment>();
            environment.SetupGet(item => item.ContentRootPath).Returns(contentRoot);
            var service = new DevelopmentFileStorageService(environment.Object);
            var bytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01, 0x02 };

            var lease = await service.CreateUploadUrlAsync(
                "image/png",
                "tenants/11111111111111111111111111111111/checkout/22222222222222222222222222222222/fields/33333333333333333333333333333333/attachments/2026/08/12",
                TimeSpan.FromMinutes(5),
                CancellationToken.None);

            Assert.Contains("/api/storefront/checkout/attachments/development-upload?token=", lease.UploadUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("development-object-storage", lease.UploadUrl, StringComparison.OrdinalIgnoreCase);

            await using (var upload = new MemoryStream(bytes))
            {
                await service.StorePresignedUploadAsync(
                    ExtractToken(lease.UploadUrl),
                    "image/png",
                    bytes.Length,
                    upload,
                    CancellationToken.None);
            }

            var metadata = await service.GetObjectMetadataAsync(lease.ObjectKey, CancellationToken.None);
            Assert.Equal("image/png", metadata.ContentType);
            Assert.Equal(bytes.Length, metadata.ContentLength);
            Assert.Equal(bytes[..4], await service.GetObjectBytesAsync(lease.ObjectKey, 4, metadata.ETag, CancellationToken.None));

            await using var replay = new MemoryStream(bytes);
            var replayException = await Assert.ThrowsAsync<DevelopmentPresignedUploadException>(() =>
                service.StorePresignedUploadAsync(
                    ExtractToken(lease.UploadUrl),
                    "image/png",
                    bytes.Length,
                    replay,
                    CancellationToken.None));
            Assert.Equal("CHECKOUT_ATTACHMENT_UPLOAD_REPLAYED", replayException.ErrorCode);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PresignedLease_RejectsTraversalMissingLeaseMimeMismatchAndExpiration()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"shopixy-storage-{Guid.NewGuid():N}");
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.ContentRootPath).Returns(contentRoot);
        var service = new DevelopmentFileStorageService(environment.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUploadUrlAsync("image/png", "tenants/../outside", TimeSpan.FromMinutes(5)));

        await using var uploadWithoutLease = new MemoryStream(new byte[] { 0x01 });
        var missingLease = await Assert.ThrowsAsync<DevelopmentPresignedUploadException>(() =>
            service.StorePresignedUploadAsync("not-a-token", "image/png", 1, uploadWithoutLease, CancellationToken.None));
        Assert.Equal("CHECKOUT_ATTACHMENT_UPLOAD_TOKEN_INVALID", missingLease.ErrorCode);

        var lease = await service.CreateUploadUrlAsync("image/png", "checkout/test", TimeSpan.FromMinutes(5));
        await using var mimeMismatch = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var mismatch = await Assert.ThrowsAsync<DevelopmentPresignedUploadException>(() =>
            service.StorePresignedUploadAsync(ExtractToken(lease.UploadUrl), "application/pdf", 4, mimeMismatch, CancellationToken.None));
        Assert.Equal("CHECKOUT_ATTACHMENT_UPLOAD_CONTENT_TYPE_MISMATCH", mismatch.ErrorCode);

        var expiredLease = await service.CreateUploadUrlAsync("image/png", "checkout/expired", TimeSpan.FromTicks(1));
        await Task.Delay(5);
        await using var expiredUpload = new MemoryStream(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var expired = await Assert.ThrowsAsync<DevelopmentPresignedUploadException>(() =>
            service.StorePresignedUploadAsync(ExtractToken(expiredLease.UploadUrl), "image/png", 8, expiredUpload, CancellationToken.None));
        Assert.Equal("CHECKOUT_ATTACHMENT_UPLOAD_LEASE_EXPIRED", expired.ErrorCode);

        await using var upload = new MemoryStream(new byte[] { 0x01 });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadFileAsync(upload, "image/png", "tenants/../outside", CancellationToken.None));

        if (Directory.Exists(contentRoot))
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static string ExtractToken(string uploadUrl)
    {
        var index = uploadUrl.IndexOf("token=", StringComparison.Ordinal);
        Assert.True(index >= 0);
        return Uri.UnescapeDataString(uploadUrl[(index + "token=".Length)..]);
    }
}
