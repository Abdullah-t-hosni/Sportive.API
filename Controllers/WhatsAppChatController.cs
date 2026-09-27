using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/whatsapp/chats")]
[Authorize]
public class WhatsAppChatController : ControllerBase
{
    private readonly AppDbContext _db;

    public WhatsAppChatController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllConversations([FromQuery] string? search = null)
    {
        var messagesQuery = _db.WhatsAppMessages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            messagesQuery = messagesQuery.Where(m => m.Phone.Contains(s) || (m.CustomerName != null && m.CustomerName.ToLower().Contains(s)) || (m.Text != null && m.Text.ToLower().Contains(s)));
        }

        var recentMsgs = await messagesQuery
            .OrderByDescending(m => m.Timestamp)
            .Take(1500)
            .ToListAsync();

        var grouped = recentMsgs
            .GroupBy(m => {
                var p = m.Phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();
                if (p.StartsWith("20") && p.Length == 12) return "0" + p.Substring(2);
                if (p.StartsWith("201") && p.Length == 12) return "0" + p.Substring(2);
                return p;
            })
            .Select(g => {
                var latest = g.OrderByDescending(m => m.Timestamp).First();
                var customerName = g.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.CustomerName) && !m.CustomerName.Contains("Store") && !m.CustomerName.Contains("المتجر"))?.CustomerName 
                                   ?? latest.CustomerName 
                                   ?? g.Key;
                return new
                {
                    phone = g.Key,
                    customerName = customerName,
                    lastMessage = new
                    {
                        id = latest.Id.ToString(),
                        text = latest.Text,
                        fromMe = latest.FromMe,
                        timestamp = latest.Timestamp,
                        mediaType = latest.MediaType,
                        mediaUrl = latest.MediaUrl
                    },
                    unreadCount = g.Count(m => !m.FromMe),
                    totalMessages = g.Count(),
                    lastActivity = latest.Timestamp
                };
            })
            .OrderByDescending(x => x.lastActivity)
            .ToList();

        var phones = grouped.Select(x => x.phone).Distinct().ToList();
        var searchHashes = phones.Select(p => Sportive.API.Models.Customer.EncryptionHelper?.ComputeSearchHash(p) ?? p).ToList();

        var matchedCustomers = await _db.Customers
            .AsNoTracking()
            .Include(c => c.Orders)
            .Include(c => c.Addresses)
            .Where(c => searchHashes.Contains(c.PhoneHash) || (c.Phone != null && phones.Contains(c.Phone)))
            .ToListAsync();

        var result = grouped.Select(g => {
            var phoneHash = Sportive.API.Models.Customer.EncryptionHelper?.ComputeSearchHash(g.phone) ?? g.phone;
            var cust = matchedCustomers.FirstOrDefault(c => c.PhoneHash == phoneHash || c.Phone == g.phone);

            int ordersCount = cust?.Orders?.Count ?? 0;
            decimal totalSpent = cust?.Orders?.Where(o => o.Status != Sportive.API.Models.OrderStatus.Cancelled).Sum(o => o.TotalAmount) ?? 0;

            return new
            {
                phone = g.phone,
                customerName = !string.IsNullOrWhiteSpace(cust?.FullName) ? cust.FullName : g.customerName,
                customerId = cust?.Id,
                ordersCount = ordersCount,
                totalSpent = totalSpent,
                lastMessage = g.lastMessage,
                unreadCount = g.unreadCount,
                totalMessages = g.totalMessages,
                lastActivity = g.lastActivity,
                city = cust?.Addresses?.FirstOrDefault()?.City
            };
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{phone}/customer-context")]
    public async Task<IActionResult> GetCustomerContext(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest("Phone is required");

        var raw = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();
        string variantLocal = (raw.StartsWith("20") && raw.Length == 12) ? "0" + raw.Substring(2) : raw;
        var last9 = raw.Length >= 9 ? raw.Substring(raw.Length - 9) : raw;
        var phoneHash = Sportive.API.Models.Customer.EncryptionHelper?.ComputeSearchHash(variantLocal) ?? variantLocal;

        var cust = await _db.Customers
            .AsNoTracking()
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
                .ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(c => c.PhoneHash == phoneHash || (c.Phone != null && c.Phone.EndsWith(last9)));

        if (cust == null)
        {
            return Ok(new
            {
                found = false,
                phone = variantLocal,
                orders = new object[] {}
            });
        }

        var orders = cust.Orders
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                status = o.Status.ToString(),
                fulfillmentType = o.FulfillmentType.ToString(),
                paymentMethod = o.PaymentMethod.ToString(),
                totalAmount = o.TotalAmount,
                createdAt = o.CreatedAt,
                itemCount = o.Items.Sum(i => i.Quantity),
                items = o.Items.Select(i => new
                {
                    productName = i.ProductNameAr,
                    quantity = i.Quantity,
                    size = i.Size,
                    color = i.Color,
                    totalPrice = i.TotalPrice
                }).ToList()
            }).ToList();

        return Ok(new
        {
            found = true,
            id = cust.Id,
            fullName = cust.FullName,
            phone = cust.Phone ?? variantLocal,
            email = cust.Email,
            city = cust.Addresses.FirstOrDefault()?.City,
            address = cust.Addresses.FirstOrDefault() != null ? $"{cust.Addresses.First().City} - {cust.Addresses.First().Street}" : null,
            notes = cust.Notes,
            ordersCount = cust.Orders.Count,
            totalSpent = cust.Orders.Where(o => o.Status != Sportive.API.Models.OrderStatus.Cancelled).Sum(o => o.TotalAmount),
            recentOrders = orders
        });
    }

    [HttpGet("{phone}")]
    public async Task<IActionResult> GetChatHistory(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest("Phone is required");

        // Normalize phone — build both variants to search DB regardless of how it was stored
        var raw = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();

        // Variant A: Egyptian local format → 01XXXXXXXXX (11 digits)
        string variantLocal;
        if (raw.StartsWith("20") && raw.Length == 12)
            variantLocal = "0" + raw.Substring(2);
        else if (raw.StartsWith("0") && raw.Length == 11)
            variantLocal = raw;
        else
            variantLocal = raw;

        // Variant B: International format → 201XXXXXXXXX (12 digits)
        string variantIntl;
        if (variantLocal.StartsWith("0") && variantLocal.Length == 11)
            variantIntl = "2" + variantLocal;
        else if (raw.StartsWith("20") && raw.Length == 12)
            variantIntl = raw;
        else
            variantIntl = "20" + variantLocal.TrimStart('0');

        var last9 = raw.Length >= 9 ? raw.Substring(raw.Length - 9) : raw;

        // Lookup customer name to match any orphan messages saved with customer name
        string? targetCustName = null;
        var phoneHash = Sportive.API.Models.Customer.EncryptionHelper?.ComputeSearchHash(variantLocal) ?? variantLocal;
        
        // Fetch matching customer into memory first, then check EndsWith to avoid LINQ translation error
        var custs = await _db.Customers.Where(c => c.PhoneHash == phoneHash).ToListAsync();
        var cust = custs.FirstOrDefault() ?? await _db.Customers.ToListAsync().ContinueWith(t => t.Result.FirstOrDefault(c => last9.Length >= 8 && !string.IsNullOrEmpty(c.Phone) && c.Phone.EndsWith(last9)));
        
        if (cust != null && !string.IsNullOrWhiteSpace(cust.FullName))
        {
            targetCustName = cust.FullName.Trim();
        }

        var messages = await _db.WhatsAppMessages
            .Where(m => m.Phone == variantLocal || m.Phone == variantIntl || m.Phone == raw || (last9.Length >= 8 && m.Phone.EndsWith(last9)) || (!string.IsNullOrEmpty(targetCustName) && m.CustomerName == targetCustName))
            .OrderByDescending(m => m.Timestamp)
            .Take(200)
            .ToListAsync();

        // Remove duplicates by message ID
        var distinctMessages = messages
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .OrderBy(m => m.Timestamp)
            .ToList();

        return Ok(new
        {
            phone = variantLocal,
            connected = true, // To satisfy frontend expectations
            count = distinctMessages.Count,
            messages = distinctMessages.Select(m => new
            {
                id = m.Id.ToString(),
                fromMe = m.FromMe,
                text = m.Text,
                timestamp = m.Timestamp,
                pushName = m.CustomerName ?? (m.FromMe ? "Store" : "Customer"),
                senderName = m.CustomerName ?? (m.FromMe ? "Store" : "Customer"),
                mediaUrl = m.MediaUrl,
                mediaType = m.MediaType,
                fileName = m.FileName
            })
        });
    }
}
