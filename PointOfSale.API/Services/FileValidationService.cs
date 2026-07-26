using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Linq;

namespace PointOfSale.API.Services;

public enum AllowedFileType
{
    Jpeg,
    Png,
    Webp,
    Gif,
    Pdf,
    Mp4,
    Doc,
    Docx
}

public static class FileValidationProfiles
{
    public const int MaxManualWalletProofBytes = 5 * 1024 * 1024;

    public static readonly IReadOnlyCollection<AllowedFileType> ManualWallet =
        new ReadOnlyCollection<AllowedFileType>(new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Pdf });

    public static readonly IReadOnlyCollection<AllowedFileType> ProductMedia =
        new ReadOnlyCollection<AllowedFileType>(new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Webp, AllowedFileType.Mp4 });

    public static readonly IReadOnlyCollection<AllowedFileType> Images =
        new ReadOnlyCollection<AllowedFileType>(new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Webp, AllowedFileType.Gif });

    public static readonly IReadOnlyCollection<AllowedFileType> ChatAttachments =
        new ReadOnlyCollection<AllowedFileType>(new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Webp, AllowedFileType.Gif, AllowedFileType.Pdf, AllowedFileType.Doc, AllowedFileType.Docx });

    public static readonly IReadOnlyCollection<AllowedFileType> RefundEvidence =
        new ReadOnlyCollection<AllowedFileType>(new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Mp4 });

    public static readonly IReadOnlyCollection<AllowedFileType> VendorPaymentProof =
        new ReadOnlyCollection<AllowedFileType>(new[] { AllowedFileType.Jpeg, AllowedFileType.Png, AllowedFileType.Webp, AllowedFileType.Mp4 });
}

public interface IFileValidationService
{
    Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default);

    Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        IReadOnlyCollection<AllowedFileType> allowedFileTypes,
        CancellationToken cancellationToken = default);
}

public sealed class FileValidationService : IFileValidationService
{
    public const int SignatureProbeLength = 64;

    private static readonly IReadOnlyCollection<AllowedFileType> DefaultAllowedFileTypes = FileValidationProfiles.ManualWallet;

    private static readonly IReadOnlyDictionary<AllowedFileType, FileTypeRule> FileTypeRules =
        new ReadOnlyDictionary<AllowedFileType, FileTypeRule>(
            new Dictionary<AllowedFileType, FileTypeRule>
            {
                [AllowedFileType.Jpeg] = new(
                    CanonicalContentType: "image/jpeg",
                    PrimaryExtension: ".jpg",
                    Extensions: new[] { ".jpg", ".jpeg" },
                    MimeTypes: new[] { "image/jpeg", "image/jpg" },
                    Signatures: new[]
                    {
                        new byte[] { 0xFF, 0xD8, 0xFF }
                    }),
                [AllowedFileType.Png] = new(
                    CanonicalContentType: "image/png",
                    PrimaryExtension: ".png",
                    Extensions: new[] { ".png" },
                    MimeTypes: new[] { "image/png" },
                    Signatures: new[]
                    {
                        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
                    }),
                [AllowedFileType.Webp] = new(
                    CanonicalContentType: "image/webp",
                    PrimaryExtension: ".webp",
                    Extensions: new[] { ".webp" },
                    MimeTypes: new[] { "image/webp" },
                    Signatures: Array.Empty<byte[]>(),
                    HeaderValidator: header =>
                        header.Length >= 12 &&
                        header.AsSpan()[..4].SequenceEqual("RIFF"u8) &&
                        header.AsSpan()[8..12].SequenceEqual("WEBP"u8)),
                [AllowedFileType.Gif] = new(
                    CanonicalContentType: "image/gif",
                    PrimaryExtension: ".gif",
                    Extensions: new[] { ".gif" },
                    MimeTypes: new[] { "image/gif" },
                    Signatures: new[]
                    {
                        "GIF87a"u8.ToArray(),
                        "GIF89a"u8.ToArray()
                    }),
                [AllowedFileType.Pdf] = new(
                    CanonicalContentType: "application/pdf",
                    PrimaryExtension: ".pdf",
                    Extensions: new[] { ".pdf" },
                    MimeTypes: new[] { "application/pdf" },
                    Signatures: new[]
                    {
                        new byte[] { 0x25, 0x50, 0x44, 0x46 }
                    }),
                [AllowedFileType.Mp4] = new(
                    CanonicalContentType: "video/mp4",
                    PrimaryExtension: ".mp4",
                    Extensions: new[] { ".mp4" },
                    MimeTypes: new[] { "video/mp4" },
                    Signatures: Array.Empty<byte[]>(),
                    HeaderValidator: header => LooksLikeMp4(header.AsSpan())),
                [AllowedFileType.Doc] = new(
                    CanonicalContentType: "application/msword",
                    PrimaryExtension: ".doc",
                    Extensions: new[] { ".doc" },
                    MimeTypes: new[] { "application/msword" },
                    Signatures: new[]
                    {
                        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }
                    }),
                [AllowedFileType.Docx] = new(
                    CanonicalContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    PrimaryExtension: ".docx",
                    Extensions: new[] { ".docx" },
                    MimeTypes: new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                    Signatures: new[]
                    {
                        new byte[] { 0x50, 0x4B, 0x03, 0x04 }
                    },
                    StructuralValidatorAsync: ValidateDocxArchiveAsync)
            });

    /// <summary>
    /// Gets all allowed MIME types for the specified file types.
    /// </summary>
    public static string[] GetAllowedMimeTypes(IReadOnlyCollection<AllowedFileType> allowedFileTypes)
    {
        return allowedFileTypes
            .Where(type => FileTypeRules.ContainsKey(type))
            .SelectMany(type => FileTypeRules[type].MimeTypes)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Gets all allowed file extensions for the specified file types.
    /// </summary>
    public static string[] GetAllowedExtensions(IReadOnlyCollection<AllowedFileType> allowedFileTypes)
    {
        return allowedFileTypes
            .Where(type => FileTypeRules.ContainsKey(type))
            .SelectMany(type => FileTypeRules[type].Extensions)
            .Distinct()
            .ToArray();
    }

    public Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        return ValidateFileAsync(fileStream, fileName, contentType, DefaultAllowedFileTypes, cancellationToken);
    }

    public async Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        IReadOnlyCollection<AllowedFileType> allowedFileTypes,
        CancellationToken cancellationToken = default)
    {
        if (fileStream == null || !fileStream.CanRead)
        {
            return FileValidationResult.Failure("File stream is unreadable.");
        }

        if (!fileStream.CanSeek)
        {
            return FileValidationResult.Failure("Readable, seekable stream is required.");
        }

        if (allowedFileTypes == null || allowedFileTypes.Count == 0)
        {
            throw new ArgumentException("At least one allowed file type must be specified.", nameof(allowedFileTypes));
        }

        var normalizedFileName = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            return FileValidationResult.Failure("File name is required.");
        }

        var normalizedContentType = NormalizeContentType(contentType);
        var extension = Path.GetExtension(normalizedFileName).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(extension))
        {
            return FileValidationResult.Failure("File extension is required.");
        }

        var header = await ReadHeaderAsync(fileStream, cancellationToken);
        if (header.Length < 4)
        {
            return FileValidationResult.Failure("File is too small or corrupted.");
        }

        foreach (var allowedType in allowedFileTypes.Distinct())
        {
            if (!FileTypeRules.TryGetValue(allowedType, out var rule))
            {
                continue;
            }

            if (!rule.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!await MatchesRuleAsync(rule, fileStream, header, cancellationToken))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedContentType) &&
                !rule.MimeTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
            {
                return FileValidationResult.Failure("File metadata does not match the uploaded content.");
            }

            var sanitizedFileName = SanitizeFileName(normalizedFileName, rule.PrimaryExtension);
            return FileValidationResult.Success(
                rule.PrimaryExtension,
                rule.CanonicalContentType,
                sanitizedFileName,
                allowedType);
        }

        return FileValidationResult.Failure("Unsupported or invalid file format.");
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        var originalPosition = fileStream.Position;
        var buffer = new byte[SignatureProbeLength];

        fileStream.Position = 0;

        try
        {
            var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            return buffer.Take(bytesRead).ToArray();
        }
        finally
        {
            fileStream.Position = originalPosition;
        }
    }

    private static async Task<bool> MatchesRuleAsync(
        FileTypeRule rule,
        Stream fileStream,
        byte[] header,
        CancellationToken cancellationToken)
    {
        if (rule.Signatures.Any(signature => StartsWith(header, signature)))
        {
            if (rule.StructuralValidatorAsync == null)
            {
                return true;
            }

            return await rule.StructuralValidatorAsync(fileStream, cancellationToken);
        }

        if (rule.HeaderValidator?.Invoke(header) == true)
        {
            return true;
        }

        return false;
    }

    private static bool StartsWith(IReadOnlyList<byte> candidate, IReadOnlyList<byte> prefix)
    {
        if (candidate.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (candidate[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeMp4(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12 || !header[4..8].SequenceEqual("ftyp"u8))
        {
            return false;
        }

        for (var index = 8; index <= header.Length - 4; index += 4)
        {
            if (IsAllowedMp4Brand(header.Slice(index, 4)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedMp4Brand(ReadOnlySpan<byte> brand)
    {
        return brand.SequenceEqual("isom"u8) ||
               brand.SequenceEqual("iso2"u8) ||
               brand.SequenceEqual("mp41"u8) ||
               brand.SequenceEqual("mp42"u8) ||
               brand.SequenceEqual("avc1"u8) ||
               brand.SequenceEqual("dash"u8);
    }

    private static async Task<bool> ValidateDocxArchiveAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        var originalPosition = fileStream.Position;
        fileStream.Position = 0;

        try
        {
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
            var hasContentTypes = archive.Entries.Any(entry =>
                string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase));
            var hasWordDocument = archive.Entries.Any(entry =>
                entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase));

            // Reject macro-enabled documents
            var hasMacros = archive.Entries.Any(entry =>
                string.Equals(entry.FullName, "word/vbaProject.bin", StringComparison.OrdinalIgnoreCase));

            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            return hasContentTypes && hasWordDocument && !hasMacros;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            fileStream.Position = originalPosition;
        }
    }

    private static string NormalizeContentType(string? contentType)
    {
        return (contentType ?? string.Empty)
            .Split(';', 2)[0]
            .Trim()
            .ToLowerInvariant();
    }

    public static string SanitizeFileName(string? fileName, string requiredExtension)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName)
            ? $"upload{requiredExtension}"
            : Path.GetFileName(fileName).Trim();

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(candidate
            .Where(ch => !invalidChars.Contains(ch))
            .ToArray());

        sanitized = sanitized
            .Replace("..", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = $"upload{requiredExtension}";
        }

        var baseName = Path.GetFileNameWithoutExtension(sanitized);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "upload";
        }

        sanitized = $"{baseName}{requiredExtension}";
        return sanitized.Length <= 255 ? sanitized : sanitized[..255];
    }

    private sealed record FileTypeRule(
        string CanonicalContentType,
        string PrimaryExtension,
        IReadOnlyCollection<string> Extensions,
        IReadOnlyCollection<string> MimeTypes,
        IReadOnlyCollection<byte[]> Signatures,
        Func<byte[], bool>? HeaderValidator = null,
        Func<Stream, CancellationToken, Task<bool>>? StructuralValidatorAsync = null);
}

public sealed class FileValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ValidatedExtension { get; init; }
    public string? CanonicalContentType { get; init; }
    public string? SanitizedFileName { get; init; }
    public AllowedFileType? FileType { get; init; }

    public static FileValidationResult Success(
        string extension,
        string canonicalContentType,
        string sanitizedFileName,
        AllowedFileType fileType) => new()
    {
        IsValid = true,
        ValidatedExtension = extension,
        CanonicalContentType = canonicalContentType,
        SanitizedFileName = sanitizedFileName,
        FileType = fileType
    };

    public static FileValidationResult Failure(string errorMessage) => new()
    {
        IsValid = false,
        ErrorMessage = errorMessage
    };
}
