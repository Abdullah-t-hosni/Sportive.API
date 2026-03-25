using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;

namespace Sportive.API.Services;

public interface IImageService
{
    Task<ImageUploadDto> UploadProductImageAsync(IFormFile file, int productId);
    Task<ImageUploadDto> UploadCategoryImageAsync(IFormFile file, int categoryId);
    Task<bool> DeleteImageAsync(string publicId);
}

public record ImageUploadDto(bool Success, string? Url, string? PublicId, string? Error);

public class CloudinaryImageService : IImageService
{
    private readonly ILogger<CloudinaryImageService> _logger;
    private readonly Cloudinary _cloudinary;
    private readonly IWebHostEnvironment _env;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public CloudinaryImageService(
        ILogger<CloudinaryImageService> logger,
        IConfiguration config,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;

        var acc = new Account(
            config["Cloudinary:CloudName"],
            config["Cloudinary:ApiKey"],
            config["Cloudinary:ApiSecret"]
        );
        _cloudinary = new Cloudinary(acc);
    }

    public async Task<ImageUploadDto> UploadProductImageAsync(IFormFile file, int productId)
        => await UploadToCloudinaryAsync(file, $"sportive/products/{productId}");

    public async Task<ImageUploadDto> UploadCategoryImageAsync(IFormFile file, int categoryId)
        => await UploadToCloudinaryAsync(file, $"sportive/categories/{categoryId}");

    public async Task<bool> DeleteImageAsync(string publicId)
    {
        // For Cloudinary images, publicId is the identifier.
        // For local images (fallback), we still check local disk.
        
        if (publicId.StartsWith("sportive/"))
        {
            var deleteParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deleteParams);
            return result.Result == "ok";
        }

        // Fallback for local files
        try
        {
            var localPath = Path.Combine(_env.ContentRootPath, "uploads", publicId.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath)) File.Delete(localPath);
            return true;
        }
        catch { return false; }
    }

    private async Task<ImageUploadDto> UploadToCloudinaryAsync(IFormFile file, string folder)
    {
        if (file == null || file.Length == 0)
            return new ImageUploadDto(false, null, null, "لم يتم إرسال أي ملف");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return new ImageUploadDto(false, null, null, "نوع الملف غير مدعوم");

        if (file.Length > MaxFileSizeBytes)
            return new ImageUploadDto(false, null, null, "حجم الصورة يتجاوز 5 ميجابايت");

        try
        {
            await using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                PublicId = Guid.NewGuid().ToString(),
                Overwrite = true
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
            {
                _logger.LogError("Cloudinary upload error: {Error}", uploadResult.Error.Message);
                return new ImageUploadDto(false, null, null, uploadResult.Error.Message);
            }

            return new ImageUploadDto(true, uploadResult.SecureUrl.ToString(), uploadResult.PublicId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to Cloudinary");
            return new ImageUploadDto(false, null, null, ex.Message);
        }
    }
}