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

    [HttpPost("attachments/{type}/{id}")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB
    public async Task<IActionResult> UploadAttachment(string type, int id, IFormFile file)
    {
        var result = await _images.UploadAttachmentAsync(file, $"{type}/{id}");
        if (!result.Success) return BadRequest(new { message = result.Error });

        switch (type.ToLower())
        {
            case "order":
                var order = await _db.Orders.FindAsync(id);
                if (order == null) return NotFound();
                order.AttachmentUrl = result.Url;
                order.AttachmentPublicId = result.PublicId;
                break;
            case "purchase":
                var pur = await _db.PurchaseInvoices.FindAsync(id);
                if (pur == null) return NotFound();
                pur.AttachmentUrl = result.Url;
                pur.AttachmentPublicId = result.PublicId;
                break;
            case "receiptvoucher":
                var rv = await _db.ReceiptVouchers.FindAsync(id);
                if (rv == null) return NotFound();
                rv.AttachmentUrl = result.Url;
                rv.AttachmentPublicId = result.PublicId;
                break;
            case "paymentvoucher":
                var pv = await _db.PaymentVouchers.FindAsync(id);
                if (pv == null) return NotFound();
                pv.AttachmentUrl = result.Url;
                pv.AttachmentPublicId = result.PublicId;
                break;
            case "journalentry":
                var je = await _db.JournalEntries.FindAsync(id);
                if (je == null) return NotFound();
                je.AttachmentUrl = result.Url;
                je.AttachmentPublicId = result.PublicId;
                break;
            case "supplier":
                var sup = await _db.Suppliers.FindAsync(id);
                if (sup == null) return NotFound();
                sup.AttachmentUrl = result.Url;
                sup.AttachmentPublicId = result.PublicId;
                break;
            default:
                return BadRequest(new { message = "Entity type not supported" });
        }

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
