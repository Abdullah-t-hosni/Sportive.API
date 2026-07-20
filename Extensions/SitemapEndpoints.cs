using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Sportive.API.Data;
using System.Linq;
using System.Text;

namespace Sportive.API.Extensions
{
    public static class SitemapEndpoints
    {
        public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/sitemap.xml", async (HttpContext context, IConfiguration config) =>
            {
                var host = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
                
                var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <sitemap>
        <loc>{host}/sitemap-products.xml</loc>
    </sitemap>
    <sitemap>
        <loc>{host}/sitemap-categories.xml</loc>
    </sitemap>
    <sitemap>
        <loc>{host}/sitemap-brands.xml</loc>
    </sitemap>
    <sitemap>
        <loc>{host}/sitemap-pages.xml</loc>
    </sitemap>
</sitemapindex>";
                
                return Results.Content(xml, "application/xml", Encoding.UTF8);
            });

            endpoints.MapGet("/sitemap-products.xml", async (AppDbContext db, IMemoryCache cache, IConfiguration config) =>
            {
                var baseUrl = config["Store:Url"]?.TrimEnd('/') ?? "https://sportive-sportwear.com";
                
                if (!cache.TryGetValue("sitemap_products", out string xml))
                {
                    var products = await db.Products
                        .Where(p => p.Status == Sportive.API.Models.ProductStatus.Active || p.Status == Sportive.API.Models.ProductStatus.OutOfStock)
                        .Select(p => new { p.Slug, p.Id, p.UpdatedAt })
                        .ToListAsync();

                    var sb = new StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
                    
                    foreach (var p in products)
                    {
                        var identifier = !string.IsNullOrEmpty(p.Slug) ? p.Slug : p.Id.ToString();
                        var lastMod = string.Format("{0:yyyy-MM-dd}", p.UpdatedAt);
                        if (lastMod == "") lastMod = DateTime.UtcNow.ToString("yyyy-MM-dd");
                        sb.AppendLine($"  <url>");
                        sb.AppendLine($"    <loc>{baseUrl}/products/{identifier}</loc>");
                        sb.AppendLine($"    <lastmod>{lastMod}</lastmod>");
                        sb.AppendLine($"    <changefreq>daily</changefreq>");
                        sb.AppendLine($"    <priority>0.8</priority>");
                        sb.AppendLine($"  </url>");
                    }
                    
                    sb.AppendLine("</urlset>");
                    xml = sb.ToString();
                    
                    cache.Set("sitemap_products", xml, TimeSpan.FromHours(1));
                }
                
                return Results.Content(xml, "application/xml", Encoding.UTF8);
            });

            endpoints.MapGet("/sitemap-categories.xml", async (AppDbContext db, IMemoryCache cache, IConfiguration config) =>
            {
                var baseUrl = config["Store:Url"]?.TrimEnd('/') ?? "https://sportive-sportwear.com";
                
                if (!cache.TryGetValue("sitemap_categories", out string xml))
                {
                    var categories = await db.Categories.Select(c => c.Id).ToListAsync();

                    var sb = new StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
                    
                    foreach (var id in categories)
                    {
                        sb.AppendLine($"  <url>");
                        sb.AppendLine($"    <loc>{baseUrl}/store?categoryId={id}</loc>");
                        sb.AppendLine($"    <changefreq>weekly</changefreq>");
                        sb.AppendLine($"    <priority>0.7</priority>");
                        sb.AppendLine($"  </url>");
                    }
                    
                    sb.AppendLine("</urlset>");
                    xml = sb.ToString();
                    
                    cache.Set("sitemap_categories", xml, TimeSpan.FromHours(1));
                }
                
                return Results.Content(xml, "application/xml", Encoding.UTF8);
            });

            endpoints.MapGet("/sitemap-brands.xml", async (AppDbContext db, IMemoryCache cache, IConfiguration config) =>
            {
                var baseUrl = config["Store:Url"]?.TrimEnd('/') ?? "https://sportive-sportwear.com";
                
                if (!cache.TryGetValue("sitemap_brands", out string xml))
                {
                    var brands = await db.Brands.Select(b => b.Id).ToListAsync();

                    var sb = new StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
                    
                    foreach (var id in brands)
                    {
                        sb.AppendLine($"  <url>");
                        sb.AppendLine($"    <loc>{baseUrl}/store?brandId={id}</loc>");
                        sb.AppendLine($"    <changefreq>weekly</changefreq>");
                        sb.AppendLine($"    <priority>0.6</priority>");
                        sb.AppendLine($"  </url>");
                    }
                    
                    sb.AppendLine("</urlset>");
                    xml = sb.ToString();
                    
                    cache.Set("sitemap_brands", xml, TimeSpan.FromHours(1));
                }
                
                return Results.Content(xml, "application/xml", Encoding.UTF8);
            });

            endpoints.MapGet("/sitemap-pages.xml", async (IConfiguration config) =>
            {
                var baseUrl = config["Store:Url"]?.TrimEnd('/') ?? "https://sportive-sportwear.com";
                
                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
                
                var pages = new[] { "", "/store" }; // Add other static pages if necessary
                foreach (var p in pages)
                {
                    sb.AppendLine($"  <url>");
                    sb.AppendLine($"    <loc>{baseUrl}{p}</loc>");
                    sb.AppendLine($"    <changefreq>daily</changefreq>");
                    sb.AppendLine($"    <priority>{(p == "" ? "1.0" : "0.9")}</priority>");
                    sb.AppendLine($"  </url>");
                }
                
                sb.AppendLine("</urlset>");
                
                return Results.Content(sb.ToString(), "application/xml", Encoding.UTF8);
            });

            // Product redirect fallback for legacy Facebook Catalog Feed ads pointing to Railway backend
            endpoints.MapGet("/products/{*slug}", (string slug, HttpContext context) =>
            {
                var queryString = context.Request.QueryString.Value ?? "";
                var targetUrl = $"https://sportive-sportwear.com/products/{slug}{queryString}";
                return Results.Redirect(targetUrl, permanent: true);
            });

            return endpoints;
        }
    }
}
