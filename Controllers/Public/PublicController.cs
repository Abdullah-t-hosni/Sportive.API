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
using Microsoft.Extensions.Configuration;

namespace Sportive.API.Controllers.Public;

[Route("api/public")]
[ApiController]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly ITenantService _tenantService;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public PublicController(IPlanService planService, ITenantService tenantService, AppDbContext db, IConfiguration config)
    {
        _planService = planService;
        _tenantService = tenantService;
        _db = db;
        _config = config;
    }

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
    /// GET /api/public/facebook-feed?section=men&categoryId=12
    /// </summary>
    [HttpGet("facebook-feed")]
    public async Task<IActionResult> GetFacebookFeed(
        [FromQuery] string? section = null,
        [FromQuery] CategoryType? type = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] decimal? originalPriceMultiplier = null,
        [FromQuery] decimal? discountPercent = null,
        [FromQuery] bool? showOriginalPrice = null)
    {
        // 1. Fetch active products along with images and brand/category details
        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Where(p => p.Status == ProductStatus.Active);

        string feedTitle = "Sportive Product Feed";

        // Section filter (men, women, kids, shoes, equipment, special)
        if (!string.IsNullOrWhiteSpace(section))
        {
            var secLower = section.Trim().ToLowerInvariant();
            CategoryType? targetType = secLower switch
            {
                "men" => CategoryType.Men,
                "women" => CategoryType.Women,
                "kids" => CategoryType.Kids,
                "equipment" => CategoryType.Equipment,
                "shoes" => CategoryType.Shoes,
                "special" => CategoryType.SpecialSizes,
                _ => null
            };

            if (targetType.HasValue)
            {
                query = query.Where(p => p.Category != null && p.Category.Type == targetType.Value);
                feedTitle = $"Sportive Product Feed - {secLower.ToUpper()}";
            }
        }
        else if (type.HasValue)
        {
            query = query.Where(p => p.Category != null && p.Category.Type == type.Value);
            feedTitle = $"Sportive Product Feed - {type.Value}";
        }

        // Subcategory filter by CategoryId (direct CategoryId or child categories)
        if (categoryId.HasValue && categoryId.Value > 0)
        {
            var childIds = await _db.Categories
                .Where(c => c.ParentId == categoryId.Value)
                .Select(c => c.Id)
                .ToListAsync();

            query = query.Where(p => p.CategoryId == categoryId.Value || (p.CategoryId.HasValue && childIds.Contains(p.CategoryId.Value)));
            
            var catObj = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId.Value);
            if (catObj != null)
            {
                feedTitle = $"Sportive Product Feed - {catObj.NameAr}";
            }
        }

        var products = await query.ToListAsync();

        // 1.5 Fetch active campaign discounts (from نظام العروض)
        var now = Sportive.API.Utils.TimeHelper.GetEgyptTime();
        var productIds = products.Select(x => x.Id).ToList();
        var categoryIds = products.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).ToList();
        var brandIds = products.Where(x => x.BrandId.HasValue).Select(x => x.BrandId!.Value).ToList();

        var activeCampaignDiscounts = await _db.ProductDiscounts
            .AsNoTracking()
            .Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now)
            .Where(d => d.ApplyTo == DiscountApplyTo.All || d.ApplyTo == DiscountApplyTo.Store)
            .Where(d => 
                (d.ProductId == null && d.CategoryId == null && d.BrandId == null) ||
                (d.ProductId != null && productIds.Contains(d.ProductId.Value)) ||
                (d.CategoryId != null && categoryIds.Contains(d.CategoryId.Value)) ||
                (d.BrandId != null && brandIds.Contains(d.BrandId.Value))
            )
            .ToListAsync();

        // 2. Prepare domain URLs
        var request = HttpContext.Request;
        var host = request.Host.Value ?? "sportive.eg";
        
        // Resolve frontend domain dynamically using ITenantContext if available
        var tenantContext = HttpContext.RequestServices.GetService<ITenantContext>();
        var currentTenant = tenantContext?.CurrentTenant;
        
        var frontendDomain = "sportive-sportwear.com"; // Hardcoded default production domain
        var storeUrlConfig = _config["Store:Url"];
        if (!string.IsNullOrEmpty(storeUrlConfig))
        {
            try
            {
                var uri = new System.Uri(storeUrlConfig);
                if (!uri.Host.Contains("railway.app") && !uri.Host.Contains("sportiveapi"))
                {
                    frontendDomain = uri.Host;
                }
            }
            catch {}
        }

        if (currentTenant != null && !string.IsNullOrEmpty(currentTenant.CustomDomain))
        {
            var cleanedDomain = currentTenant.CustomDomain.Trim().ToLowerInvariant();
            if (cleanedDomain.StartsWith("http://")) cleanedDomain = cleanedDomain.Substring(7);
            if (cleanedDomain.StartsWith("https://")) cleanedDomain = cleanedDomain.Substring(8);
            cleanedDomain = cleanedDomain.TrimEnd('/');

            if (!cleanedDomain.Contains("api") && !cleanedDomain.Contains("railway.app"))
            {
                frontendDomain = cleanedDomain;
            }
        }
        else if (host.Contains("localhost") || host.Contains("127.0.0.1"))
        {
            frontendDomain = host;
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
            new XElement("title", feedTitle),
            new XElement("link", frontendBaseUrl),
            new XElement("description", $"Automatic product catalog feed for {feedTitle}")
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

            var identifier = !string.IsNullOrEmpty(p.Slug) ? p.Slug : p.Id.ToString();
            var itemUrl = $"{frontendBaseUrl}/products/{identifier}";

            // availability status mapping
            var availability = p.TotalStock > 0 ? "in stock" : "out of stock";

            // Description clean fallback
            var description = string.IsNullOrWhiteSpace(p.DescriptionAr) ? p.NameAr : p.DescriptionAr;
            if (string.IsNullOrWhiteSpace(description))
            {
                description = p.NameEn;
            }

            // Price & Pre-Discount (Sale Price) Logic for Facebook/Meta Catalog Feed
            decimal sellingPrice = p.Price;
            decimal? originalPrice = null;

            // 1. Evaluate Campaign Discount (نظام العروض)
            var pDiscount = activeCampaignDiscounts
                .Where(d => 
                    (d.ProductId == p.Id) ||
                    (p.CategoryId.HasValue && d.CategoryId.HasValue && d.CategoryId.Value == p.CategoryId.Value) ||
                    (p.BrandId.HasValue && d.BrandId.HasValue && d.BrandId.Value == p.BrandId.Value) ||
                    (d.ProductId == null && d.CategoryId == null && d.BrandId == null)
                )
                .OrderByDescending(d => d.ProductId != null ? 4 : (d.CategoryId != null ? 3 : (d.BrandId != null ? 2 : 1)))
                .FirstOrDefault();

            if (pDiscount != null)
            {
                decimal calculatedDiscount = pDiscount.DiscountType == DiscountType.Percentage
                    ? Math.Round(p.Price - (p.Price * pDiscount.DiscountValue / 100), 2)
                    : Math.Round(p.Price - pDiscount.DiscountValue, 2);

                if (calculatedDiscount < p.Price && calculatedDiscount > 0)
                {
                    originalPrice = p.Price;
                    sellingPrice = calculatedDiscount;
                }
            }
            else if (p.DiscountPrice.HasValue && p.DiscountPrice.Value > 0 && p.DiscountPrice.Value != p.Price)
            {
                if (p.DiscountPrice.Value < p.Price)
                {
                    originalPrice = p.Price;
                    sellingPrice = p.DiscountPrice.Value;
                }
                else
                {
                    originalPrice = p.DiscountPrice.Value;
                    sellingPrice = p.Price;
                }
            }
            else if (discountPercent.HasValue && discountPercent.Value > 0 && discountPercent.Value < 100)
            {
                decimal factor = 1m - (discountPercent.Value / 100m);
                if (factor > 0)
                {
                    originalPrice = Math.Round(sellingPrice / factor, 2);
                }
            }
            else if (originalPriceMultiplier.HasValue && originalPriceMultiplier.Value > 1m)
            {
                originalPrice = Math.Round(sellingPrice * originalPriceMultiplier.Value, 2);
            }
            else if (showOriginalPrice == true)
            {
                // Default pre-discount price ratio: +25% pre-discount markup if requested without specific percentage
                originalPrice = Math.Round(sellingPrice * 1.25m, 2);
            }

            string priceStr = originalPrice.HasValue ? $"{originalPrice.Value:F2} EGP" : $"{sellingPrice:F2} EGP";

            var itemElement = new XElement("item",
                new XElement(g + "id", p.Id.ToString()),
                new XElement("title", p.NameAr),
                new XElement("description", description),
                new XElement("link", itemUrl),
                new XElement(g + "image_link", imageUrl),
                new XElement(g + "brand", p.Brand?.NameAr ?? "Sportive"),
                new XElement(g + "condition", "new"),
                new XElement(g + "availability", availability),
                new XElement(g + "price", priceStr)
            );

            // If a pre-discount original price exists, add <g:sale_price> with the actual discounted selling price
            if (originalPrice.HasValue)
            {
                itemElement.Add(new XElement(g + "sale_price", $"{sellingPrice:F2} EGP"));
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

        // Prepend XML declaration for compliance
        var xmlContent = doc.Declaration != null 
            ? doc.Declaration + System.Environment.NewLine + doc.ToString() 
            : doc.ToString();

        // Return as application/xml
        return Content(xmlContent, "application/xml", Encoding.UTF8);
    }
}
