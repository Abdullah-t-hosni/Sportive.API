using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

var connectionString = "Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=Abdo010152144;";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
    .Options;

using var db = new AppDbContext(options);

var productsWithPositiveStock = db.Products
    .Include(p => p.Variants)
    .Where(p => p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock || p.Status == ProductStatus.Discontinued)
    .Where(p => p.TotalStock > 0 || p.Variants.Any(v => v.StockQuantity > 0))
    .ToList();

Console.WriteLine("Products with Positive Stock: " + productsWithPositiveStock.Count);

foreach(var p in productsWithPositiveStock)
{
    Console.WriteLine($"[{p.SKU}] {p.NameAr} - TotalStock: {p.TotalStock}, Variants Count: {p.Variants.Count}, Variants Positive: {p.Variants.Count(v => v.StockQuantity > 0)}");
}

var allProducts = db.Products.Count();
Console.WriteLine("Total Products in DB: " + allProducts);
