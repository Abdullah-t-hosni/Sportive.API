using System;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Linq;
using Sportive.API.Services;
using Sportive.API.Models;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Sportive.API.Interfaces;
using Sportive.API.DTOs;

namespace CheckDb {
    class DummyLocalizer : IStringLocalizer<SharedResource> {
        public LocalizedString this[string name] => new LocalizedString(name, name);
        public LocalizedString this[string name, params object[] arguments] => new LocalizedString(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => new List<LocalizedString>();
    }
    
    class DummySequence : ISequenceGenerator {
        public System.Threading.Tasks.Task<string> NextAsync(string type) => System.Threading.Tasks.Task.FromResult("SEQ-TEST");
    }

    class Program {
        static async System.Threading.Tasks.Task Main() {
            var conf = new ConfigurationBuilder().SetBasePath(@"E:\porject\Sportive\Sportive.API").AddJsonFile("appsettings.Development.json").Build();
            
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(opts => opts.UseMySql(conf.GetConnectionString("DefaultConnection"), ServerVersion.AutoDetect(conf.GetConnectionString("DefaultConnection"))));
            services.AddLogging(b => b.AddConsole());
            services.AddSingleton<IStringLocalizer<SharedResource>, DummyLocalizer>();
            services.AddSingleton<ISequenceGenerator, DummySequence>();
            services.AddScoped<AccountingCoreService>();
            services.AddScoped<Sportive.API.Services.Accounting.SalesAccountingService>();
            
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var acc = scope.ServiceProvider.GetRequiredService<Sportive.API.Services.Accounting.SalesAccountingService>();
            
            var order = await db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).Include(o => o.Customer).Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == 3986);
                
            if (order != null) {
                Console.WriteLine("Simulating accounting for Return Order: " + order.Id);
                try {
                    await acc.PostPartialSalesReturnAsync(order, order.Items.ToList(), 945);
                    Console.WriteLine("Simulation Success! (No exception thrown)");
                    await db.SaveChangesAsync();
                } catch(Exception e) {
                    Console.WriteLine("SIMULATION ERROR: " + e.Message);
                    if (e.InnerException != null) Console.WriteLine("INNER: " + e.InnerException.Message);
                }
            }
        }
    }
}
