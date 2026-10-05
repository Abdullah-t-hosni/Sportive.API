using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Text.Json;

namespace Sportive.API.Services
{
    public class TaskGenerationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TaskGenerationService> _logger;

        public TaskGenerationService(IServiceProvider serviceProvider, ILogger<TaskGenerationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await GenerateDailyTasksAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating daily tasks.");
                }

                try
                {
                    await CheckAndPostDailyPartnersReportAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking/posting daily partners report.");
                }

                // Check every 15 minutes to guarantee timely execution and recover from app sleep/restarts
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }

        public async Task GenerateDailyTasksAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // We use Egypt time for local task generation date
            var today = TimeHelper.GetEgyptTime().Date;
            int currentDayOfWeek = (int)today.DayOfWeek; // 0 = Sunday, 1 = Monday ... 6 = Saturday

            // 1. Get all active blueprints that are within their valid date range
            var activeBlueprints = await db.TaskBlueprints
                .Include(b => b.Employee)
                .Include(b => b.ResponsibilityType)
                .Where(b => b.IsActive 
                            && b.StartDate.Date <= today 
                            && (b.EndDate == null || b.EndDate.Value.Date >= today))
                .ToListAsync();

            foreach (var blueprint in activeBlueprints)
            {
                // Check if today is an active day of week for this blueprint
                if (!string.IsNullOrEmpty(blueprint.ActiveDaysOfWeek) && 
                    !blueprint.ActiveDaysOfWeek.Contains(currentDayOfWeek.ToString()))
                {
                    continue; // Skip, it's not supposed to run today
                }

                // Check if a task was already generated for this blueprint for today
                var alreadyGenerated = await db.EmployeeTasks.AnyAsync(t => 
                    t.TaskBlueprintId == blueprint.Id && t.TaskDate.Date == today);

                if (alreadyGenerated) continue;

                // We need to generate a task!
                var newTask = new EmployeeTask
                {
                    EmployeeId = blueprint.EmployeeId,
                    ResponsibilityTypeId = blueprint.ResponsibilityTypeId,
                    TaskBlueprintId = blueprint.Id,
                    Title = blueprint.Name,
                    Description = blueprint.ResponsibilityType?.Description,
                    TaskDate = today,
                    DueDate = today.AddDays(1).AddSeconds(-1),
                    Status = EmployeeTaskStatus.Pending,
                    TargetQuantity = blueprint.TargetQuantity,
                    MaxBonusAmount = blueprint.RewardAmount,
                    MaxDeductionAmount = blueprint.PenaltyAmount,
                    CriteriaJson = blueprint.CriteriaJson,
                    CreatedByUserId = "SYSTEM",
                    Items = new List<EmployeeTaskItem>()
                };

                // Generate specific items based on Behavior
                if (blueprint.TaskBehavior == "RandomInventory")
                {
                    int quantity = (int)blueprint.TargetQuantity;
                    if (quantity <= 0) quantity = 1;

                    var productQuery = db.Products.AsQueryable();

                    // Apply filters from CriteriaJson
                    if (!string.IsNullOrEmpty(blueprint.CriteriaJson))
                    {
                        try
                        {
                            var filters = JsonSerializer.Deserialize<Dictionary<string, string>>(blueprint.CriteriaJson);
                            if (filters != null)
                            {
                                if (filters.TryGetValue("Status", out var statusVal) && Enum.TryParse<ProductStatus>(statusVal, out var parsedStatus))
                                {
                                    productQuery = productQuery.Where(p => p.Status == parsedStatus);
                                }
                                if (filters.TryGetValue("CategoryId", out var catIdStr) && int.TryParse(catIdStr, out var catId))
                                {
                                    productQuery = productQuery.Where(p => p.CategoryId == catId);
                                }
                            }
                        }
                        catch { /* Ignore parsing errors */ }
                    }

                    // Select Random Products
                    var randomProducts = await productQuery
                        .OrderBy(x => Guid.NewGuid()) // RANDOM SORTING
                        .Take(quantity)
                        .Select(p => new { p.Id, p.NameAr })
                        .ToListAsync();

                    foreach (var p in randomProducts)
                    {
                        newTask.Items.Add(new EmployeeTaskItem
                        {
                            ProductId = p.Id,
                            ItemName = p.NameAr,
                            ExpectedQuantity = 0, // This is a blind count for the employee
                            ActualQuantity = 0,
                            IsCompleted = false
                        });
                    }
                    
                    // The TargetQuantity for the task itself is the number of products they need to inventory
                    newTask.TargetQuantity = randomProducts.Count;
                }

                db.EmployeeTasks.Add(newTask);
            }

            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync();
                _logger.LogInformation("Generated daily employee tasks.");
            }
        }

        private async Task CheckAndPostDailyPartnersReportAsync()
        {
            var egyptTime = TimeHelper.GetEgyptTime();
            // تقرير الشركاء والمتجر يصدر تلقائياً ابتداءً من الساعة 8:00 صباحاً بتوقيت مصر
            if (egyptTime.Hour < 8) return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var targetDate = egyptTime.Date.AddDays(-1);
            var reportRef = $"PARTNERS-{targetDate:yyyy-MM-dd}";

            var partnersChan = await db.InternalChatChannels
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.DirectKey == InternalChatBotService.ChannelKeyPartners);

            if (partnersChan == null) return;

            var alreadyPosted = await db.InternalChatMessages
                .AsNoTracking()
                .AnyAsync(m => m.ChannelId == partnersChan.Id && m.LinkedEntityRef == reportRef);

            if (!alreadyPosted)
            {
                var chatBot = scope.ServiceProvider.GetRequiredService<IInternalChatBotService>();
                _logger.LogInformation("Automatic daily partners report triggered by background runner for date {TargetDate}", targetDate.ToString("yyyy-MM-dd"));
                await chatBot.PostDailyPartnersAndStoreReportAsync(targetDate);
            }
        }
    }
}
