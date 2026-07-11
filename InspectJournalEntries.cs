using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;

var builder = WebApplication.CreateBuilder(args);
var connectionString = "Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=Abdo010152144;Max Pool Size=15;";
var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

using var db = new AppDbContext(optionsBuilder.Options);
var entries = db.JournalEntries
    .Include(e => e.Lines)
    .Where(e => e.Reference == "SPT-2607-0075" || e.Reference == "JE-WEB-2607-0077")
    .ToList();

foreach (var e in entries)
{
    Console.WriteLine($"Entry: {e.EntryNumber} (Id: {e.Id}) - Status: {e.Status}");
}
