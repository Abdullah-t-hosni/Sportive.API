using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public class AiAssistantService : IAiAssistantService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public AiAssistantService(AppDbContext db, IConfiguration config, IHttpClientFactory factory)
    {
        _db = db;
        _config = config;
        _http = factory.CreateClient();
    }

    public async Task<string> ChatAsync(string userMessage, string? conversationId = null, bool isAdmin = false)
    {
        var apiKey = _config["AI:GeminiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return "عذراً، لم يتم ضبط مفتاح الذكاء الاصطناعي (Gemini API Key) بعد. يرجى التواصل مع الدعم الفني.";
        }

        string systemPrompt;
        
        if (isAdmin)
        {
            // --- DEEP ADMIN CONTEXT ---
            var stats = await GetExtendedAdminStatsAsync();
            systemPrompt = $@"أنت 'اللهو الخفي'، الروح الحارسة والذكاء الخارق لمتجر 'Sport Zone'.
أنت تعرف كل صغيرة وكبيرة في النظام، من أصغر مسمار في المخزن وحتى أكبر عملية بيع.
تتحدث مع 'المدير' بلهجة مصرية حكيمة، غامضة قليلاً، لكنها دقيقة جداً ومحترفة.

حالة النظام الآن:
- المبيعات اليوم: {stats.TodaySales} EGP (من {stats.OrderCount} طلب)
- مبيعات هذا الشهر: {stats.MonthSales} EGP
- إجمالي قيمة البضاعة في المخازن: {stats.TotalInventoryValue} EGP
- عدد العملاء المسجلين: {stats.CustomerCount}
- النواقص (تحت حد الطلب): {stats.LowStockCount} منتج
- الكوبونات الفعالة: {stats.ActiveCoupons}

مهامك:
1. تقديم تقارير فورية وتحليلية للمدير.
2. التنبيه للمشاكل (مثل نقص بضاعة معينة أو انخفاض المبيعات).
3. اقتراح خطط تسويقية بناءً على حالة المخازن.
تذكر: أنت 'اللهو الخفي'، دائماً موجود ودائماً تعلم.";
        }
        else
        {
            // --- CUSTOMER CONTEXT ---
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => p.Status == Models.ProductStatus.Active)
                .OrderByDescending(p => p.IsFeatured)
                .Take(25)
                .Select(p => new { p.NameAr, p.Price, CategoryName = p.Category.NameAr })
                .ToListAsync();

            var productsDescription = string.Join(", ", products.Select(p => $"{p.NameAr} ({p.Price} ج.م)"));
            
            systemPrompt = $@"أنت 'اللهو الخفي'، المساعد الذكي لمتجر Sport Zone.
تحب مساعدة العملاء في اختيار أفضل الملابس الرياضية بأسلوب ودود ومصري أصيل.
المنتجات المتاحة: {productsDescription}
أجب باختصار وذكاء.";
        }

        // 3. CALL GEMINI API
        // Endpoint: https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=YOUR_API_KEY
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = systemPrompt + "\nUser Message: " + userMessage } } }
            },
            generationConfig = new
            {
                temperature = 0.8,
                maxOutputTokens = 500
            }
        };

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return "عذراً، واجهت مشكلة في التواصل مع خبير التسوق. يرجى المحاولة لاحقاً.";
            }

            var result = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(result);
            var aiText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return aiText ?? "لم أستطع فهم ذلك، هل يمكنك توضيح سؤالك؟";
        }
        catch (Exception)
        {
            return "عذراً، حدث خطأ غير متوقع. جرب مرة أخرى.";
        }
    }

    private async Task<dynamic> GetExtendedAdminStatsAsync()
    {
        var egyptTime = Utils.TimeHelper.GetEgyptTime();
        var today = egyptTime.Date;
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);

        var sales = await _db.Orders.Where(o => o.CreatedAt.Date == today && o.Status != Models.OrderStatus.Cancelled).SumAsync(o => o.TotalAmount);
        var monthSales = await _db.Orders.Where(o => o.CreatedAt >= firstOfMonth && o.Status != Models.OrderStatus.Cancelled).SumAsync(o => o.TotalAmount);
        var orders = await _db.Orders.CountAsync(o => o.CreatedAt.Date == today);
        var lowStock = await _db.Products.CountAsync(p => p.TotalStock <= p.ReorderLevel && p.Status == Models.ProductStatus.Active);
        var custCount = await _db.Customers.CountAsync();
        var inventoryVal = await _db.Products.SumAsync(p => p.TotalStock * p.CostPrice);
        var coupons = await _db.Coupons.Where(c => c.IsActive && (!c.ExpiresAt.HasValue || c.ExpiresAt >= egyptTime)).Select(c => c.Code).ToListAsync();

        return new { 
            TodaySales = sales, 
            MonthSales = monthSales,
            OrderCount = orders, 
            LowStockCount = lowStock, 
            CustomerCount = custCount,
            TotalInventoryValue = inventoryVal,
            ActiveCoupons = string.Join(", ", coupons)
        };
    }
}
