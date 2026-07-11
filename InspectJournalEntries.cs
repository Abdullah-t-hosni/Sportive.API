using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

using var db = new AppDbContext(optionsBuilder.Options);
var entries = db.JournalEntries.Include(e => e.Lines).Where(e => e.EntryNumber == "JE-WEB-2607-0077" || e.EntryNumber == "JE-2607-0081").ToList();

foreach (var e in entries)
{
    Console.WriteLine($"Entry: {e.EntryNumber} (Id: {e.Id})");
    foreach (var l in e.Lines)
    {
        Console.WriteLine($"  - AccountId: {l.AccountId}, Debit: {l.Debit}, Credit: {l.Credit}, Desc: {l.Description}");
    }
}
