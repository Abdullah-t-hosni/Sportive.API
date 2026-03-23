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
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryImageService> _logger;

    public CloudinaryImageService(IConfiguration config, ILogger<CloudinaryImageService> logger)
    {
        var section = config.GetSection("Cloudinary");
        var account = new Account(
            section["CloudName"],
            section["ApiKey"],
            section["ApiSecret"]
        );
        _cloudinary = new Cloudinary(account);
        _logger = logger;
    }

    public async Task<ImageUploadDto> UploadProductImageAsync(IFormFile file, int productId)
        => await UploadAsync(file, $"sportive/products/{productId}");

    public async Task<ImageUploadDto> UploadCategoryImageAsync(IFormFile file, int categoryId)
        => await UploadAsync(file, $"sportive/categories/{categoryId}");

    public async Task<bool> DeleteImageAsync(string publicId)
    {
        try
        {
            var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId));
            return result.Result == "ok";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudinary delete failed for {PublicId}", publicId);
            return false;
        }
    }

    private async Task<ImageUploadDto> UploadAsync(IFormFile file, string folder)
    {
        if (file == null || file.Length == 0)
            return new ImageUploadDto(false, null, null, "No file uploaded.");

        try
        {
            await using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                Transformation = new Transformation().Quality("auto").FetchFormat("auto")
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                return new ImageUploadDto(false, null, null, result.Error.Message);

            return new ImageUploadDto(true, result.SecureUrl.ToString(), result.PublicId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudinary upload failed");
            return new ImageUploadDto(false, null, null, "فشل رفع الصورة للسحابة");
        }
    }
}
