using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Threading.Tasks;

namespace Sportive.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FixController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IAccountingService _accounting;

        public FixController(AppDbContext db, IAccountingService accounting)
        {
            _db = db;
            _accounting = accounting;
        }

        [HttpGet("fix-canceled-order")]
        public async Task<IActionResult> FixCanceledOrder()
        {
            // Delete JE-WEB-2607-0072
            var badReturnEntry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.EntryNumber == "JE-WEB-2607-0072");
            if (badReturnEntry != null)
            {
                _db.JournalLines.RemoveRange(badReturnEntry.Lines);
                _db.JournalEntries.Remove(badReturnEntry);
            }

            // Reverse JE-WEB-2607-0068
            var saleEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.EntryNumber == "JE-WEB-2607-0068");
            if (saleEntry != null && saleEntry.Status != JournalEntryStatus.Reversed)
            {
                await _accounting.ReverseEntryAsync(saleEntry.Id, "إلغاء الطلب");
            }

            await _db.SaveChangesAsync();

            return Ok("Fixed");
        }
    }
}
