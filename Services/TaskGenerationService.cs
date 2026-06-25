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

                // Run every 1 hour to check if new day started or new blueprints added for today
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
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
    }
}
