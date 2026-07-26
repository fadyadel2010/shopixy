namespace PointOfSale.API.Services;

public interface IImageService
{
    Task<string> UploadImageAsync(IFormFile file, string folder = "products");
    Task<bool> DeleteImageAsync(string imageUrl);
    Task<List<string>> UploadMultipleImagesAsync(List<IFormFile> files, string folder = "products");
}

public class ImageService : IImageService
{
    private readonly IConfiguration _configuration;
    private readonly IFileValidationService _fileValidationService;
    private readonly ILogger<ImageService> _logger;
    private readonly string _uploadPath;
    private readonly long _maxFileSize = 5 * 1024 * 1024; // 5MB

    public ImageService(
        IConfiguration configuration,
        IFileValidationService fileValidationService,
        ILogger<ImageService> logger,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _fileValidationService = fileValidationService;
        _logger = logger;
        
        // For now, store locally. Can be switched to Azure Blob Storage later
        _uploadPath = Path.Combine(environment.WebRootPath ?? environment.ContentRootPath, "uploads", "images");
        
        // Ensure directory exists
        if (!Directory.Exists(_uploadPath))
        {
            Directory.CreateDirectory(_uploadPath);
        }
    }

    public async Task<string> UploadImageAsync(IFormFile file, string folder = "products")
    {
        // Validate file
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty");

        if (file.Length > _maxFileSize)
            throw new ArgumentException($"File size exceeds maximum allowed size of {_maxFileSize / 1024 / 1024}MB");

        FileValidationResult validationResult;
        await using (var validationStream = file.OpenReadStream())
        {
            validationResult = await _fileValidationService.ValidateFileAsync(
                validationStream,
                file.FileName,
                file.ContentType,
                FileValidationProfiles.Images);
        }

        if (!validationResult.IsValid || string.IsNullOrWhiteSpace(validationResult.ValidatedExtension))
            throw new ArgumentException("Unsupported or invalid image type");

        var extension = validationResult.ValidatedExtension;

        // Generate unique filename
        var fileName = $"{Guid.NewGuid()}{extension}";
        var folderPath = Path.Combine(_uploadPath, folder);
        
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var filePath = Path.Combine(folderPath, fileName);

        // Save file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Resize image (optional - you can add image resizing library like ImageSharp here)
        // For now, just return the path
        
        // Return relative URL
        return $"/uploads/images/{folder}/{fileName}";
    }

    public async Task<List<string>> UploadMultipleImagesAsync(List<IFormFile> files, string folder = "products")
    {
        var uploadedUrls = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var url = await UploadImageAsync(file, folder);
                uploadedUrls.Add(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to upload image: {file.FileName}");
                // Continue with other files
            }
        }

        return uploadedUrls;
    }

    public Task<bool> DeleteImageAsync(string imageUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return Task.FromResult(false);

            // Extract filename from URL
            var fileName = Path.GetFileName(imageUrl);
            var folder = imageUrl.Contains("/products/") ? "products" : "other";
            var filePath = Path.Combine(_uploadPath, folder, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to delete image: {imageUrl}");
            return Task.FromResult(false);
        }
    }
}

// TODO: Azure Blob Storage implementation
// Uncomment and configure when ready to use Azure
/*
public class AzureBlobImageService : IImageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly ILogger<AzureBlobImageService> _logger;

    public AzureBlobImageService(IConfiguration configuration, ILogger<AzureBlobImageService> logger)
    {
        var connectionString = configuration["Azure:BlobStorage:ConnectionString"];
        _containerName = configuration["Azure:BlobStorage:ContainerName"] ?? "product-images";
        _blobServiceClient = new BlobServiceClient(connectionString);
        _logger = logger;
    }

    public async Task<string> UploadImageAsync(IFormFile file, string folder = "products")
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var fileName = $"{folder}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var blobClient = containerClient.GetBlobClient(fileName);

        using (var stream = file.OpenReadStream())
        {
            await blobClient.UploadAsync(stream, overwrite: true);
        }

        return blobClient.Uri.ToString();
    }

    // ... implement other methods
}
*/
