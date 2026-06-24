using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ClosedXML.Excel;
using Sportive.API.Attributes;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services.HR;
using Microsoft.Extensions.Logging;
using Sportive.API.Interfaces;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/employee-attendances")]
[RequirePermission(ModuleKeys.HrAttendance)]
public class EmployeeAttendancesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    private readonly ZKDeviceService _zkService;
    private readonly ILogger<EmployeeAttendancesController> _logger;

    public EmployeeAttendancesController(AppDbContext db, ITranslator t, ZKDeviceService zkService, ILogger<EmployeeAttendancesController> logger)
    {
        _db = db;
        _t = t;
        _zkService = zkService;
        _logger = logger;
    }

    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    [RequirePermission(ModuleKeys.HrAttendance)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? employeeId = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var q = _db.EmployeeAttendances
            .Include(a => a.Employee)
            .ThenInclude(e => e.Department)
            .AsQueryable();

        if (employeeId.HasValue)
            q = q.Where(a => a.EmployeeId == employeeId.Value);
        
        if (departmentId.HasValue)
            q = q.Where(a => a.Employee.DepartmentId == departmentId.Value);

        if (startDate.HasValue)
            q = q.Where(a => a.Date >= startDate.Value.Date);

        if (endDate.HasValue)
            q = q.Where(a => a.Date <= endDate.Value.Date);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.Date).ThenBy(a => a.Employee.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new EmployeeAttendanceDto(
                a.Id,
                a.EmployeeId,
                a.Employee.Name,
                a.Employee.EmployeeNumber,
                a.Date,
                a.CheckIn,
                a.CheckOut,
                a.WorkHours,
                a.OvertimeHours,
                a.DelayMinutes,
                a.IsAbsent,
                a.Notes,
                a.CreatedByUserId,
                a.CreatedAt
            )).ToListAsync();

        return Ok(new PaginatedResult<EmployeeAttendanceDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    [RequirePermission(ModuleKeys.HrAttendance, requireEdit: true)]
    public async Task<IActionResult> Create([FromBody] CreateAttendanceDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId);
        if (emp == null) return NotFound(new { message = "Employee not found." });

        // Check if record exists for this date
        var existing = await _db.EmployeeAttendances
            .FirstOrDefaultAsync(a => a.EmployeeId == dto.EmployeeId && a.Date == dto.Date.Date);
        if (existing != null)
            return BadRequest(new { message = _t.Get("HR.AttendanceRecordExists") ?? "An attendance record already exists for this employee on this date." });

        var attendance = new EmployeeAttendance
        {
            EmployeeId = dto.EmployeeId,
            Date = dto.Date.Date,
            CheckIn = dto.CheckIn,
            CheckOut = dto.CheckOut,
            WorkHours = dto.WorkHours,
            OvertimeHours = dto.OvertimeHours,
            DelayMinutes = dto.DelayMinutes,
            IsAbsent = dto.IsAbsent,
            Notes = dto.Notes,
            CreatedByUserId = UserId,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _db.EmployeeAttendances.Add(attendance);
        await _db.SaveChangesAsync();

        return Ok(attendance);
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.HrAttendance, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAttendanceDto dto)
    {
        var attendance = await _db.EmployeeAttendances.FindAsync(id);
        if (attendance == null) return NotFound();

        attendance.CheckIn = dto.CheckIn;
        attendance.CheckOut = dto.CheckOut;
        attendance.WorkHours = dto.WorkHours;
        attendance.OvertimeHours = dto.OvertimeHours;
        attendance.DelayMinutes = dto.DelayMinutes;
        attendance.IsAbsent = dto.IsAbsent;
        attendance.Notes = dto.Notes;
        attendance.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [RequirePermission(ModuleKeys.HrAttendance, requireEdit: true)]
    public async Task<IActionResult> Delete(int id)
    {
        var attendance = await _db.EmployeeAttendances.FindAsync(id);
        if (attendance == null) return NotFound();

        _db.EmployeeAttendances.Remove(attendance);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("upload")]
    [RequirePermission(ModuleKeys.HrAttendance, requireEdit: true)]
    public async Task<IActionResult> UploadExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Please upload an Excel file (.xlsx)." });

        var employees = await _db.Employees.ToDictionaryAsync(e => e.EmployeeNumber.Trim(), e => e);
        var logsAdded = 0;
        var logsUpdated = 0;
        var warnings = new List<string>();

        using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream);
            using (var workbook = new XLWorkbook(stream))
            {
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                    return BadRequest(new { message = "Worksheet is empty." });

                // Find header indexes
                int empNumCol = 1, dateCol = 2, checkInCol = 3, checkOutCol = 4, notesCol = 5;
                var firstRow = worksheet.Row(1);
                
                for (int col = 1; col <= worksheet.ColumnsUsed().Count(); col++)
                {
                    var cellVal = firstRow.Cell(col).Value.ToString().Trim().ToLower();
                    if (cellVal.Contains("Ø±Ù‚Ù…") || cellVal.Contains("ÙƒÙˆØ¯") || cellVal.Contains("employee") || cellVal.Contains("id") || cellVal.Contains("code") || cellVal.Contains("ac-no"))
                        empNumCol = col;
                    else if (cellVal.Contains("ØªØ§Ø±ÙŠØ®") || cellVal.Contains("ÙŠÙˆÙ…") || cellVal.Contains("date"))
                        dateCol = col;
                    else if (cellVal.Contains("Ø­Ø¶ÙˆØ±") || cellVal.Contains("Ø¯Ø®ÙˆÙ„") || cellVal.Contains("in") || cellVal.Contains("checkin"))
                        checkInCol = col;
                    else if (cellVal.Contains("Ø§Ù†ØµØ±Ø§Ù") || cellVal.Contains("Ø®Ø±ÙˆØ¬") || cellVal.Contains("out") || cellVal.Contains("checkout"))
                        checkOutCol = col;
                    else if (cellVal.Contains("Ù…Ù„Ø§Ø­Ø¸Ø§Øª") || cellVal.Contains("notes") || cellVal.Contains("Ø¨ÙŠØ§Ù†"))
                        notesCol = col;
                }

                var rowCount = worksheet.RowsUsed().Count();
                for (int r = 2; r <= rowCount; r++)
                {
                    var row = worksheet.Row(r);
                    
                    var rawEmpNum = row.Cell(empNumCol).Value.ToString().Trim();
                    if (string.IsNullOrEmpty(rawEmpNum)) continue;

                    // Match Employee
                    if (!employees.TryGetValue(rawEmpNum, out var emp))
                    {
                        warnings.Add($"Ø§Ù„Ø³Ø·Ø± {r}: Ù„Ù… ÙŠØªÙ… Ø§Ù„Ø¹Ø«ÙˆØ± Ø¹Ù„Ù‰ Ù…ÙˆØ¸Ù Ø¨Ø±Ù‚Ù… ({rawEmpNum})");
                        continue;
                    }

                    // Parse Date
                    var rawDateVal = row.Cell(dateCol).Value;
                    DateTime attendanceDate;
                    
                    if (rawDateVal.IsDateTime)
                    {
                        attendanceDate = rawDateVal.GetDateTime().Date;
                    }
                    else
                    {
                        if (!DateTime.TryParse(rawDateVal.ToString(), out attendanceDate))
                        {
                            warnings.Add($"Ø§Ù„Ø³Ø·Ø± {r}: ØªÙ†Ø³ÙŠÙ‚ ØªØ§Ø±ÙŠØ® ØºÙŠØ± ØµØ§Ù„Ø­ ({rawDateVal}) Ù„Ù„Ù…ÙˆØ¸Ù ({emp.Name})");
                            continue;
                        }
                        attendanceDate = attendanceDate.Date;
                    }

                    // Parse times
                    DateTime? checkIn = null;
                    DateTime? checkOut = null;
                    bool isAbsent = false;

                    var rawIn = row.Cell(checkInCol).Value.ToString().Trim();
                    var rawOut = row.Cell(checkOutCol).Value.ToString().Trim();
                    
                    if (string.IsNullOrEmpty(rawIn))
                    {
                        isAbsent = true;
                    }
                    else
                    {
                        if (DateTime.TryParse(rawIn, out var parsedIn))
                        {
                            // If Excel stored a full DateTime, use it. If only time, combine with date.
                            checkIn = parsedIn.Year > 1900 ? parsedIn : attendanceDate.Add(parsedIn.TimeOfDay);
                        }
                        else if (TimeSpan.TryParse(rawIn, out var parsedTimeIn))
                        {
                            checkIn = attendanceDate.Add(parsedTimeIn);
                        }
                        else
                        {
                            warnings.Add($"Ø§Ù„Ø³Ø·Ø± {r}: ØªÙ†Ø³ÙŠÙ‚ ÙˆÙ‚Øª Ø§Ù„Ø­Ø¶ÙˆØ± ØºÙŠØ± ØµØ§Ù„Ø­ ({rawIn}) Ù„Ù„Ù…ÙˆØ¸Ù ({emp.Name})");
                            continue;
                        }
                    }

                    if (!isAbsent && !string.IsNullOrEmpty(rawOut))
                    {
                        if (DateTime.TryParse(rawOut, out var parsedOut))
                        {
                            checkOut = parsedOut.Year > 1900 ? parsedOut : attendanceDate.Add(parsedOut.TimeOfDay);
                        }
                        else if (TimeSpan.TryParse(rawOut, out var parsedTimeOut))
                        {
                            checkOut = attendanceDate.Add(parsedTimeOut);
                        }
                        
                        if (checkOut.HasValue && checkIn.HasValue && checkOut < checkIn)
                        {
                            // Shift crosses midnight, so checkout is next day
                            checkOut = checkOut.Value.AddDays(1);
                        }
                    }

                    // Calculate Hours and Delays
                    decimal workHours = 0;
                    decimal overtime = 0;
                    decimal delayMinutes = 0;

                    if (!isAbsent && checkIn.HasValue && checkOut.HasValue)
                    {
                        workHours = (decimal)(checkOut.Value - checkIn.Value).TotalHours;

                        if (emp.AttendanceMode != AttendanceMode.MonthlyTotal)
                        {
                            // â”€â”€â”€ ÙˆØ¶Ø¹ Fixed / Flexible: Ø­Ø³Ø§Ø¨ Ø¥Ø¶Ø§ÙÙŠ ÙˆØªØ£Ø®ÙŠØ± ÙŠÙˆÙ…ÙŠØ§Ù‹ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                            var stdHours = emp.WorkHoursPerDay;
                            if (workHours > stdHours)
                            {
                                overtime = workHours - stdHours;
                            }

                            if (emp.AttendanceMode == AttendanceMode.Fixed && !string.IsNullOrEmpty(emp.ShiftStartTime))
                            {
                                if (TimeSpan.TryParse(emp.ShiftStartTime, out var shiftStart))
                                {
                                    var stdCheckIn = attendanceDate.Add(shiftStart);
                                    if (checkIn.Value > stdCheckIn)
                                    {
                                        var diff = (checkIn.Value - stdCheckIn).TotalMinutes;
                                        if (diff > 0) delayMinutes = (decimal)diff;
                                    }
                                }
                            }
                        }
                        // ÙˆØ¶Ø¹ MonthlyTotal: Ù„Ø§ ÙŠÙˆØ¬Ø¯ Ø¥Ø¶Ø§ÙÙŠ Ø£Ùˆ ØªØ£Ø®ÙŠØ± ÙŠÙˆÙ…ÙŠ â€” ÙŠÙØ­Ø³Ø¨ ÙÙŠ Ù†Ù‡Ø§ÙŠØ© Ø§Ù„Ø´Ù‡Ø± ÙÙ‚Ø·
                    }

                    var notes = row.Cell(notesCol).Value.ToString().Trim();

                    // Check if weekend day (WeeklyDaysOff)
                    var weekendDays = (emp.WeeklyDaysOff ?? "Friday")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(d => d.Trim().ToLower())
                        .ToList();
                    
                    var dayNameAr = attendanceDate.ToString("dddd", new System.Globalization.CultureInfo("ar-EG")).ToLower();
                    var dayNameEn = attendanceDate.ToString("dddd", new System.Globalization.CultureInfo("en-US")).ToLower();

                    var isWeekend = weekendDays.Contains(dayNameEn) || weekendDays.Contains(dayNameAr);
                    if (isWeekend)
                    {
                        delayMinutes = 0;
                        if (isAbsent) continue; // ØªØ¬Ø§Ù‡Ù„ Ø£ÙŠØ§Ù… Ø§Ù„Ø¥Ø¬Ø§Ø²Ø© Ø§Ù„Ø£Ø³Ø¨ÙˆØ¹ÙŠØ©
                    }

                    // Save or Update
                    var attendance = await _db.EmployeeAttendances
                        .FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.Date == attendanceDate);

                    if (attendance == null)
                    {
                        attendance = new EmployeeAttendance
                        {
                            EmployeeId = emp.Id,
                            Date = attendanceDate,
                            CheckIn = checkIn,
                            CheckOut = checkOut,
                            WorkHours = workHours,
                            OvertimeHours = overtime,
                            DelayMinutes = delayMinutes,
                            IsAbsent = isAbsent,
                            Notes = notes,
                            CreatedByUserId = UserId,
                            CreatedAt = TimeHelper.GetEgyptTime()
                        };
                        _db.EmployeeAttendances.Add(attendance);
                        logsAdded++;
                    }
                    else
                    {
                        if (emp.AttendanceMode == AttendanceMode.MonthlyTotal)
                        {
                            // ÙˆØ¶Ø¹ Ø§Ù„Ø¥Ø¬Ù…Ø§Ù„ÙŠ Ø§Ù„Ø´Ù‡Ø±ÙŠ: Ù†Ø¬Ù…Ø¹ Ø§Ù„Ø³Ø§Ø¹Ø§Øª (ÙˆØ±Ø¯ÙŠØ© Ù…Ù‚Ø³Ù‘Ù…Ø© â€” Ø¯Ø®ÙˆÙ„ÙŠÙ† ÙÙŠ Ù†ÙØ³ Ø§Ù„ÙŠÙˆÙ…)
                            attendance.WorkHours += workHours;
                            if (checkIn.HasValue && (!attendance.CheckIn.HasValue || checkIn < attendance.CheckIn))
                                attendance.CheckIn = checkIn; // Ø£ÙˆÙ„ Ø¯Ø®ÙˆÙ„ ÙÙŠ Ø§Ù„ÙŠÙˆÙ…
                            if (checkOut.HasValue && (!attendance.CheckOut.HasValue || checkOut > attendance.CheckOut))
                                attendance.CheckOut = checkOut; // Ø¢Ø®Ø± Ø®Ø±ÙˆØ¬ ÙÙŠ Ø§Ù„ÙŠÙˆÙ…
                        }
                        else
                        {
                            // ÙˆØ¶Ø¹ Ø«Ø§Ø¨Øª/Ù…Ø±Ù†: Ù†Ø³ØªØ¨Ø¯Ù„
                            attendance.CheckIn = checkIn;
                            attendance.CheckOut = checkOut;
                            attendance.WorkHours = workHours;
                            attendance.OvertimeHours = overtime;
                            attendance.DelayMinutes = delayMinutes;
                        }
                        attendance.IsAbsent = isAbsent;
                        attendance.Notes = string.IsNullOrEmpty(notes) ? attendance.Notes : notes;
                        attendance.UpdatedAt = TimeHelper.GetEgyptTime();
                        logsUpdated++;
                    }

                }
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = $"ØªÙ… Ø±ÙØ¹ Ø§Ù„Ø­Ø¶ÙˆØ± Ø¨Ù†Ø¬Ø§Ø­. Ø§Ù„Ù…Ø¶Ø§Ù: {logsAdded}ØŒ Ø§Ù„Ù…Ø­Ø¯Ù‘Ø«: {logsUpdated}",
            addedCount = logsAdded,
            updatedCount = logsUpdated,
            warnings = warnings
        });
    }

    [HttpGet("devices")]
    [RequirePermission(ModuleKeys.HrAttendance)]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _db.ZkDevices
            .OrderBy(d => d.Name)
            .ToListAsync();
        return Ok(devices);
    }

    [HttpPost("devices")]
    [RequirePermission(ModuleKeys.HrAttendance, requireEdit: true)]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SerialNumber) || string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "Serial Number and Name are required." });
        }

        var exists = await _db.ZkDevices.AnyAsync(d => d.SerialNumber == dto.SerialNumber);
        if (exists)
        {
            return BadRequest(new { message = "A device with this Serial Number is already registered." });
        }

        var device = new ZkDevice
        {
            SerialNumber = dto.SerialNumber.Trim(),
            Name = dto.Name.Trim(),
            Notes = dto.Notes?.Trim(),
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _db.ZkDevices.Add(device);
        await _db.SaveChangesAsync();

        return Ok(device);
    }

    [HttpDelete("devices/{id}")]
    [RequirePermission(ModuleKeys.HrAttendance, requireEdit: true)]
    public async Task<IActionResult> DeleteDevice(int id)
    {
        var device = await _db.ZkDevices.FindAsync(id);
        if (device == null) return NotFound();

        _db.ZkDevices.Remove(device);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("sync-punch")]
    [RequirePermission(ModuleKeys.HrAttendance, requireEdit: true)]
    public async Task<IActionResult> SyncPunches([FromBody] List<SyncPunchDto> punches)
    {
        if (punches == null || !punches.Any())
        {
            return BadRequest(new { message = "No punches provided." });
        }

        var count = 0;
        foreach (var punch in punches)
        {
            try
            {
                if (!string.IsNullOrEmpty(punch.SerialNumber))
                {
                    var registered = await _db.ZkDevices.AnyAsync(d => d.SerialNumber == punch.SerialNumber);
                    if (!registered)
                    {
                        continue;
                    }
                }

                await _zkService.ProcessPunchAsync(punch.EmployeeNumber, punch.Timestamp, punch.SerialNumber);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing punch for employee {Emp}", punch.EmployeeNumber);
            }
        }

        return Ok(new { success = true, count = count, message = $"Successfully synced {count} punches." });
    }
}

