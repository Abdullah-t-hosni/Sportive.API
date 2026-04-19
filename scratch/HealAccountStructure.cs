using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;

public class HealAccountStructure {
    public static async Task Main(string[] args) {
        var connectionString = "Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=Abdo010152144;";
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        using var db = new AppDbContext(optionsBuilder.Options);
        
        Console.WriteLine("--- بدء عملية الإصلاح ---");

        // 1. إعادة إنشاء حسابات الكاشير (110701, 110703)
        var parent1107 = await db.Accounts.FirstOrDefaultAsync(a => a.Code == "1107");
        
        async Task<Account> EnsureAccount(string code, string nameAr, string nameEn, AccountType type, AccountNature nature, int? parentId) {
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code);
            if (acc == null) {
                acc = new Account {
                    Code = code,
                    NameAr = nameAr,
                    NameEn = nameEn,
                    Type = type,
                    Nature = nature,
                    IsActive = true,
                    IsLeaf = true,
                    AllowPosting = true,
                    ParentId = parentId,
                    Level = 3
                };
                db.Accounts.Add(acc);
                await db.SaveChangesAsync();
                Console.WriteLine($"   تم إنشاء حساب: {nameAr} ({code})");
            } else {
                acc.IsActive = true;
                await db.SaveChangesAsync();
                Console.WriteLine($"   تم تفعيل حساب: {nameAr} ({code})");
            }
            return acc;
        }

        var vCashier = await EnsureAccount("110701", "فودافون كاش كاشير", "Vodafone Cash Cashier", AccountType.Asset, AccountNature.Debit, parent1107?.Id);
        var iCashier = await EnsureAccount("110703", "انستاباي كاشير", "Instapay Cashier", AccountType.Asset, AccountNature.Debit, parent1107?.Id);

        // 2. تحديث المربوط (Mappings)
        async Task UpdateMapping(string key, int accountId) {
            var mapping = await db.AccountSystemMappings.FirstOrDefaultAsync(m => m.Key.ToLower() == key.ToLower());
            if (mapping != null) {
                mapping.AccountId = accountId;
                await db.SaveChangesAsync();
                Console.WriteLine($"   تم تحديث الربط: {key} -> {accountId}");
            } else {
                mapping = new AccountSystemMapping { Key = key, AccountId = accountId };
                db.AccountSystemMappings.Add(mapping);
                await db.SaveChangesAsync();
                Console.WriteLine($"   تم إضافة ربط جديد: {key} -> {accountId}");
            }
        }

        await UpdateMapping(MappingKeys.PosVodafone, vCashier.Id);
        await UpdateMapping(MappingKeys.PosInstaPay, iCashier.Id);

        // 3. تصحيح ربط الرواتب (2201 هو الحساب الصحيح للرواتب والالتزمات تجاه الموظفين)
        // التأكد من اسم حساب 2201
        var acc2201 = await db.Accounts.FirstOrDefaultAsync(a => a.Code == "2201");
        if (acc2201 != null) {
            acc2201.NameAr = "رواتب مستحقة - موظفين";
            acc2201.Type = AccountType.Liability;
            acc2201.Nature = AccountNature.Credit;
            await db.SaveChangesAsync();
        }
        
        // 4. حذف الـ POS الغريب (11040001)
        var oddPos = await db.Accounts.FirstOrDefaultAsync(a => a.Code == "11040001");
        if (oddPos != null) {
            var linesCount = await db.JournalLines.CountAsync(l => l.AccountId == oddPos.Id);
            if (linesCount == 0) {
                db.Accounts.Remove(oddPos);
                await db.SaveChangesAsync();
                Console.WriteLine("   تم حذف حساب POS القديم (11040001) بنجاح.");
            }
        }

        Console.WriteLine("--- انتهى الإصلاح بنجاح! ---");
    }
}
