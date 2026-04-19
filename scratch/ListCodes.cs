using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

public class ListCodes {
    public static async Task Main(string[] args) {
        var connectionString = "Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=Abdo010152144;";
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        using var db = new AppDbContext(optionsBuilder.Options);
        
        var top10 = await db.Accounts.Where(a => a.Code.StartsWith("1103") || a.Code.StartsWith("2101")).Take(10).ToListAsync();
        foreach (var a in top10) {
            Console.WriteLine($"Code: {a.Code}, Name: {a.NameAr}, IsActive: {a.IsActive}");
        }
    }
}
