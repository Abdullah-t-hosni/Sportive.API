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
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _http;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

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
            var basePath = GetUploadBasePath();
            var filePath = Path.Combine(basePath, publicId.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(filePath)) File.Delete(filePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image {PublicId}", publicId);
            return Task.FromResult(false);
        }
    }

    private string GetUploadBasePath()
    {
        // On Railway, WebRootPath is usually null. We use ContentRootPath/wwwroot/uploads as per Program.cs configuration.
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        return Path.Combine(webRoot, "uploads");
    }

    private async Task<ImageUploadDto> UploadAsync(IFormFile file, string folder)
    {
        if (file == null || file.Length == 0)
            return new ImageUploadDto(false, null, null, "لم يتم إرسال أي ملف");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return new ImageUploadDto(false, null, null,
                $"نوع الملف غير مدعوم. المسموح: {string.Join(", ", AllowedExtensions)}");

        if (file.Length > MaxFileSizeBytes)
            return new ImageUploadDto(false, null, null, "حجم الصورة يتجاوز 5 ميجابايت");

        try
        {
            var basePath    = GetUploadBasePath();
            var uploadFolder = Path.Combine(basePath, folder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(uploadFolder);

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadFolder, fileName);
            var publicId = $"{folder}/{fileName}";

            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream);

            var request = _http.HttpContext?.Request;
            var baseUrl = request != null
                ? $"{request.Scheme}://{request.Host}"
                : "https://sportiveapi-production.up.railway.app";

            var url = $"{baseUrl}/uploads/{folder}/{fileName}";

            _logger.LogInformation("Image saved: {Path}", filePath);
            return new ImageUploadDto(true, url, publicId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image upload failed for folder {Folder}", folder);
            return new ImageUploadDto(false, null, null, $"فشل حفظ الصورة: {ex.Message}");
        }
    }
}