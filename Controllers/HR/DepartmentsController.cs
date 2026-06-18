using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/departments")]
[RequirePermission(ModuleKeys.HrDepartments)]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    private readonly IAuditService _audit;
    public DepartmentsController(AppDbContext db, ITranslator t, IAuditService audit) { _db = db; _t = t; _audit = audit; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DepartmentDto>>> GetDepartments()
    {
        return await _db.Departments
            .Include(d => d.ParentDepartment)
            .Include(d => d.Manager)
            .Select(d => new DepartmentDto(
                d.Id, 
                d.Name, 
                d.Description, 
                d.Employees.Count, 
                d.WorkHoursPerDay, 
                d.OvertimeMultiplier, 
                d.DaysPerMonth,
                d.ParentDepartmentId,
                d.ParentDepartment != null ? d.ParentDepartment.Name : null,
                d.ManagerEmployeeId,
                d.Manager != null ? d.Manager.Name : null
            ))
            .ToListAsync();
    }

    [RequirePermission(ModuleKeys.HrDepartments, requireEdit: true)]
    [HttpPost]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment(CreateDepartmentDto dto)
    {
        var dept = new Department 
        { 
            Name = dto.Name, 
            Description = dto.Description,
            WorkHoursPerDay = dto.WorkHoursPerDay,
            OvertimeMultiplier = dto.OvertimeMultiplier,
            DaysPerMonth = dto.DaysPerMonth,
            ParentDepartmentId = dto.ParentDepartmentId,
            ManagerEmployeeId = dto.ManagerEmployeeId
        };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("CreateDepartment", "Department", dept.Id.ToString(), $"Created department {dept.Name}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        
        return Ok(new DepartmentDto(
            dept.Id, 
            dept.Name, 
            dept.Description, 
            0, 
            dept.WorkHoursPerDay, 
            dept.OvertimeMultiplier, 
            dept.DaysPerMonth,
            dept.ParentDepartmentId,
            null,
            dept.ManagerEmployeeId,
            null
        ));
    }

    [RequirePermission(ModuleKeys.HrDepartments, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDepartment(int id, CreateDepartmentDto dto)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();

        dept.Name = dto.Name;
        dept.Description = dto.Description;
        dept.WorkHoursPerDay = dto.WorkHoursPerDay;
        dept.OvertimeMultiplier = dto.OvertimeMultiplier;
        dept.DaysPerMonth = dto.DaysPerMonth;
        dept.ParentDepartmentId = dto.ParentDepartmentId;
        dept.ManagerEmployeeId = dto.ManagerEmployeeId;

        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("UpdateDepartment", "Department", id.ToString(), $"Updated department {dept.Name}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return NoContent();
    }

    [RequirePermission(ModuleKeys.HrDepartments, requireEdit: true)]
    [HttpPost("{id}/apply-to-employees")]
    public async Task<IActionResult> BulkUpdateEmployeesFromDepartment(int id)
    {
        var dept = await _db.Departments.Include(d => d.Employees).FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null) return NotFound();

        foreach (var emp in dept.Employees)
        {
            emp.WorkHoursPerDay = dept.WorkHoursPerDay;
            emp.OvertimeMultiplier = dept.OvertimeMultiplier;
            emp.DaysPerMonth = dept.DaysPerMonth;
        }

        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("BulkUpdateEmployeesFromDepartment", "Department", id.ToString(), $"Applied department settings to {dept.Employees.Count} employees", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(new { message = $"Settings applied to {dept.Employees.Count} employees." });
    }

    [RequirePermission(ModuleKeys.HrDepartments, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dept = await _db.Departments.Include(d => d.Employees).FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null) return NotFound();
        if (dept.Employees.Any()) return BadRequest(new { message = _t.Get("HR.DepartmentHasEmployees") });
        
        var hasSubDepts = await _db.Departments.AnyAsync(d => d.ParentDepartmentId == id);
        if (hasSubDepts) return BadRequest(new { message = "لا يمكن حذف قسم يحتوي على أقسام فرعية." });

        var deptName = dept.Name;
        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        try { await _audit.LogAsync("DeleteDepartment", "Department", id.ToString(), $"Deleted department {deptName}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return NoContent();
    }
}
