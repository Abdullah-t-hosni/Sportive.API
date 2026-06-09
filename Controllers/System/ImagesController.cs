using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _images;
    private readonly AppDbContext _db;

    public ImagesController(IImageService images, AppDbContext db)
    {
        _images = images;
        _db     = db;
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPost("products/{productId}")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadProductImage(
        [FromRoute] int productId, [FromForm] IFormFile file, [FromQuery] bool isMain = false, [FromQuery] string? colorAr = null)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return NotFound(new { message = "Ø§Ù„Ù…Ù†ØªØ¬ ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯" });

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
            ImagePublicId = result.PublicId, // Save this!
            IsMain    = isMain,
            ColorAr   = colorAr,
            SortOrder = _db.ProductImages.Count(i => i.ProductId == productId)
        };

        _db.ProductImages.Add(productImage);
        await _db.SaveChangesAsync();

        return Ok(new { id = productImage.Id, url = result.Url, publicId = result.PublicId, isMain });
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPost("products/variants/{variantId}")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadVariantImage([FromRoute] int variantId, [FromForm] IFormFile file)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return NotFound(new { message = "Ø§Ù„Ù…ÙˆØ¯ÙŠÙ„ ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯" });

        var result = await _images.UploadAttachmentAsync(file, $"variants/{variantId}");
        if (!result.Success) return BadRequest(new { message = result.Error });

        variant.ImageUrl = result.Url;
        variant.ImagePublicId = result.PublicId;
        await _db.SaveChangesAsync();

        return Ok(new { url = result.Url, publicId = result.PublicId });
    }

    [RequirePermission(ModuleKeys.Categories, requireEdit: true)]
    [HttpPost("categories/{categoryId}")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadCategoryImage([FromRoute] int categoryId, [FromForm] IFormFile file)
    {
        var category = await _db.Categories.FindAsync(categoryId);
        if (category == null) return NotFound(new { message = "Ø§Ù„Ù‚Ø³Ù… ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯" });

        var result = await _images.UploadCategoryImageAsync(file, categoryId);
        if (!result.Success) return BadRequest(new { message = result.Error });

        category.ImageUrl  = result.Url;
        category.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new { url = result.Url, publicId = result.PublicId });
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPost("attachments/{type}/{id}")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB
    public async Task<IActionResult> UploadAttachment([FromRoute] string type, [FromRoute] int id, [FromForm] IFormFile file)
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
            case "employee":
                var emp = await _db.Employees.FindAsync(id);
                if (emp == null) return NotFound();
                emp.AttachmentUrl = result.Url;
                emp.AttachmentPublicId = result.PublicId;
                break;
            default:
                return BadRequest(new { message = "Entity type not supported" });
        }

        await _db.SaveChangesAsync();
        return Ok(new { url = result.Url, publicId = result.PublicId });
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("products/images/{imageId}/metadata")]
    public async Task<IActionResult> UpdateImageMetadata(int imageId, [FromQuery] string? colorAr, [FromQuery] bool? isMain)
    {
        var image = await _db.ProductImages.FindAsync(imageId);
        if (image == null) return NotFound();

        if (colorAr != null) image.ColorAr = colorAr == "null" ? null : colorAr;
        if (isMain.HasValue)
        {
            if (isMain.Value)
            {
                var existing = _db.ProductImages.Where(i => i.ProductId == image.ProductId && i.IsMain);
                foreach (var img in existing) img.IsMain = false;
            }
            image.IsMain = isMain.Value;
        }

        await _db.SaveChangesAsync();
        return Ok(new { id = image.Id, colorAr = image.ColorAr, isMain = image.IsMain });
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("products/images/{imageId}")]
    public async Task<IActionResult> DeleteProductImage(int imageId)
    {
        var image = await _db.ProductImages.FindAsync(imageId);
        if (image == null) return NotFound();

        if (!string.IsNullOrEmpty(image.ImagePublicId))
            await _images.DeleteImageAsync(image.ImagePublicId);

        _db.ProductImages.Remove(image);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("products/variants/{variantId}")]
    public async Task<IActionResult> DeleteVariantImage(int variantId)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return NotFound();

        if (!string.IsNullOrEmpty(variant.ImagePublicId))
            await _images.DeleteImageAsync(variant.ImagePublicId);

        variant.ImageUrl = null;
        variant.ImagePublicId = null;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ══════════════════════════════════════════════════════
    // Multi-Attachment Endpoints (EntityAttachments table)
    // ══════════════════════════════════════════════════════

    private static readonly HashSet<string> AllowedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        { "order", "purchase", "journalentry", "assetpurchase" };

    /// <summary>رفع مرفق جديد لـ entity (يضاف للقائمة ولا يستبدل)</summary>
    [Authorize]
    [HttpPost("entity-attachments/{type}/{id}")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadEntityAttachment(
        [FromRoute] string type, [FromRoute] int id, [FromForm] IFormFile file)
    {
        if (!AllowedEntityTypes.Contains(type))
            return BadRequest(new { message = $"Entity type '{type}' is not supported" });

        var result = await _images.UploadAttachmentAsync(file, $"{type}/{id}");
        if (!result.Success) return BadRequest(new { message = result.Error });

        var attachment = new EntityAttachment
        {
            EntityType       = type.ToLower(),
            EntityId         = id,
            Url              = result.Url!,
            PublicId         = result.PublicId,
            FileName         = file.FileName,
            ContentType      = file.ContentType,
            FileSizeBytes    = file.Length,
            UploadedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        };

        _db.EntityAttachments.Add(attachment);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            attachment.Id,
            attachment.Url,
            attachment.PublicId,
            attachment.FileName,
            attachment.ContentType,
            attachment.FileSizeBytes,
            attachment.CreatedAt
        });
    }

    /// <summary>جلب كل المرفقات لـ entity معين</summary>
    [Authorize]
    [HttpGet("entity-attachments/{type}/{id}")]
    public async Task<IActionResult> GetEntityAttachments(
        [FromRoute] string type, [FromRoute] int id)
    {
        if (!AllowedEntityTypes.Contains(type))
            return BadRequest(new { message = $"Entity type '{type}' is not supported" });

        var attachments = await _db.EntityAttachments
            .AsNoTracking()
            .Where(a => a.EntityType == type.ToLower() && a.EntityId == id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.Url,
                a.FileName,
                a.ContentType,
                a.FileSizeBytes,
                a.CreatedAt
            })
            .ToListAsync();

        return Ok(attachments);
    }

    /// <summary>حذف مرفق واحد بالـ ID</summary>
    [Authorize]
    [HttpDelete("entity-attachments/{attachmentId:int}")]
    public async Task<IActionResult> DeleteEntityAttachment([FromRoute] int attachmentId)
    {
        var attachment = await _db.EntityAttachments.FindAsync(attachmentId);
        if (attachment == null) return NotFound();

        if (!string.IsNullOrEmpty(attachment.PublicId))
            await _images.DeleteImageAsync(attachment.PublicId);

        _db.EntityAttachments.Remove(attachment);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
