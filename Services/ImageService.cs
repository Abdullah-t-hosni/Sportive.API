namespace Sportive.API.Services;

public interface IImageService
{
    Task<ImageUploadDto> UploadProductImageAsync(IFormFile file, int productId);
    Task<ImageUploadDto> UploadCategoryImageAsync(IFormFile file, int categoryId);
    Task<bool> DeleteImageAsync(string publicId);
}

public record ImageUploadDto(bool Success, string? Url, string? PublicId, string? Error);

/// <summary>
/// Local image service — يحفظ الصور على الـ server محلياً
/// بدل Cloudinary للتطوير والتجربة
/// </summary>
public class CloudinaryImageService : IImageService
{
    private readonly ILogger<CloudinaryImageService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _http;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public CloudinaryImageService(
        ILogger<CloudinaryImageService> logger,
        IWebHostEnvironment env,
        IHttpContextAccessor http)
    {
        _logger = logger;
        _env    = env;
        _http   = http;
    }

    public async Task<ImageUploadDto> UploadProductImageAsync(IFormFile file, int productId)
        => await UploadAsync(file, $"products/{productId}");

    public async Task<ImageUploadDto> UploadCategoryImageAsync(IFormFile file, int categoryId)
        => await UploadAsync(file, $"categories/{categoryId}");

    public Task<bool> DeleteImageAsync(string publicId)
    {
        try
        {
            var filePath = Path.Combine(_env.WebRootPath, "uploads", publicId.TrimStart('/'));
            if (File.Exists(filePath))
                File.Delete(filePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image {PublicId}", publicId);
            return Task.FromResult(false);
        }
    }

    private async Task<ImageUploadDto> UploadAsync(IFormFile file, string folder)
    {
        // Validate extension
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!AllowedExtensions.Contains(ext))
            return new ImageUploadDto(false, null, null,
                $"نوع الملف غير مدعوم. المسموح: {string.Join(", ", AllowedExtensions)}");

        // Validate size
        if (file.Length > MaxFileSizeBytes)
            return new ImageUploadDto(false, null, null, "حجم الصورة يتجاوز 5 ميجابايت");

        try
        {
            // Create folder
            var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", folder);
            Directory.CreateDirectory(uploadFolder);

            // Unique filename
            var fileName  = $"{Guid.NewGuid()}{ext}";
            var filePath  = Path.Combine(uploadFolder, fileName);
            var publicId  = $"{folder}/{fileName}";

            // Save file
            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream);

            // Build URL
            var request = _http.HttpContext?.Request;
            var baseUrl = request != null
                ? $"{request.Scheme}://{request.Host}"
                : "http://localhost:5000";

            var url = $"{baseUrl}/uploads/{folder}/{fileName}";

            _logger.LogInformation("Image saved: {Path}", filePath);
            return new ImageUploadDto(true, url, publicId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image upload failed");
            return new ImageUploadDto(false, null, null, "فشل حفظ الصورة");
        }
    }
}
