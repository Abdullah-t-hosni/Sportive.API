using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services.HR
{
    public class ZKDeviceService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ZKDeviceService> _logger;

        public ZKDeviceService(AppDbContext db, ILogger<ZKDeviceService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Processes a raw punch log from a fingerprint machine.
        /// </summary>
        public async Task ProcessPunchAsync(string pin, DateTime timestamp, string? serialNumber)
        {
            if (string.IsNullOrWhiteSpace(pin))
            {
                _logger.LogWarning("Received empty PIN from device SN: {SN}", serialNumber);
                return;
            }

            var normalizedPin = pin.TrimStart('0');
            
            // Match employee by absolute match, normalized numeric code, or suffixes (e.g., EMP-0012 matching 12)
            var emp = await _db.Employees.FirstOrDefaultAsync(e => 
                e.EmployeeNumber.ToLower() == pin.ToLower() ||
                (!string.IsNullOrEmpty(normalizedPin) && e.EmployeeNumber.ToLower() == normalizedPin.ToLower()) ||
                e.EmployeeNumber.EndsWith("-" + pin) ||
                (!string.IsNullOrEmpty(normalizedPin) && e.EmployeeNumber.EndsWith("-" + normalizedPin))
            );

            if (emp == null)
            {
                _logger.LogWarning("No employee matches PIN: {PIN} (normalized: {Normalized}) from device SN: {SN}", pin, normalizedPin, serialNumber);
                return;
            }

            var date = timestamp.Date;

            // We look for a recent attendance record that is either:
            // 1. Matches this exact date.
            // 2. Or is an open check-in (no check-out) that started within 16 hours (overnight shifts).
            var attendance = await _db.EmployeeAttendances
                .Where(a => a.EmployeeId == emp.Id)
                .OrderByDescending(a => a.Date)
                .FirstOrDefaultAsync();

            if (attendance != null && 
                (attendance.Date == date || 
                 (attendance.CheckIn.HasValue && !attendance.CheckOut.HasValue && (timestamp - attendance.CheckIn.Value).TotalHours < 16)))
            {
                // Update existing record
                if (!attendance.CheckIn.HasValue || timestamp < attendance.CheckIn.Value)
                {
                    attendance.CheckIn = timestamp;
                }
                else if (!attendance.CheckOut.HasValue || timestamp > attendance.CheckOut.Value)
                {
                    attendance.CheckOut = timestamp;
                }

                RecalculateAttendanceMetrics(attendance, emp);
                attendance.UpdatedAt = TimeHelper.GetEgyptTime();
                _logger.LogInformation("Updated attendance record ID {Id} for Employee {Emp} on {Date} (In: {In}, Out: {Out})", 
                    attendance.Id, emp.Name, attendance.Date.ToShortDateString(), attendance.CheckIn, attendance.CheckOut);
            }
            else
            {
                // Create new record
                var newAttendance = new EmployeeAttendance
                {
                    EmployeeId = emp.Id,
                    Date = date,
                    CheckIn = timestamp,
                    IsAbsent = false,
                    Notes = $"بصمة تلقائية من جهاز: {serialNumber ?? "غير معروف"}",
                    CreatedAt = TimeHelper.GetEgyptTime()
                };

                RecalculateAttendanceMetrics(newAttendance, emp);
                _db.EmployeeAttendances.Add(newAttendance);
                _logger.LogInformation("Created new attendance record for Employee {Emp} on {Date} (In: {In})", 
                    emp.Name, date.ToShortDateString(), timestamp);
            }

            await _db.SaveChangesAsync();
        }

        private void RecalculateAttendanceMetrics(EmployeeAttendance attendance, Employee emp)
        {
            if (attendance.IsAbsent)
            {
                attendance.WorkHours = 0;
                attendance.OvertimeHours = 0;
                attendance.DelayMinutes = 0;
                return;
            }

            if (attendance.CheckIn.HasValue && attendance.CheckOut.HasValue)
            {
                attendance.WorkHours = (decimal)(attendance.CheckOut.Value - attendance.CheckIn.Value).TotalHours;
                
                // Overtime
                var stdHours = emp.WorkHoursPerDay;
                if (attendance.WorkHours > stdHours)
                {
                    attendance.OvertimeHours = attendance.WorkHours - stdHours;
                }
                else
                {
                    attendance.OvertimeHours = 0;
                }
            }
            else
            {
                attendance.WorkHours = 0;
                attendance.OvertimeHours = 0;
            }

            // Delay minutes calculation (only in Fixed Shift mode, comparing CheckIn to shift start)
            if (attendance.CheckIn.HasValue)
            {
                // Verify if it is weekend day - weekends should have 0 delay
                var weekendDays = (emp.WeeklyDaysOff ?? "Friday")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => d.Trim().ToLower())
                    .ToList();
                
                var dayNameAr = attendance.Date.ToString("dddd", new System.Globalization.CultureInfo("ar-EG")).ToLower();
                var dayNameEn = attendance.Date.ToString("dddd", new System.Globalization.CultureInfo("en-US")).ToLower();
                var isWeekend = weekendDays.Contains(dayNameEn) || weekendDays.Contains(dayNameAr);

                if (isWeekend)
                {
                    attendance.DelayMinutes = 0;
                }
                else if (emp.AttendanceMode == AttendanceMode.Fixed && !string.IsNullOrEmpty(emp.ShiftStartTime))
                {
                    if (TimeSpan.TryParse(emp.ShiftStartTime, out var shiftStart))
                    {
                        var stdCheckIn = attendance.Date.Add(shiftStart);
                        if (attendance.CheckIn.Value > stdCheckIn)
                        {
                            var diff = (attendance.CheckIn.Value - stdCheckIn).TotalMinutes;
                            attendance.DelayMinutes = diff > 0 ? (decimal)diff : 0;
                        }
                        else
                        {
                            attendance.DelayMinutes = 0;
                        }
                    }
                }
                else
                {
                    attendance.DelayMinutes = 0;
                }
            }
        }
    }
}
