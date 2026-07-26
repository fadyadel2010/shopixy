using PointOfSale.API.Services;

namespace PointOfSale.Tests.Security;

public sealed class FileValidationServiceTests
{
    private readonly FileValidationService _service = new();

    [Fact]
    public async Task ValidateFileAsync_ShouldAccept_JpegMagicBytes()
    {
        await using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

        var result = await _service.ValidateFileAsync(
            stream,
            "product.jpeg",
            "image/jpeg",
            FileValidationProfiles.ProductMedia);

        Assert.True(result.IsValid);
        Assert.Equal(".jpg", result.ValidatedExtension);
        Assert.Equal("image/jpeg", result.CanonicalContentType);
        Assert.Equal("product.jpg", result.SanitizedFileName);
    }

    [Fact]
    public async Task ValidateFileAsync_ShouldReject_ExtensionMismatch()
    {
        await using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

        var result = await _service.ValidateFileAsync(
            stream,
            "spoofed.png",
            "image/png",
            FileValidationProfiles.ProductMedia);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateFileAsync_ShouldAccept_PdfMagicBytes()
    {
        await using var stream = new MemoryStream("%PDF-test"u8.ToArray());

        var result = await _service.ValidateFileAsync(
            stream,
            "evidence.pdf",
            "application/pdf");

        Assert.True(result.IsValid);
        Assert.Equal(".pdf", result.ValidatedExtension);
        Assert.Equal("application/pdf", result.CanonicalContentType);
    }

    [Fact]
    public async Task ValidateFileAsync_ShouldReject_UnsupportedTextFile()
    {
        await using var stream = new MemoryStream("plain text"u8.ToArray());

        var result = await _service.ValidateFileAsync(
            stream,
            "notes.txt",
            "text/plain",
            FileValidationProfiles.ChatAttachments);

        Assert.False(result.IsValid);
    }
}
