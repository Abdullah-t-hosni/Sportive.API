using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Utils;
using Serilog;

namespace Sportive.API.Services;

public class ChatPart
{
    public string? Text { get; set; }
    public ChatFunctionCall? FunctionCall { get; set; }
    public ChatFunctionResponse? FunctionResponse { get; set; }
}

public class ChatFunctionCall
{
    public string Name { get; set; } = string.Empty;
    public JsonElement? Args { get; set; }
    public string? Id { get; set; }
}

public class ChatFunctionResponse
{
    public string Name { get; set; } = string.Empty;
    public object Response { get; set; } = null!;
    public string? Id { get; set; }
}

public class ChatTurn
{
    public string Role { get; set; } = string.Empty;
    public List<ChatPart> Parts { get; set; } = new();
}

public class AiAssistantService : IAiAssistantService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly EncryptionHelper _encryptionHelper;

    public AiAssistantService(AppDbContext db, IConfiguration config, IHttpClientFactory factory, EncryptionHelper encryptionHelper)
    {
        _db = db;
        _config = config;
        _http = factory.CreateClient();
        _encryptionHelper = encryptionHelper;
    }

    public async Task<string> ChatAsync(string userMessage, string? conversationId = null, bool isAdmin = false)
    {
        // 🛡️ Guard: prevent prompt injection and token cost explosion
        if (string.IsNullOrWhiteSpace(userMessage))
            return "عذراً، لم تصلني رسالة.";
        if (userMessage.Length > 1000)
            userMessage = userMessage.Substring(0, 1000); // hard cap ~250 tokens

        var apiKey = _config["AI:GeminiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey == "${GEMINI_KEY}")
        {
            apiKey = Environment.GetEnvironmentVariable("GEMINI_KEY") ?? Environment.GetEnvironmentVariable("AI_GEMINI_KEY");
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "${GEMINI_KEY}")
        {
            return "عذراً، لم يتم ضبط مفتاح الذكاء الاصطناعي (Gemini API Key) بعد. يرجى التواصل مع الدعم الفني.";
        }

        string systemPrompt;
        
        if (isAdmin)
        {
            // --- DEEP ADMIN CONTEXT ---
            var stats = await GetExtendedAdminStatsAsync();
            var egyptTime = Utils.TimeHelper.GetEgyptTime();
            systemPrompt = $@"أنت 'اللهو الخفي'، الروح الحارسة والذكاء الخارق لمتجر 'Sportive'.
أنت تعرف كل صغيرة وكبيرة في النظام، من أصغر مسمار في المخزن وحتى أكبر عملية بيع.
تتحدث مع 'المدير' بلهجة مصرية حكيمة، غامضة قليلاً، لكنها دقيقة جداً ومحترفة.

حالة النظام اليوم بالتفصيل ({egyptTime:yyyy-MM-dd HH:mm}):
- إجمالي المبيعات (الخام): {stats.TodaySales} ج.م (من {stats.OrderCount} فاتورة/طلب)
- صافي الإيرادات اليومية (بدون الضريبة): {stats.TodayNetRevenue} ج.م
- إجمالي التكلفة للبضاعة المباعة اليوم: {stats.TodayCost} ج.م
- إجمالي الربح اليومي الفعلي: {stats.TodayProfit} ج.م (بمتوسط هامش ربح {stats.TodayMargin:F1}%)
- طرق الدفع لمبيعات اليوم:
  * نقدي (كاش): {stats.CashSales} ج.م
  * فيزا / بنك: {stats.VisaSales} ج.م
  * إنستا باي (InstaPay): {stats.InstapaySales} ج.م
  * فودافون كاش: {stats.VodafoneSales} ج.م
  * طرق أخرى: {stats.OtherSales} ج.م

إحصائيات إجمالية وعامة:
- مبيعات هذا الشهر (حسب يوم العمل الفعلي): {stats.MonthSales} ج.م
- إجمالي قيمة البضاعة الحالية في المخازن بسعر التكلفة: {stats.TotalInventoryValue} ج.م
- عدد العملاء المسجلين: {stats.CustomerCount} عميل
- المنتجات الناقصة (تحت حد الطلب): {stats.LowStockCount} منتج
- الكوبونات الفعالة حالياً: {stats.ActiveCoupons}

مهامك:
1. تقديم تقارير فورية وتحليلية دقيقة جداً ومطابقة للأرقام المذكورة أعلاه.
2. عند السؤال عن المبيعات أو الأرباح أو الكاش والفيزا، استخدم الأرقام المحددة اليوم أعلاه ولا تقم باختراع أو تخمين أرقام أخرى.
3. التنبيه للمشاكل (مثل نقص بضاعة معينة أو انخفاض المبيعات).
4. اقتراح خطط تسويقية بناءً على حالة المخازن.
5. استخدام الدوال (Tools) المتاحة لك للبحث عن الفواتير أو المنتجات أو العملاء والإجابة على أي أسئلة يطرحها المدير بالتفصيل.
6. عند عرض روابط الفواتير أو المنتجات أو العملاء، استخدم التنسيق المرجعي الذي ترجعه لك الدوال (مثلاً [رقم الفاتورة](/admin/orders?viewId=الأيدي) أو [تعديل المنتج](/admin/products/edit/الأيدي) أو [تعديل العميل](/admin/customers/edit/الأيدي)) لكي يتمكن المدير من فتحها مباشرة في لوحة التحكم. لا تقم باختراع الروابط بنفسك بل استخدم الروابط المرتجعة من الدوال.
تذكر: أنت 'اللهو الخفي'، دائماً موجود ودائماً تعلم.
قاعدة صارمة: أجب باختصار شديد وبشكل مباشر جداً. لا تكرر سؤال المستخدم، ولا تكتب مقدمات أو ترحيب طويل، ولا تكتب استنتاجات طويلة. أعطِ الخلاصة أو الأرقام المطلوبة في سطر أو سطرين على الأكثر إلا إذا طُلب منك التفصيل.";
        }
        else
        {
            // --- CUSTOMER CONTEXT ---
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => p.Status == Models.ProductStatus.Active)
                .OrderByDescending(p => p.IsFeatured)
                .Take(25)
                .Select(p => new { p.NameAr, p.Price, CategoryName = p.Category != null ? p.Category.NameAr : "عام" })
                .ToListAsync();

            var productsDescription = string.Join(", ", products.Select(p => $"{p.NameAr} ({p.Price} ج.م)"));
            
            systemPrompt = $@"أنت 'اللهو الخفي'، المساعد الذكي لمتجر Sportive.
تحب مساعدة العملاء في اختيار أفضل الملابس الرياضية بأسلوب ودود ومصري أصيل.
المنتجات المتاحة: {productsDescription}
أجب باختصار شديد جداً ومباشر، لا تكرر كلام المستخدم ولا تستخدم مقدمات أو خاتمات طويلة. سطر أو سطرين يكفي.";
        }

        // Tools (Function declarations) definition
        var tools = new object[]
        {
            new
            {
                FunctionDeclarations = new object[]
                {
                    new
                    {
                        Name = "searchInvoices",
                        Description = "البحث في الفواتير والطلبات باستخدام كلمة بحث (مثل رقم الفاتورة أو اسم العميل)، وتاريخ اختياري. ترجع قائمة الفواتير مع أرقامها وقيمتها وحالتها وروابطها.",
                        Parameters = new
                        {
                            Type = "object",
                            Properties = new
                            {
                                Query = new { Type = "string", Description = "كلمة البحث أو اسم العميل أو رقم الفاتورة" },
                                StartDate = new { Type = "string", Description = "تاريخ البدء بتنسيق YYYY-MM-DD" },
                                EndDate = new { Type = "string", Description = "تاريخ الانتهاء بتنسيق YYYY-MM-DD" }
                            }
                        }
                    },
                    new
                    {
                        Name = "getInvoiceDetails",
                        Description = "الحصول على تفاصيل فاتورة معينة بالتفصيل (المنتجات، الأسعار، التكاليف، طريقة الدفع وحالة الدفع).",
                        Parameters = new
                        {
                            Type = "object",
                            Properties = new
                            {
                                OrderNumber = new { Type = "string", Description = "رقم الفاتورة (مثلاً POS-2606-0593 أو SZ-20240001)" },
                                OrderId = new { Type = "integer", Description = "معرف الفاتورة الرقمي إن وجد" }
                            }
                        }
                    },
                    new
                    {
                        Name = "searchProducts",
                        Description = "البحث في كتالوج المنتجات باستخدام الاسم أو الـ SKU أو الماركة. ترجع المنتجات مع أسعارها وتكاليفها والمخزون الحالي.",
                        Parameters = new
                        {
                            Type = "object",
                            Properties = new
                            {
                                Query = new { Type = "string", Description = "اسم المنتج أو الـ SKU أو كلمة بحث" }
                            },
                            Required = new[] { "query" }
                        }
                    },
                    new
                    {
                        Name = "searchCustomers",
                        Description = "البحث عن عميل أو عملاء باستخدام الاسم أو رقم الهاتف أو البريد الإلكتروني. ترجع تفاصيل العميل، مبيعاته، ومديونيته.",
                        Parameters = new
                        {
                            Type = "object",
                            Properties = new
                            {
                                Query = new { Type = "string", Description = "اسم العميل أو جزء منه أو رقم الهاتف" }
                            },
                            Required = new[] { "query" }
                        }
                    }
                }
            }
        };

        // 3. CALL GEMINI API (v1 beta stable)
        _http.DefaultRequestHeaders.Remove("x-goog-api-key");
        _http.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
        var url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent";

        int maxTurns = 3;
        var turns = new List<ChatTurn>
        {
            new ChatTurn
            {
                Role = "user",
                Parts = new List<ChatPart> { new ChatPart { Text = systemPrompt + "\n--- User Inquiry ---\n" + userMessage } }
            }
        };

        try
        {
            for (int turn = 0; turn < maxTurns; turn++)
            {
                var requestBody = new
                {
                    Contents = turns,
                    Tools = isAdmin ? tools : null
                };

                var jsonBody = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                
                var response = await _http.PostAsync(url, content);
                var result = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("AiAssistant Error: {Result}", result);
                    if (result.Contains("API_KEY_INVALID") || result.Contains("API key not valid"))
                    {
                        return "⚠️ عذراً، مفتاح الذكاء الاصطناعي (Gemini API Key) غير صالح. يرجى إدخال مفتاح API صحيح يبدأ بـ 'AIzaSy' أو 'AQ.' من Google AI Studio لكي يتحدث معك المساعد الذكي ويحلل مبيعاتك.";
                    }
                    return $"عذراً، واجهت مشكلة في التواصل مع خبير التسوق. الخطأ من السيرفر: {result}";
                }

                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                var candidates = root.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0)
                {
                    return "لم أستطع فهم ذلك، هل يمكنك توضيح سؤالك؟";
                }

                var firstCandidate = candidates[0];
                var candidateContent = firstCandidate.GetProperty("content");
                var candidateParts = candidateContent.GetProperty("parts");
                if (candidateParts.GetArrayLength() == 0)
                {
                    return "لم أستطع فهم ذلك، هل يمكنك توضيح سؤالك؟";
                }

                var modelPart = candidateParts[0];

                // Check for functionCall
                if (modelPart.TryGetProperty("functionCall", out var functionCall) || modelPart.TryGetProperty("function_call", out functionCall))
                {
                    var functionName = functionCall.GetProperty("name").GetString();
                    
                    string? callId = null;
                    if (functionCall.TryGetProperty("id", out var idProp))
                    {
                        callId = idProp.GetString();
                    }

                    var args = functionCall.TryGetProperty("args", out var argsProp) ? argsProp : (JsonElement?)null;

                    // Execute function locally
                    object functionResult;
                    try
                    {
                        functionResult = await ExecuteToolAsync(functionName, args);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to execute function {FunctionName}", functionName);
                        functionResult = new { error = $"فشل تنفيذ الاستعلام: {ex.Message}" };
                    }

                    // Append the model's tool request turn to history
                    var modelTurn = new ChatTurn
                    {
                        Role = "model",
                        Parts = new List<ChatPart>
                        {
                            new ChatPart
                            {
                                FunctionCall = new ChatFunctionCall
                                {
                                    Name = functionName ?? "",
                                    Args = args,
                                    Id = callId
                                }
                            }
                        }
                    };
                    turns.Add(modelTurn);

                    // Append the function result response turn to history
                    var responseTurn = new ChatTurn
                    {
                        Role = "function",
                        Parts = new List<ChatPart>
                        {
                            new ChatPart
                            {
                                FunctionResponse = new ChatFunctionResponse
                                {
                                    Name = functionName ?? "",
                                    Response = functionResult,
                                    Id = callId
                                }
                            }
                        }
                    };
                    turns.Add(responseTurn);

                    // Proceed to next turn of loop
                    continue;
                }

                // Check for text response
                if (modelPart.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString() ?? "لم أستطع فهم ذلك، هل يمكنك توضيح سؤالك؟";
                }

                break;
            }

            return "لم أستطع فهم ذلك، هل يمكنك توضيح سؤالك؟";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AiAssistant unexpected failure");
            return "عذراً، حدث خطأ غير متوقع. جرب مرة أخرى.";
        }
    }

    private async Task<object> ExecuteToolAsync(string? functionName, JsonElement? args)
    {
        if (string.IsNullOrEmpty(functionName))
            return new { error = "اسم الدالة فارغ" };

        switch (functionName)
        {
            case "searchInvoices":
                {
                    string? query = null;
                    string? startDate = null;
                    string? endDate = null;

                    if (args.HasValue)
                    {
                        if (args.Value.TryGetProperty("query", out var pQuery)) query = pQuery.GetString();
                        if (args.Value.TryGetProperty("startDate", out var pStart)) startDate = pStart.GetString();
                        if (args.Value.TryGetProperty("endDate", out var pEnd)) endDate = pEnd.GetString();
                    }

                    var q = _db.Orders.AsNoTracking().Include(o => o.Customer).AsQueryable();

                    if (!string.IsNullOrEmpty(query))
                    {
                        var qLower = query.ToLower();
                        q = q.Where(o => o.OrderNumber.ToLower().Contains(qLower) || 
                                         (o.Customer != null && o.Customer.FullName.ToLower().Contains(qLower)));
                    }

                    if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var start))
                    {
                        q = q.Where(o => o.CreatedAt >= start);
                    }

                    if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var end))
                    {
                        var endLimit = end.Date.AddDays(1);
                        q = q.Where(o => o.CreatedAt < endLimit);
                    }

                    var orders = await q
                        .OrderByDescending(o => o.CreatedAt)
                        .Take(15)
                        .Select(o => new {
                            o.Id,
                            o.OrderNumber,
                            CustomerName = o.Customer != null ? o.Customer.FullName : "زبون عابر",
                            o.TotalAmount,
                            o.PaidAmount,
                            Status = o.Status.ToString(),
                            PaymentStatus = o.PaymentStatus.ToString(),
                            PaymentMethod = o.PaymentMethod.ToString(),
                            o.CreatedAt,
                            DetailsLink = $"[تفاصيل الفاتورة {o.OrderNumber}](/admin/orders?viewId={o.Id})"
                        })
                        .ToListAsync();

                    return orders;
                }

            case "getInvoiceDetails":
                {
                    string? orderNumber = null;
                    int? orderId = null;

                    if (args.HasValue)
                    {
                        if (args.Value.TryGetProperty("orderNumber", out var pNum)) orderNumber = pNum.GetString();
                        if (args.Value.TryGetProperty("orderId", out var pId) && pId.ValueKind == JsonValueKind.Number) orderId = pId.GetInt32();
                    }

                    if (!orderId.HasValue && string.IsNullOrEmpty(orderNumber))
                    {
                        return new { error = "يجب تحديد رقم الفاتورة أو المعرف الرقمي." };
                    }

                    var query = _db.Orders.AsNoTracking()
                        .Include(o => o.Customer)
                        .Include(o => o.Items)
                            .ThenInclude(i => i.Product)
                        .AsQueryable();

                    if (orderId.HasValue)
                    {
                        query = query.Where(o => o.Id == orderId.Value);
                    }
                    else if (!string.IsNullOrEmpty(orderNumber))
                    {
                        query = query.Where(o => o.OrderNumber == orderNumber);
                    }

                    var order = await query.FirstOrDefaultAsync();

                    if (order == null)
                    {
                        return new { error = "لم يتم العثور على الفاتورة المطلوبة." };
                    }

                    return new {
                        order.Id,
                        order.OrderNumber,
                        CustomerName = order.Customer != null ? order.Customer.FullName : "زبون عابر",
                        Status = order.Status.ToString(),
                        FulfillmentType = order.FulfillmentType.ToString(),
                        PaymentMethod = order.PaymentMethod.ToString(),
                        PaymentStatus = order.PaymentStatus.ToString(),
                        order.SubTotal,
                        order.DiscountAmount,
                        order.DeliveryFee,
                        order.TotalAmount,
                        order.PaidAmount,
                        order.RemainingAmount,
                        Items = order.Items.Select(i => new {
                            ProductId = i.ProductId,
                            ProductName = i.ProductNameAr,
                            SKU = i.SKU,
                            i.Size,
                            i.Color,
                            i.Quantity,
                            i.UnitPrice,
                            i.TotalPrice,
                            CostPrice = i.Product != null ? i.Product.CostPrice : 0
                        }).ToList(),
                        order.CreatedAt,
                        DetailsLink = $"[عرض وتعديل الفاتورة {order.OrderNumber}](/admin/orders?viewId={order.Id})"
                    };
                }

            case "searchProducts":
                {
                    string? query = null;

                    if (args.HasValue && args.Value.TryGetProperty("query", out var pQuery))
                    {
                        query = pQuery.GetString();
                    }

                    if (string.IsNullOrEmpty(query))
                    {
                        return new { error = "يجب إدخال كلمة بحث للمنتجات." };
                    }

                    var qLower = query.ToLower();
                    var products = await _db.Products.AsNoTracking()
                        .Include(p => p.Category)
                        .Include(p => p.Variants)
                        .Where(p => p.NameAr.ToLower().Contains(qLower) || 
                                    p.NameEn.ToLower().Contains(qLower) || 
                                    p.SKU.ToLower() == qLower ||
                                    p.Variants.Any(v => v.Size.ToLower() == qLower || v.Color.ToLower() == qLower))
                        .Take(15)
                        .Select(p => new {
                            p.Id,
                            p.NameAr,
                            p.SKU,
                            p.Price,
                            p.CostPrice,
                            p.TotalStock,
                            Status = p.Status.ToString(),
                            CategoryName = p.Category != null ? p.Category.NameAr : "عام",
                            Variants = p.Variants.Select(v => new {
                                v.Id,
                                v.Size,
                                v.Color,
                                v.StockQuantity
                            }).ToList(),
                            EditLink = $"[تعديل المنتج {p.NameAr}](/admin/products/edit/{p.Id})"
                        })
                        .ToListAsync();

                    return products;
                }

            case "searchCustomers":
                {
                    string? query = null;

                    if (args.HasValue && args.Value.TryGetProperty("query", out var pQuery))
                    {
                        query = pQuery.GetString();
                    }

                    if (string.IsNullOrEmpty(query))
                    {
                        return new { error = "يجب تحديد كلمة بحث للعميل." };
                    }

                    var qLower = query.ToLower();
                    var searchHash = _encryptionHelper.ComputeSearchHash(query);
                    
                    var customers = await _db.Customers.AsNoTracking()
                        .Where(c => c.FullName.ToLower().Contains(qLower) ||
                                    c.EmailHash == searchHash ||
                                    c.PhoneHash == searchHash)
                        .Take(15)
                        .Select(c => new {
                            c.Id,
                            c.FullName,
                            c.EmailEncrypted,
                            c.PhoneEncrypted,
                            c.TotalSales,
                            c.TotalPaid,
                            c.FixedDiscount,
                            c.IsActive,
                            c.CreatedAt,
                            EditLink = $"[تعديل العميل {c.FullName}](/admin/customers/edit/{c.Id})"
                        })
                        .ToListAsync();

                    var decryptedCustomers = customers.Select(c => new {
                        c.Id,
                        c.FullName,
                        Email = string.IsNullOrEmpty(c.EmailEncrypted) ? "" : _encryptionHelper.Decrypt(c.EmailEncrypted),
                        Phone = string.IsNullOrEmpty(c.PhoneEncrypted) ? "" : _encryptionHelper.Decrypt(c.PhoneEncrypted),
                        c.TotalSales,
                        c.TotalPaid,
                        Balance = c.TotalSales - c.TotalPaid,
                        c.FixedDiscount,
                        c.IsActive,
                        c.CreatedAt,
                        c.EditLink
                    }).ToList();

                    return decryptedCustomers;
                }

            default:
                return new { error = "دالة غير مدعومة" };
        }
    }



    private async Task<dynamic> GetExtendedAdminStatsAsync()
    {
        var now = Utils.TimeHelper.GetEgyptTime();
        var endHour = Utils.TimeHelper.GetBusinessDayEndHour();
        
        // Today's business day boundaries (2:00 AM cutoff)
        var todayStart = (now.Hour < endHour) 
            ? now.Date.AddDays(-1).AddHours(endHour) 
            : now.Date.AddHours(endHour);
        var todayEnd = todayStart.AddDays(1);
        
        var firstOfMonth = new DateTime(now.Year, now.Month, 1).AddHours(endHour);

        // Fetch Today's Orders (Including OrderItems and Products for Profit/Cost calculations)
        var todayOrders = await _db.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Where(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd && o.Status != Models.OrderStatus.Cancelled)
            .ToListAsync();

        // 1. Today Sales (Gross and Net)
        decimal todayGrossSales = todayOrders.Sum(o => o.TotalAmount);
        decimal todayNetRevenue = todayOrders.Sum(o => o.TotalAmount - o.TotalVatAmount);
        
        // 2. Today Cost & Profit
        decimal todayCost = 0;
        foreach (var order in todayOrders)
        {
            foreach (var item in order.Items)
            {
                todayCost += (item.Product?.CostPrice ?? 0) * item.Quantity;
            }
        }
        decimal todayProfit = todayNetRevenue - todayCost;
        decimal todayMargin = todayNetRevenue > 0 ? (todayProfit / todayNetRevenue) * 100 : 0;

        // 3. Payment Methods Breakdown
        decimal cashSales = todayOrders.Where(o => o.PaymentMethod == Models.PaymentMethod.Cash).Sum(o => o.TotalAmount);
        decimal visaSales = todayOrders.Where(o => o.PaymentMethod == Models.PaymentMethod.CreditCard || o.PaymentMethod == Models.PaymentMethod.Bank).Sum(o => o.TotalAmount);
        decimal instapaySales = todayOrders.Where(o => o.PaymentMethod == Models.PaymentMethod.InstaPay).Sum(o => o.TotalAmount);
        decimal vodafoneSales = todayOrders.Where(o => o.PaymentMethod == Models.PaymentMethod.Vodafone).Sum(o => o.TotalAmount);
        decimal otherSales = todayOrders.Where(o => o.PaymentMethod != Models.PaymentMethod.Cash && 
                                                    o.PaymentMethod != Models.PaymentMethod.CreditCard && 
                                                    o.PaymentMethod != Models.PaymentMethod.Bank && 
                                                    o.PaymentMethod != Models.PaymentMethod.InstaPay && 
                                                    o.PaymentMethod != Models.PaymentMethod.Vodafone).Sum(o => o.TotalAmount);

        // 4. Month Sales (align to business day start of month)
        decimal monthSales = await _db.Orders
            .Where(o => o.CreatedAt >= firstOfMonth && o.Status != Models.OrderStatus.Cancelled)
            .SumAsync(o => o.TotalAmount);

        var lowStock = await _db.Products.CountAsync(p => p.TotalStock <= p.ReorderLevel && p.Status == Models.ProductStatus.Active);
        var custCount = await _db.Customers.CountAsync();
        var inventoryVal = await _db.Products.SumAsync(p => (decimal?)p.TotalStock * p.CostPrice) ?? 0;
        var coupons = await _db.Coupons.Where(c =>
            c.IsActive && (!c.ExpiresAt.HasValue || c.ExpiresAt >= now)).CountAsync();

        return new { 
            TodaySales = todayGrossSales,
            TodayNetRevenue = todayNetRevenue,
            TodayCost = todayCost,
            TodayProfit = todayProfit,
            TodayMargin = todayMargin,
            OrderCount = todayOrders.Count,
            CashSales = cashSales,
            VisaSales = visaSales,
            InstapaySales = instapaySales,
            VodafoneSales = vodafoneSales,
            OtherSales = otherSales,
            MonthSales = monthSales,
            LowStockCount = lowStock, 
            CustomerCount = custCount,
            TotalInventoryValue = inventoryVal,
            ActiveCoupons = coupons
        };
    }
}
