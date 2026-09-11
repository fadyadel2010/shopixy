using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace PointOfSale.API.Services;

public interface IDevelopmentPresignedUploadStorage
{
    Task StorePresignedUploadAsync(
        string token,
        string? contentType,
        long? contentLength,
        Stream body,
        CancellationToken cancellationToken = default);
}

public interface IDevelopmentPresignedDownloadStorage
{
    Task<R2DownloadObjectResult> OpenPresignedDownloadAsync(
        string token,
        CancellationToken cancellationToken = default);
}

public sealed class DevelopmentPresignedUploadException : Exception
{
    public DevelopmentPresignedUploadException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public sealed class DevelopmentPresignedDownloadException : Exception
{
    public DevelopmentPresignedDownloadException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

/// <summary>
/// Private, development-only object storage for server-mediated uploads.
/// Production registrations continue to require configured R2 storage.
/// Upload and download leases are represented by opaque application URLs instead
/// of public filesystem paths; uploads are single-use and downloads are expiring.
/// </summary>
public sealed class DevelopmentFileStorageService :
    IR2StorageService,
    IDevelopmentPresignedUploadStorage,
    IDevelopmentPresignedDownloadStorage
{
    private const long MaxLeasedUploadBytes = 50 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> ContentTypeExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["application/pdf"] = ".pdf",
            ["application/msword"] = ".doc",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["video/mp4"] = ".mp4"
        };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, SemaphoreSlim> LeaseLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LeaseLocksGate = new();

    private readonly string _storageRoot;
    private readonly string _leaseRoot;
    private readonly string _downloadLeaseRoot;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IFileValidationService _fileValidationService;

    public DevelopmentFileStorageService(
        IHostEnvironment environment,
        IHttpContextAccessor? httpContextAccessor = null,
        IFileValidationService? fileValidationService = null)
    {
        _storageRoot = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "secure-uploads",
            "development-object-storage"));
        _leaseRoot = Path.Combine(_storageRoot, "_leases");
        _downloadLeaseRoot = Path.Combine(_storageRoot, "_download-leases");
        _httpContextAccessor = httpContextAccessor;
        _fileValidationService = fileValidationService ?? new FileValidationService();
    }

    public async Task<R2UploadUrlResult> CreateUploadUrlAsync(
        string contentType,
        string path,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresIn <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresIn), "Upload leases must have a positive lifetime.");
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (!ContentTypeExtensions.TryGetValue(normalizedContentType, out var extension))
        {
            throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType));
        }

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var objectKey = BuildObjectKey(path, fileName);
        _ = ResolvePath(objectKey);

        Directory.CreateDirectory(_leaseRoot);
        var leaseId = Guid.NewGuid();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var lease = new DevelopmentUploadLease(
            leaseId,
            HashTokenSecret(secret),
            objectKey,
            normalizedContentType,
            DateTime.UtcNow.Add(expiresIn),
            "pending");

        var leasePath = ResolveLeasePath(leaseId);
        await File.WriteAllTextAsync(leasePath, JsonSerializer.Serialize(lease, JsonOptions), cancellationToken);

        var token = $"{leaseId:N}.{secret}";
        return new R2UploadUrlResult(
            BuildUploadUrl(token),
            fileName,
            objectKey,
            "PUT",
            normalizedContentType);
    }

    public async Task<string> CreateDownloadUrlAsync(
        string objectKey,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expiresIn <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresIn), "Download leases must have a positive lifetime.");
        }

        _ = ResolvePath(objectKey);
        Directory.CreateDirectory(_downloadLeaseRoot);

        var leaseId = Guid.NewGuid();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var lease = new DevelopmentDownloadLease(
            leaseId,
            HashTokenSecret(secret),
            objectKey,
            DateTime.UtcNow.Add(expiresIn));

        await File.WriteAllTextAsync(
            ResolveDownloadLeasePath(leaseId),
            JsonSerializer.Serialize(lease, JsonOptions),
            cancellationToken);

        return BuildDownloadUrl($"{leaseId:N}.{secret}");
    }

    public async Task<R2DownloadObjectResult> OpenPresignedDownloadAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var parsed = ParseDownloadToken(token);
        var lease = await ReadDownloadLeaseAsync(parsed.LeaseId, cancellationToken);

        if (!string.Equals(lease.SecretSha256, HashTokenSecret(parsed.Secret), StringComparison.OrdinalIgnoreCase))
        {
            throw new DevelopmentPresignedDownloadException(
                "DEVELOPMENT_DOWNLOAD_TOKEN_INVALID",
                "Download lease token is invalid.");
        }

        if (lease.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new DevelopmentPresignedDownloadException(
                "DEVELOPMENT_DOWNLOAD_LEASE_EXPIRED",
                "Download lease has expired.");
        }

        return await DownloadObjectAsync(lease.ObjectKey, null, cancellationToken);
    }

    public async Task<R2UploadResult> UploadFileAsync(
        Stream file,
        string contentType,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.CanRead)
        {
            throw new ArgumentException("The upload stream must be readable.", nameof(file));
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (!ContentTypeExtensions.TryGetValue(normalizedContentType, out var extension))
        {
            throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType));
        }

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var objectKey = BuildObjectKey(path, fileName);
        var destinationPath = ResolvePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);

        return new R2UploadResult(null, fileName, objectKey);
    }

    public async Task StorePresignedUploadAsync(
        string token,
        string? contentType,
        long? contentLength,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var parsed = ParseToken(token);
        var leaseLock = GetLeaseLock(parsed.LeaseId);

        await leaseLock.WaitAsync(cancellationToken);
        try
        {
            var lease = await ReadLeaseAsync(parsed.LeaseId, cancellationToken);
            ValidateLease(lease, parsed.Secret, contentType);

            if (contentLength is <= 0 or > MaxLeasedUploadBytes)
            {
                throw new DevelopmentPresignedUploadException(
                    "CHECKOUT_ATTACHMENT_UPLOAD_SIZE_INVALID",
                    "Uploaded file size is outside the supported development upload range.");
            }

            var destinationPath = ResolvePath(lease.ObjectKey);
            if (File.Exists(destinationPath))
            {
                await WriteLeaseAsync(lease with { Status = "used" }, cancellationToken);
                throw new DevelopmentPresignedUploadException(
                    "CHECKOUT_ATTACHMENT_UPLOAD_REPLAYED",
                    "This upload lease has already been used.");
            }

            await WriteLeaseAsync(lease with { Status = "uploading" }, cancellationToken);

            var tempPath = Path.Combine(Path.GetDirectoryName(destinationPath)!, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using (var destination = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    var actualLength = await CopyWithLimitAsync(body, destination, MaxLeasedUploadBytes, cancellationToken);
                    if (actualLength <= 0)
                    {
                        throw new DevelopmentPresignedUploadException(
                            "CHECKOUT_ATTACHMENT_UPLOAD_SIZE_INVALID",
                            "Uploaded file is empty.");
                    }

                    if (contentLength.HasValue && actualLength != contentLength.Value)
                    {
                        throw new DevelopmentPresignedUploadException(
                            "CHECKOUT_ATTACHMENT_UPLOAD_SIZE_MISMATCH",
                            "Uploaded file size does not match the request metadata.");
                    }

                    destination.Position = 0;
                    await ValidateStoredFileAsync(destination, lease, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                File.Move(tempPath, destinationPath);
                await WriteLeaseAsync(lease with { Status = "used", UsedAtUtc = DateTime.UtcNow }, cancellationToken);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                await WriteLeaseAsync(lease with { Status = "failed", UsedAtUtc = DateTime.UtcNow }, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            leaseLock.Release();
        }
    }

    public async Task<R2ObjectMetadata> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveExistingPath(objectKey);
        var info = new FileInfo(path);
        var checksum = await ComputeChecksumAsync(path, cancellationToken);
        return new R2ObjectMetadata(
            ResolveContentType(path),
            info.Length,
            checksum,
            checksum);
    }

    public Task<byte[]> GetObjectBytesAsync(
        string objectKey,
        int maxBytes,
        CancellationToken cancellationToken = default) =>
        GetObjectBytesAsync(objectKey, maxBytes, null, cancellationToken);

    public async Task<byte[]> GetObjectBytesAsync(
        string objectKey,
        int maxBytes,
        string? ifMatchETag,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var path = ResolveExistingPath(objectKey);
        var info = new FileInfo(path);
        await EnsureETagMatchesAsync(path, ifMatchETag, cancellationToken);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = (int)Math.Min(info.Length, maxBytes);
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == buffer.Length ? buffer : buffer[..offset];
    }

    public Task DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(objectKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<R2ObjectMetadata> CopyObjectAsync(
        string sourceObjectKey,
        string destinationObjectKey,
        string contentType,
        string? sourceIfMatchETag,
        CancellationToken cancellationToken = default)
    {
        _ = NormalizeContentType(contentType);
        var sourcePath = ResolveExistingPath(sourceObjectKey);
        await EnsureETagMatchesAsync(sourcePath, sourceIfMatchETag, cancellationToken);

        var destinationPath = ResolvePath(destinationObjectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using (var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        return await GetObjectMetadataAsync(destinationObjectKey, cancellationToken);
    }

    public async Task<R2DownloadObjectResult> DownloadObjectAsync(
        string objectKey,
        string? ifMatchETag,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveExistingPath(objectKey);
        await EnsureETagMatchesAsync(path, ifMatchETag, cancellationToken);
        var info = new FileInfo(path);
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var checksum = await ComputeChecksumAsync(path, cancellationToken);
        return new R2DownloadObjectResult(stream, ResolveContentType(path), info.Length, checksum);
    }

    private string ResolveExistingPath(string objectKey)
    {
        var path = ResolvePath(objectKey);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("Stored object was not found.", objectKey);
    }

    private string ResolvePath(string objectKey)
    {
        var segments = NormalizeSegments(objectKey);
        var path = Path.GetFullPath(Path.Combine(new[] { _storageRoot }.Concat(segments).ToArray()));
        var requiredPrefix = _storageRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Object key resolved outside the development storage root.");
        }

        return path;
    }

    private string ResolveLeasePath(Guid leaseId)
    {
        var path = Path.GetFullPath(Path.Combine(_leaseRoot, $"{leaseId:N}.json"));
        var requiredPrefix = Path.GetFullPath(_leaseRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Lease key resolved outside the development lease root.");
        }

        return path;
    }

    private string ResolveDownloadLeasePath(Guid leaseId)
    {
        var path = Path.GetFullPath(Path.Combine(_downloadLeaseRoot, $"{leaseId:N}.json"));
        var requiredPrefix = Path.GetFullPath(_downloadLeaseRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Download lease key resolved outside the development lease root.");
        }

        return path;
    }

    private static string BuildObjectKey(string path, string fileName) =>
        string.Join('/', NormalizeSegments(path).Append(fileName));

    private string BuildUploadUrl(string token)
    {
        var uploadPath = $"/api/storefront/checkout/attachments/development-upload?token={Uri.EscapeDataString(token)}";
        var request = _httpContextAccessor?.HttpContext?.Request;
        if (request == null)
        {
            return uploadPath;
        }

        return $"{request.Scheme}://{request.Host}{request.PathBase}{uploadPath}";
    }

    private string BuildDownloadUrl(string token)
    {
        var downloadPath = $"/api/storefront/checkout/attachments/development-download?token={Uri.EscapeDataString(token)}";
        var request = _httpContextAccessor?.HttpContext?.Request;
        if (request == null)
        {
            return downloadPath;
        }

        return $"{request.Scheme}://{request.Host}{request.PathBase}{downloadPath}";
    }

    private static string[] NormalizeSegments(string value)
    {
        var segments = (value ?? string.Empty)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                segment.Contains("..", StringComparison.Ordinal) ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidOperationException("Object key contains an invalid path segment.");
        }

        return segments;
    }

    private static async Task EnsureETagMatchesAsync(
        string path,
        string? ifMatchETag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ifMatchETag))
        {
            return;
        }

        var actual = await ComputeChecksumAsync(path, cancellationToken);
        var expected = ifMatchETag.Trim().Trim('"');
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Stored object ETag does not match the requested version.");
        }
    }

    private static async Task<string> ComputeChecksumAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream"
    };

    private static string NormalizeContentType(string contentType) =>
        (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static (Guid LeaseId, string Secret) ParseToken(string token)
    {
        var parts = (token ?? string.Empty).Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !Guid.TryParseExact(parts[0], "N", out var leaseId) ||
            parts[1].Length != 64 ||
            parts[1].Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_TOKEN_INVALID",
                "Upload lease token is invalid.");
        }

        return (leaseId, parts[1].ToLowerInvariant());
    }

    private static (Guid LeaseId, string Secret) ParseDownloadToken(string token)
    {
        var parts = (token ?? string.Empty).Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !Guid.TryParseExact(parts[0], "N", out var leaseId) ||
            parts[1].Length != 64 ||
            parts[1].Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new DevelopmentPresignedDownloadException(
                "DEVELOPMENT_DOWNLOAD_TOKEN_INVALID",
                "Download lease token is invalid.");
        }

        return (leaseId, parts[1].ToLowerInvariant());
    }

    private static SemaphoreSlim GetLeaseLock(Guid leaseId)
    {
        lock (LeaseLocksGate)
        {
            var key = leaseId.ToString("N");
            if (!LeaseLocks.TryGetValue(key, out var leaseLock))
            {
                leaseLock = new SemaphoreSlim(1, 1);
                LeaseLocks[key] = leaseLock;
            }

            return leaseLock;
        }
    }

    private async Task<DevelopmentUploadLease> ReadLeaseAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var path = ResolveLeasePath(leaseId);
        if (!File.Exists(path))
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_LEASE_NOT_FOUND",
                "Upload lease was not found.");
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<DevelopmentUploadLease>(json, JsonOptions)
            ?? throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_LEASE_INVALID",
                "Upload lease metadata is invalid.");
    }

    private async Task<DevelopmentDownloadLease> ReadDownloadLeaseAsync(
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        var path = ResolveDownloadLeasePath(leaseId);
        if (!File.Exists(path))
        {
            throw new DevelopmentPresignedDownloadException(
                "DEVELOPMENT_DOWNLOAD_LEASE_NOT_FOUND",
                "Download lease was not found.");
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<DevelopmentDownloadLease>(json, JsonOptions)
            ?? throw new DevelopmentPresignedDownloadException(
                "DEVELOPMENT_DOWNLOAD_LEASE_INVALID",
                "Download lease metadata is invalid.");
    }

    private Task WriteLeaseAsync(DevelopmentUploadLease lease, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            ResolveLeasePath(lease.LeaseId),
            JsonSerializer.Serialize(lease, JsonOptions),
            cancellationToken);

    private static void ValidateLease(DevelopmentUploadLease lease, string tokenSecret, string? requestContentType)
    {
        if (!string.Equals(lease.SecretSha256, HashTokenSecret(tokenSecret), StringComparison.OrdinalIgnoreCase))
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_TOKEN_INVALID",
                "Upload lease token is invalid.");
        }

        if (!string.Equals(lease.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_REPLAYED",
                "This upload lease has already been used.");
        }

        if (lease.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_LEASE_EXPIRED",
                "Upload lease has expired.");
        }

        if (!string.Equals(NormalizeContentType(requestContentType ?? string.Empty), lease.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_CONTENT_TYPE_MISMATCH",
                "Uploaded file type does not match the approved upload lease.");
        }
    }

    private async Task ValidateStoredFileAsync(
        Stream file,
        DevelopmentUploadLease lease,
        CancellationToken cancellationToken)
    {
        var allowedTypes = FileValidationService.GetAllowedFileTypesForMimeTypes(new[] { lease.ContentType });
        if (allowedTypes.Count == 0)
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_CONTENT_TYPE_UNSUPPORTED",
                "Uploaded file type is not supported.");
        }

        var validation = await _fileValidationService.ValidateFileAsync(
            file,
            Path.GetFileName(lease.ObjectKey),
            lease.ContentType,
            allowedTypes,
            cancellationToken);
        if (!validation.IsValid)
        {
            throw new DevelopmentPresignedUploadException(
                "CHECKOUT_ATTACHMENT_UPLOAD_SIGNATURE_MISMATCH",
                validation.ErrorMessage ?? "Uploaded file content does not match the approved file type.");
        }
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return total;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new DevelopmentPresignedUploadException(
                    "CHECKOUT_ATTACHMENT_UPLOAD_TOO_LARGE",
                    "Uploaded file exceeds the development upload limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string HashTokenSecret(string secret)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record DevelopmentUploadLease(
        Guid LeaseId,
        string SecretSha256,
        string ObjectKey,
        string ContentType,
        DateTime ExpiresAtUtc,
        string Status,
        DateTime? UsedAtUtc = null);

    private sealed record DevelopmentDownloadLease(
        Guid LeaseId,
        string SecretSha256,
        string ObjectKey,
        DateTime ExpiresAtUtc);
}
