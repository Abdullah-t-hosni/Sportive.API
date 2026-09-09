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
