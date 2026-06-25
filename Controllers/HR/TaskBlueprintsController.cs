using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Controllers.HR
{
    [ApiController]
    [Route("api/hr/task-blueprints")]
    [Authorize]
    public class TaskBlueprintsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TaskBlueprintsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _db.TaskBlueprints
                .Include(b => b.Employee)
                .Include(b => b.ResponsibilityType)
                .OrderByDescending(b => b.Id)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    Employee = new { b.Employee.Id, b.Employee.Name },
                    ResponsibilityType = new { b.ResponsibilityType.Id, b.ResponsibilityType.Name },
                    b.StartDate,
                    b.EndDate,
                    b.ActiveDaysOfWeek,
                    b.TaskBehavior,
                    b.TargetQuantity,
                    b.RewardAmount,
                    b.PenaltyAmount,
                    b.CriteriaJson,
                    b.IsActive
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var bp = await _db.TaskBlueprints.FindAsync(id);
            if (bp == null) return NotFound();
            return Ok(bp);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaskBlueprint dto)
        {
            dto.CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _db.TaskBlueprints.Add(dto);
            await _db.SaveChangesAsync();
            return Ok(dto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] TaskBlueprint dto)
        {
            var existing = await _db.TaskBlueprints.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name = dto.Name;
            existing.EmployeeId = dto.EmployeeId;
            existing.ResponsibilityTypeId = dto.ResponsibilityTypeId;
            existing.StartDate = dto.StartDate;
            existing.EndDate = dto.EndDate;
            existing.ActiveDaysOfWeek = dto.ActiveDaysOfWeek;
            existing.TaskBehavior = dto.TaskBehavior;
            existing.TargetQuantity = dto.TargetQuantity;
            existing.RewardAmount = dto.RewardAmount;
            existing.PenaltyAmount = dto.PenaltyAmount;
            existing.CriteriaJson = dto.CriteriaJson;
            existing.IsActive = dto.IsActive;

            await _db.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _db.TaskBlueprints.FindAsync(id);
            if (existing == null) return NotFound();

            _db.TaskBlueprints.Remove(existing);
            await _db.SaveChangesAsync();
            return Ok();
        }
        
        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var existing = await _db.TaskBlueprints.FindAsync(id);
            if (existing == null) return NotFound();

            existing.IsActive = !existing.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { isActive = existing.IsActive });
        }
    }
}
