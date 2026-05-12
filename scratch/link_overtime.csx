using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
optionsBuilder.UseSqlite(config.GetConnectionString("DefaultConnection"));

using (var db = new AppDbContext(optionsBuilder.Options))
{
    var code = Args.FirstOrDefault() ?? "51203";
    var account = db.Accounts.FirstOrDefault(a => a.Code == code);
    if (account == null)
    {
        Console.WriteLine($"Account with code {code} not found.");
        return;
    }
    Console.WriteLine($"Found Account: ID={account.Id}, Code={account.Code}, Name={account.NameAr}");

    var mappingKey = "overtimeExpenseAccountID"; // MappingKeys.OvertimeExpense
    var mapping = db.AccountSystemMappings.FirstOrDefault(m => m.Key == mappingKey);
    if (mapping == null)
    {
        mapping = new AccountSystemMapping { Key = mappingKey, AccountId = account.Id };
        db.AccountSystemMappings.Add(mapping);
        Console.WriteLine($"Created new mapping for {mappingKey} to account {account.Id}");
    }
    else
    {
        mapping.AccountId = account.Id;
        Console.WriteLine($"Updated existing mapping for {mappingKey} to account {account.Id}");
    }
    db.SaveChanges();
    Console.WriteLine("Changes saved successfully.");
}
