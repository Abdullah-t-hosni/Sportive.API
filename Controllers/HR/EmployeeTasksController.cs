using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Controllers.HR
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeTasksController : ControllerBase
    {
        private readonly AppDbContext _context;

        public EmployeeTasksController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/EmployeeTasks
        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeTask>>> GetEmployeeTasks([FromQuery] int? employeeId)
        {
            var query = _context.EmployeeTasks
                .Include(t => t.ResponsibilityType)
                .Include(t => t.Employee)
                .AsQueryable();

            if (employeeId.HasValue)
            {
                query = query.Where(t => t.EmployeeId == employeeId.Value);
            }

            return await query.OrderByDescending(t => t.TaskDate).ToListAsync();
        }

        // GET: api/EmployeeTasks/my-tasks
        [HttpGet("my-tasks")]
        public async Task<ActionResult<IEnumerable<EmployeeTask>>> GetMyTasks()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.AppUserId == userId);
            
            if (employee == null) return BadRequest("لم يتم العثور على ملف موظف مرتبط بحسابك.");

            var tasks = await _context.EmployeeTasks
                .Include(t => t.ResponsibilityType)
                .Where(t => t.EmployeeId == employee.Id)
                .OrderByDescending(t => t.TaskDate)
                .ToListAsync();

            return tasks;
        }

        // GET: api/EmployeeTasks/5
        [HttpGet("{id}")]
        public async Task<ActionResult<EmployeeTask>> GetEmployeeTask(int id)
        {
            var task = await _context.EmployeeTasks
                .Include(t => t.ResponsibilityType)
                .Include(t => t.Employee)
                .Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null)
            {
                return NotFound();
            }

            return task;
        }

        // POST: api/EmployeeTasks
        [HttpPost]
        public async Task<ActionResult<EmployeeTask>> PostEmployeeTask(EmployeeTask employeeTask)
        {
            employeeTask.CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            employeeTask.Status = EmployeeTaskStatus.Pending;

            // Here we can auto-generate Items based on the ResponsibilityType and CriteriaJson
            // For example, if it's an INVENTORY task and criteria has a CategoryId...
            
            _context.EmployeeTasks.Add(employeeTask);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetEmployeeTask), new { id = employeeTask.Id }, employeeTask);
        }

        // POST: api/EmployeeTasks/5/submit
        // Employee submits the task
        [HttpPost("{id}/submit")]
        public async Task<IActionResult> SubmitTask(int id, [FromBody] SubmitTaskDto dto)
        {
            var task = await _context.EmployeeTasks.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return NotFound();

            if (task.Status != EmployeeTaskStatus.Pending && task.Status != EmployeeTaskStatus.InProgress && task.Status != EmployeeTaskStatus.Rework)
            {
                return BadRequest("لا يمكن تقديم هذه المهمة في حالتها الحالية.");
            }

            task.Status = EmployeeTaskStatus.Submitted;
            task.CompletedQuantity = dto.CompletedQuantity;
            task.EmployeeNotes = dto.Notes;

            // Update item details if provided
            if (dto.Items != null && dto.Items.Any())
            {
                foreach(var dtoItem in dto.Items)
                {
                    var existingItem = task.Items.FirstOrDefault(i => i.Id == dtoItem.Id);
                    if(existingItem != null)
                    {
                        existingItem.ActualQuantity = dtoItem.ActualQuantity;
                        existingItem.IsCompleted = dtoItem.IsCompleted;
                        existingItem.Notes = dtoItem.Notes;
                    }
                }
            }

            await _context.SaveChangesAsync();
            return Ok(task);
        }

        // POST: api/EmployeeTasks/5/evaluate
        // Manager evaluates the task (Approves or Rejects)
        [HttpPost("{id}/evaluate")]
        public async Task<IActionResult> EvaluateTask(int id, [FromBody] EvaluateTaskDto dto)
        {
            var task = await _context.EmployeeTasks.Include(t => t.Employee).FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return NotFound();

            if (task.Status != EmployeeTaskStatus.Submitted)
            {
                return BadRequest("المهمة ليست في حالة انتظار التقييم.");
            }

            task.Status = dto.Status; // Approved, Rejected, or Rework
            task.ManagerNotes = dto.ManagerNotes;
            
            if (dto.Status == EmployeeTaskStatus.Approved)
            {
                task.CompletedQuantity = dto.FinalCompletedQuantity ?? task.CompletedQuantity;
                
                // Calculate proportional deduction / bonus
                decimal percentage = task.TargetQuantity > 0 ? (task.CompletedQuantity / task.TargetQuantity) : 1m;
                if (percentage > 1m) percentage = 1m; // Cap at 100% for calculation
                
                // Example Logic:
                // If max bonus = 100, completed 50% => 50 bonus.
                // If max deduction = 500, completed 80% => failed 20% => penalty = 0.2 * 500 = 100.
                decimal shortfallPercentage = 1m - percentage;

                task.ActualBonusAmount = Math.Round(task.MaxBonusAmount * percentage, 2);
                task.ActualDeductionAmount = Math.Round(task.MaxDeductionAmount * shortfallPercentage, 2);

                // Auto-generate Bonus/Deduction in Payroll/Records
                if (task.ActualBonusAmount > 0)
                {
                    var bonus = new EmployeeBonus
                    {
                        EmployeeId = task.EmployeeId,
                        BonusDate = DateTime.Now,
                        Amount = task.ActualBonusAmount,
                        Reason = $"مكافأة عن إنجاز المهمة: {task.Title}",
                        Notes = task.ManagerNotes
                    };
                    _context.EmployeeBonuses.Add(bonus);
                    await _context.SaveChangesAsync(); // Save to get ID
                    task.EmployeeBonusId = bonus.Id;
                }

                if (task.ActualDeductionAmount > 0)
                {
                    var deduction = new EmployeeDeduction
                    {
                        EmployeeId = task.EmployeeId,
                        DeductionDate = DateTime.Now,
                        Amount = task.ActualDeductionAmount,
                        Reason = $"خصم لقصور في المهمة: {task.Title} (نسبة الإنجاز {percentage:P0})",
                        Notes = task.ManagerNotes
                    };
                    _context.EmployeeDeductions.Add(deduction);
                    await _context.SaveChangesAsync(); // Save to get ID
                    task.EmployeeDeductionId = deduction.Id;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(task);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEmployeeTask(int id)
        {
            var task = await _context.EmployeeTasks.FindAsync(id);
            if (task == null) return NotFound();
            
            if (task.Status == EmployeeTaskStatus.Approved)
                return BadRequest("لا يمكن حذف مهمة معتمدة.");

            _context.EmployeeTasks.Remove(task);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class SubmitTaskDto
    {
        public decimal CompletedQuantity { get; set; }
        public string? Notes { get; set; }
        public List<SubmitTaskItemDto>? Items { get; set; }
    }

    public class SubmitTaskItemDto
    {
        public int Id { get; set; }
        public decimal ActualQuantity { get; set; }
        public bool IsCompleted { get; set; }
        public string? Notes { get; set; }
    }

    public class EvaluateTaskDto
    {
        public EmployeeTaskStatus Status { get; set; }
        public decimal? FinalCompletedQuantity { get; set; }
        public string? ManagerNotes { get; set; }
    }
}
