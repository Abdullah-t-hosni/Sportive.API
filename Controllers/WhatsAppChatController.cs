using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

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

    private static (List<string> phoneHashes, List<string> emailHashes, string localPhone, string intlPhone) GenerateSearchHashes(string? phone)
    {
        var phoneHashes = new List<string>();
        var emailHashes = new List<string>();
        if (string.IsNullOrWhiteSpace(phone))
            return (phoneHashes, emailHashes, "", "");

        var digits = Regex.Replace(phone, @"\D", "").Trim();
        if (string.IsNullOrEmpty(digits))
            return (phoneHashes, emailHashes, phone.Trim(), phone.Trim());

        // Local Egyptian: 01XXXXXXXXX (11 digits)
        string local = digits;
        if (digits.StartsWith("20") && digits.Length == 12)
            local = "0" + digits.Substring(2);
        else if (digits.StartsWith("0020") && digits.Length == 14)
            local = "0" + digits.Substring(4);
        else if (!digits.StartsWith("0") && digits.Length == 10)
            local = "0" + digits;

        // International: 201XXXXXXXXX
        string intl = local.StartsWith("0") ? "2" + local : "20" + local;
        // Bare 10 digits without leading zero
        string bare = local.StartsWith("0") ? local.Substring(1) : local;

        var variants = new[] { local, intl, bare, digits, phone.Trim() }.Distinct();
        foreach (var v in variants)
        {
            var pHash = Customer.EncryptionHelper?.ComputeSearchHash(v);
            if (!string.IsNullOrEmpty(pHash) && !phoneHashes.Contains(pHash))
                phoneHashes.Add(pHash);

            var eHash = Customer.EncryptionHelper?.ComputeSearchHash($"{v}@sportive.com");
            if (!string.IsNullOrEmpty(eHash) && !emailHashes.Contains(eHash))
                emailHashes.Add(eHash);
        }

        return (phoneHashes, emailHashes, local, intl);
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
            .Take(2000)
            .ToListAsync();

        var grouped = recentMsgs
            .GroupBy(m => {
                var p = Regex.Replace(m.Phone ?? "", @"\D", "").Trim();
                if (p.StartsWith("20") && p.Length == 12) return "0" + p.Substring(2);
                if (p.StartsWith("0020") && p.Length == 14) return "0" + p.Substring(4);
                return p;
            })
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => {
                var latest = g.OrderByDescending(m => m.Timestamp).First();
                var customerName = g.FirstOrDefault(m => !m.FromMe && !string.IsNullOrWhiteSpace(m.CustomerName) && !m.CustomerName.Contains("Store") && !m.CustomerName.Contains("المتجر"))?.CustomerName
                                   ?? (!latest.FromMe ? latest.CustomerName : null)
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

        // Collect all candidate hashes for all phones in batch
        var allHashes = new List<string>();
        var allEmailHashes = new List<string>();
        foreach (var item in grouped)
        {
            var (pHashes, eHashes, _, _) = GenerateSearchHashes(item.phone);
            allHashes.AddRange(pHashes);
            allEmailHashes.AddRange(eHashes);
        }
        allHashes = allHashes.Distinct().ToList();
        allEmailHashes = allEmailHashes.Distinct().ToList();

        var matchedCustomers = await _db.Customers
            .AsNoTracking()
            .Include(c => c.Orders)
            .Include(c => c.Addresses)
            .Where(c => allHashes.Contains(c.PhoneHash) || allEmailHashes.Contains(c.EmailHash))
            .ToListAsync();

        var result = grouped.Select(g => {
            var (pHashes, eHashes, _, _) = GenerateSearchHashes(g.phone);
            var cust = matchedCustomers.FirstOrDefault(c => pHashes.Contains(c.PhoneHash) || eHashes.Contains(c.EmailHash));

            int ordersCount = cust?.Orders?.Count ?? 0;
            decimal totalSpent = cust?.Orders?.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount) ?? 0;

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

        var (phoneHashes, emailHashes, localPhone, _) = GenerateSearchHashes(phone);

        // 1. Primary lookup by indexed hashes in MySQL (Fast & 100% reliable)
        var cust = await _db.Customers
            .AsNoTracking()
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
                .ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(c => phoneHashes.Contains(c.PhoneHash) || emailHashes.Contains(c.EmailHash));

        // 2. Fallback: Search Orders by customer notes or matching recent orders
        if (cust == null)
        {
            var raw = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();
            var recentMsg = await _db.WhatsAppMessages
                .AsNoTracking()
                .Where(m => m.Phone.Contains(localPhone) || m.Phone.Contains(raw))
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefaultAsync(m => m.Text != null && (m.Text.Contains("SPT-") || m.Text.Contains("POS-")));

            if (recentMsg?.Text != null)
            {
                var match = Regex.Match(recentMsg.Text, @"(SPT|POS|ORD)-\d{4}-\d{4}", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var orderNum = match.Value;
                    var order = await _db.Orders
                        .AsNoTracking()
                        .Include(o => o.Customer)
                            .ThenInclude(c => c.Addresses)
                        .Include(o => o.Customer)
                            .ThenInclude(c => c.Orders)
                                .ThenInclude(co => co.Items)
                        .FirstOrDefaultAsync(o => o.OrderNumber == orderNum);

                    if (order?.Customer != null)
                    {
                        cust = order.Customer;
                    }
                }
            }
        }

        if (cust == null)
        {
            return Ok(new
            {
                found = false,
                phone = localPhone,
                ordersCount = 0,
                totalSpent = 0,
                recentOrders = new object[] {}
            });
        }

        var orders = cust.Orders
            .OrderByDescending(o => o.CreatedAt)
            .Take(15)
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

        var defaultAddress = cust.Addresses.FirstOrDefault(a => a.IsDefault) ?? cust.Addresses.FirstOrDefault();
        string? fullAddress = defaultAddress != null
            ? $"{defaultAddress.City} - {defaultAddress.District} {defaultAddress.Street}".Trim(' ', '-')
            : null;

        return Ok(new
        {
            found = true,
            id = cust.Id,
            fullName = cust.FullName,
            phone = cust.Phone ?? localPhone,
            email = cust.Email,
            city = defaultAddress?.City,
            address = fullAddress,
            notes = cust.Notes,
            ordersCount = cust.Orders.Count,
            totalSpent = cust.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
            recentOrders = orders
        });
    }

    [HttpGet("{phone}")]
    public async Task<IActionResult> GetChatHistory(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest("Phone is required");

        var (_, _, localPhone, intlPhone) = GenerateSearchHashes(phone);
        var raw = phone.Replace("+", "").Replace(" ", "").Replace("-", "").Trim();
        var last9 = raw.Length >= 9 ? raw.Substring(raw.Length - 9) : raw;

        var messages = await _db.WhatsAppMessages
            .AsNoTracking()
            .Where(m => m.Phone == localPhone || m.Phone == intlPhone || m.Phone == raw || (last9.Length >= 8 && m.Phone.EndsWith(last9)))
            .OrderByDescending(m => m.Timestamp)
            .Take(250)
            .ToListAsync();

        var distinctMessages = messages
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .OrderBy(m => m.Timestamp)
            .ToList();

        return Ok(new
        {
            phone = localPhone,
            connected = true,
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
