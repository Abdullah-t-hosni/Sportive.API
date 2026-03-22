using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _images;
    private readonly AppDbContext _db;

    public ImagesController(IImageService images, AppDbContext db)
    {
        _images = images;
        _db     = db;
    }

    [HttpPost("products/{productId}")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadProductImage(
        int productId, IFormFile file, [FromQuery] bool isMain = false)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return NotFound(new { message = "المنتج غير موجود" });

        var result = await _images.UploadProductImageAsync(file, productId);
        if (!result.Success) return BadRequest(new { message = result.Error });

        if (isMain)
        {
            var existing = _db.ProductImages.Where(i => i.ProductId == productId && i.IsMain);
            foreach (var img in existing) img.IsMain = false;
        }

        var productImage = new ProductImage
        {
            ProductId = productId,
            ImageUrl  = result.Url!,
            IsMain    = isMain,
            SortOrder = _db.ProductImages.Count(i => i.ProductId == productId)
        };

        _db.ProductImages.Add(productImage);
        await _db.SaveChangesAsync();

        return Ok(new { id = productImage.Id, url = result.Url, publicId = result.PublicId, isMain });
    }

    [HttpPost("categories/{categoryId}")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadCategoryImage(int categoryId, IFormFile file)
    {
        var category = await _db.Categories.FindAsync(categoryId);
        if (category == null) return NotFound(new { message = "القسم غير موجود" });

        var result = await _images.UploadCategoryImageAsync(file, categoryId);
        if (!result.Success) return BadRequest(new { message = result.Error });

        category.ImageUrl  = result.Url;
        category.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { url = result.Url, publicId = result.PublicId });
    }

    [HttpDelete("products/images/{imageId}")]
    public async Task<IActionResult> DeleteProductImage(int imageId, [FromQuery] string? publicId)
    {
        var image = await _db.ProductImages.FindAsync(imageId);
        if (image == null) return NotFound();

        if (!string.IsNullOrEmpty(publicId))
            await _images.DeleteImageAsync(publicId);

        _db.ProductImages.Remove(image);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
