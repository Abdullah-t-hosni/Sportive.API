using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Data;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;
using System.Text;
using System.Linq;

namespace Sportive.API.Controllers.Public;

[Route("api/public")]
[ApiController]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly ITenantService _tenantService;
    private readonly AppDbContext _db;

    public PublicController(IPlanService planService, ITenantService tenantService, AppDbContext db)
    {
        _planService = planService;
        _tenantService = tenantService;
        _db = db;
    }

    /// <summary>جلب الباقات المتاحة للعرض في صفحة الأسعار</summary>
    [HttpGet("plans")]
    public async Task<IActionResult> GetActivePlans()
    {
        var plans = await _planService.GetAllPlansAsync(includeInactive: false);
        return Ok(new { success = true, data = plans });
    }

    /// <summary>
    /// التحقق من توافر اسم النطاق الفرعي (Slug) قبل التسجيل
    /// GET /api/public/check-slug/{slug}
    /// </summary>
    [HttpGet("check-slug/{slug}")]
    public async Task<IActionResult> CheckSlugAvailability(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length < 3)
            return BadRequest(new { available = false, message = "الـ Slug يجب أن يكون 3 أحرف على الأقل." });

        // Validate format
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9][a-z0-9\-]{1,48}[a-z0-9]$"))
            return BadRequest(new { available = false, message = "الـ Slug يحتوي على أحرف غير مسموحة. استخدم أحرفاً إنجليزية صغيرة وأرقام وشرطات فقط." });

        // Reserved slugs
        var reserved = new[] { "admin", "api", "www", "app", "mail", "smtp", "raakiza", "sportive", "support", "help", "test", "staging", "dev" };
        if (System.Array.Exists(reserved, r => r == slug.ToLowerInvariant()))
            return Ok(new { available = false, message = "هذا الاسم محجوز ولا يمكن استخدامه." });

        var available = await _tenantService.IsSlugAvailableAsync(slug);
        return Ok(new
        {
            available,
            message = available ? "هذا الاسم متاح! ✓" : "هذا الاسم محجوز مسبقاً.",
            subdomain = available ? $"{slug.ToLowerInvariant()}.raakiza.com" : null
        });
    }

    /// <summary>
    /// تسجيل عميل جديد من الموقع التسويقي (Self-Onboarding)
    /// POST /api/public/self-register
    /// </summary>
    [HttpPost("self-register")]
    public async Task<IActionResult> SelfRegister([FromBody] SelfRegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, errors = ModelState });

        var result = await _tenantService.SelfRegisterAsync(request);

        if (!result.Success)
            return BadRequest(new { success = false, message = result.Message });

        return Ok(new
        {
            success = true,
            message = result.Message,
            subdomain = result.Subdomain,
            adminEmail = result.AdminEmail
        });
    }

    /// <summary>
    /// توليد ملف منتجات متوافق مع Facebook Catalog Feed (RSS 2.0 XML)
    /// GET /api/public/facebook-feed
    /// </summary>
    [HttpGet("facebook-feed")]
    public async Task<IActionResult> GetFacebookFeed()
    {
        // 1. Fetch active products along with images and brand/category details
        var products = await _db.Products
            .AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Where(p => p.Status == ProductStatus.Active)
            .ToListAsync();

        // 2. Prepare domain URLs
        var request = HttpContext.Request;
        var host = request.Host.Value ?? "sportive.eg";
        
        // Resolve frontend domain dynamically using ITenantContext if available
        var tenantContext = HttpContext.RequestServices.GetService<ITenantContext>();
        var currentTenant = tenantContext?.CurrentTenant;
        
        var frontendDomain = "sportive-sportwear.com"; // Hardcoded default production domain
        if (currentTenant != null && !string.IsNullOrEmpty(currentTenant.CustomDomain))
        {
            frontendDomain = currentTenant.CustomDomain;
        }
        else
        {
            if (!string.IsNullOrEmpty(host))
            {
                if (host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
                {
                    frontendDomain = host.Substring(4);
                }
                else if (host.Contains("localhost") || host.Contains("127.0.0.1"))
                {
                    frontendDomain = host;
                }
            }
        }
        
        var scheme = request.Scheme;
        if (!host.Contains("localhost") && !host.Contains("127.0.0.1"))
        {
            scheme = "https"; // Force secure protocol for production domains
        }
        
        var frontendBaseUrl = $"{scheme}://{frontendDomain}";
        var apiBaseUrl = $"{scheme}://{host}";

        // 3. Construct XML structure using XDocument (Google Merchant / Facebook Feed standard namespaces)
        XNamespace g = "http://base.google.com/ns/1.0";
        
        var channel = new XElement("channel",
            new XElement("title", "Sportive Product Feed"),
            new XElement("link", frontendBaseUrl),
            new XElement("description", "Automatic daily product catalog update feed for Sportive E-commerce.")
        );

        foreach (var p in products)
        {
            // Build absolute main image URL
            var mainImage = p.Images.FirstOrDefault(img => img.IsMain) ?? p.Images.FirstOrDefault();
            var imageUrl = mainImage?.ImageUrl ?? "/uploads/placeholder.jpg";
            if (!imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                imageUrl = $"{apiBaseUrl}{imageUrl}";
            }

            var itemUrl = $"{frontendBaseUrl}/products/{p.Slug}";

            // availability status mapping
            var availability = p.TotalStock > 0 ? "in stock" : "out of stock";

            // Description clean fallback
            var description = string.IsNullOrWhiteSpace(p.DescriptionAr) ? p.NameAr : p.DescriptionAr;
            // Facebook expects a minimum description
            if (string.IsNullOrWhiteSpace(description))
            {
                description = p.NameEn;
            }

            var itemElement = new XElement("item",
                new XElement(g + "id", p.Id.ToString()),
                new XElement("title", p.NameAr),
                new XElement("description", description),
                new XElement("link", itemUrl),
                new XElement(g + "image_link", imageUrl),
                new XElement(g + "brand", p.Brand?.NameAr ?? "Sportive"),
                new XElement(g + "condition", "new"),
                new XElement(g + "availability", availability),
                new XElement(g + "price", $"{p.Price:F2} EGP")
            );

            // Optional discounted price mapping
            if (p.DiscountPrice.HasValue && p.DiscountPrice.Value < p.Price && p.DiscountPrice.Value > 0)
            {
                itemElement.Add(new XElement(g + "sale_price", $"{p.DiscountPrice.Value:F2} EGP"));
            }

            // Google product category map (optional but useful)
            if (p.Category != null)
            {
                itemElement.Add(new XElement(g + "google_product_category", p.Category.NameEn ?? p.Category.NameAr));
            }

            channel.Add(itemElement);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("rss", 
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "g", g),
                channel
            )
        );

        // Return as application/xml
        return Content(doc.ToString(), "application/xml", Encoding.UTF8);
    }
}
