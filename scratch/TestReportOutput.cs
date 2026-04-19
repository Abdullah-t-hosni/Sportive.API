using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Utils;
using Sportive.API.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class TestReportOutput {
    public static async Task Main(string[] args) {
        var connectionString = "Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=Abdo010152144;";
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        using var db = new AppDbContext(optionsBuilder.Options);
        
        var from = new DateTime(2026, 1, 1);
        var to = DateTime.Now.AddDays(1);
        
        var movementsQuery = db.InventoryMovements
                .Include(m => m.Product)
                .Include(m => m.ProductVariant)
                .Where(m => m.CreatedAt >= from && m.CreatedAt <= to);

        var dbMovements = await movementsQuery.OrderBy(m => m.CreatedAt).ToListAsync();
        
        Console.WriteLine($"--- API Test Output (Movements Count: {dbMovements.Count}) ---");
        foreach(var m in dbMovements) {
            Console.WriteLine($"Date: {m.CreatedAt}, Type: {m.Type}, SKU: {m.Product?.SKU}, Qty: {m.Quantity}");
        }
    }
}
