using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Microsoft.Extensions.DependencyInjection;

var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
optionsBuilder.UseMySql(Environment.GetEnvironmentVariable("DATABASE_URL"), ServerVersion.AutoDetect(Environment.GetEnvironmentVariable("DATABASE_URL")));
using var db = new AppDbContext(optionsBuilder.Options);

var refs = new[] { "POS-2606-0096", "POS-2606-0097", "POS-2606-0098", "POS-2606-0099", "POS-2606-0100", "POS-2606-0101", "POS-2606-0102", "POS-2606-0160", "POS-2606-0432" };
foreach(var r in refs) {
    var orderExists = db.Orders.Any(o => o.OrderNumber == r);
    var movements = db.InventoryMovements.Where(m => m.Reference == r).ToList();
    Console.WriteLine($"Ref: {r}, OrderExists: {orderExists}, Movements: {movements.Count}");
}
