using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sportive.API.Scratch
{
    public class CheckJournalSorting
    {
        private readonly AppDbContext _db;
        public CheckJournalSorting(AppDbContext db) => _db = db;

        public async Task Run()
        {
            var entries = await _db.JournalEntries
                .OrderByDescending(e => e.Id)
                .Take(5)
                .Select(e => new { e.Id, e.EntryNumber, e.CreatedAt, e.EntryDate, e.Description })
                .ToListAsync();

            Console.WriteLine("ID | Number | CreatedAt | EntryDate | Description");
            foreach (var e in entries)
            {
                Console.WriteLine($"{e.Id} | {e.EntryNumber} | {e.CreatedAt} | {e.EntryDate} | {e.Description}");
            }
        }
    }
}
