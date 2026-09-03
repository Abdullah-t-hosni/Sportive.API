using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/whatsapp/chats")]
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

        // Clean phone
        var cleanPhone = phone.Replace("+", "").Replace(" ", "").Trim();
        if (cleanPhone.StartsWith("20") && cleanPhone.Length == 12)
            cleanPhone = "0" + cleanPhone.Substring(2);

        var messages = await _db.WhatsAppMessages
            .Where(m => m.Phone == cleanPhone)
            .OrderByDescending(m => m.Timestamp)
            .Take(200)
            .ToListAsync();

        messages.Reverse();

        return Ok(new
        {
            phone = cleanPhone,
            connected = true, // To satisfy frontend expectations
            count = messages.Count,
            messages = messages.Select(m => new
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
