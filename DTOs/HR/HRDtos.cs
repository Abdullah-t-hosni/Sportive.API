using Sportive.API.Models;

namespace Sportive.API.DTOs;

// ══════════════════════════════════════════════════════
// DEPARTMENT DTOs
// ══════════════════════════════════════════════════════

public record DepartmentDto(
    int Id, 
    string Name, 
    string? Description, 
    int EmployeeCount, 
    decimal WorkHoursPerDay, 
    decimal OvertimeMultiplier, 
    int DaysPerMonth,
    int? ParentDepartmentId = null,
    string? ParentDepartmentName = null,
    int? ManagerEmployeeId = null,
    string? ManagerName = null
);

public record CreateDepartmentDto(
    string Name, 
    string? Description, 
    decimal WorkHoursPerDay = 9, 
    decimal OvertimeMultiplier = 1.5m, 
    int DaysPerMonth = 26,
    int? ParentDepartmentId = null,
    int? ManagerEmployeeId = null
);

// ══════════════════════════════════════════════════════
// EMPLOYEE DTOs
// ══════════════════════════════════════════════════════

public record CreateEmployeeDto(
    string        Name,
    DateTime      HireDate,
    decimal       BaseSalary,
    decimal       TransportationAllowance = 0,
    decimal       CommunicationAllowance  = 0,
    decimal       BonusAmount             = 0,
    decimal       FixedAllowance          = 0,
    string?       Phone              = null,
    string?       Email              = null,
    string?       NationalId         = null,
    string?       JobTitle           = null,
    int?          DepartmentId       = null,
    string?       BankAccount        = null,
    string?       Notes              = null,
    string?       AttachmentUrl      = null,
    string?       AttachmentPublicId = null,
    string?       AppUserId          = null,
    OrderSource?  CostCenter         = null,
    decimal       WorkHoursPerDay    = 9,
    decimal       OvertimeMultiplier = 1.5m,
    int           DaysPerMonth       = 26,
    AttendanceMode AttendanceMode    = AttendanceMode.Fixed,
    string        ShiftStartTime     = "09:00",
    string        WeeklyDaysOff      = "Friday"
);

public record UpdateEmployeeDto(
    string        Name,
    DateTime      HireDate,
    decimal       BaseSalary,
    EmployeeStatus Status,
    decimal       TransportationAllowance = 0,
    decimal       CommunicationAllowance  = 0,
    decimal       BonusAmount             = 0,
    decimal       FixedAllowance          = 0,
    string?       Phone              = null,
    string?       Email              = null,
    string?       NationalId         = null,
    string?       JobTitle           = null,
    int?          DepartmentId       = null,
    string?       BankAccount        = null,
    DateTime?     TerminationDate    = null,
    string?       Notes              = null,
    string?       AttachmentUrl      = null,
    string?       AttachmentPublicId = null,
    OrderSource?  CostCenter         = null,
    decimal       WorkHoursPerDay    = 9,
    decimal       OvertimeMultiplier = 1.5m,
    int           DaysPerMonth       = 26,
    AttendanceMode AttendanceMode    = AttendanceMode.Fixed,
    string        ShiftStartTime     = "09:00",
    string        WeeklyDaysOff      = "Friday"
);

public record EmployeeDto(
    int            Id,
    string         EmployeeNumber,
    string         Name,
    string?        Phone,
    string?        Email,
    string?        NationalId,
    string?        JobTitle,
    int?           DepartmentId,
    string?        DepartmentName,
    DateTime       HireDate,
    DateTime?      TerminationDate,
    decimal        BaseSalary,
    decimal        TransportationAllowance,
    decimal        CommunicationAllowance,
    decimal        BonusAmount,
    decimal        FixedAllowance,
    string?        BankAccount,
    int            Status,
    string?        Notes,
    string?        AttachmentUrl,
    string?        AttachmentPublicId,
    DateTime       CreatedAt,
    string?        AppUserId    = null,
    string?        AppUserName  = null,
    OrderSource?   CostCenter   = null,
    decimal        WorkHoursPerDay = 9,
    decimal        OvertimeMultiplier = 1.5m,
    int            DaysPerMonth = 26,
    AttendanceMode AttendanceMode  = AttendanceMode.Fixed,
    string         ShiftStartTime  = "09:00",
    string         WeeklyDaysOff   = "Friday"
);

public record EmployeeBasicDto(int Id, string EmployeeNumber, string Name, string? JobTitle, int? DepartmentId, string? DepartmentName, decimal BaseSalary, decimal TransportationAllowance, decimal CommunicationAllowance, decimal BonusAmount, decimal FixedAllowance, decimal PendingAdvancesAmount, decimal PendingBonusesAmount, decimal PendingDeductionsAmount, int Status, decimal WorkHoursPerDay, decimal OvertimeMultiplier, int DaysPerMonth);

// ══════════════════════════════════════════════════════
// PAYROLL RUN DTOs
// ══════════════════════════════════════════════════════

public record CreatePayrollRunDto(
    int     PeriodYear,
    int     PeriodMonth,
    List<CreatePayrollItemDto> Items,
    string? Notes                      = null,
    int?    WagesExpenseAccountId      = null,
    int?    AccruedSalariesAccountId   = null,
    int?    DeductionRevenueAccountId  = null,
    int?    AdvancesAccountId          = null
);

public record PayPayrollDto(
    int      CashAccountId,  // الخزنة/البنك
    DateTime PaymentDate,
    string?  Notes = null
);

public record CreatePayrollItemDto(
    int      EmployeeId,
    decimal? OverrideBasicSalary = null,
    decimal  TransportationAllowance = 0,
    decimal  CommunicationAllowance  = 0,
    decimal  BonusAmount             = 0,
    decimal  FixedAllowance          = 0,
    decimal  DeductionAmount         = 0,
    decimal  AdvanceDeducted         = 0,
    int      AbsenceDays             = 0,
    decimal  AbsenceDeduction        = 0,
    decimal  OvertimeHours           = 0,
    decimal  OvertimeAmount          = 0,
    string?  Notes               = null,
    decimal? CommissionAmount    = null
);

public record PayrollRunDto(
    int           Id,
    string        PayrollNumber,
    int           PeriodYear,
    int           PeriodMonth,
    decimal       TotalBasicSalary,
    decimal       TotalTransportation,
    decimal       TotalCommunication,
    decimal       TotalBonuses,
    decimal       TotalFixedAllowances,
    decimal       TotalOvertimeAmount,
    decimal       TotalDeductions,
    decimal       TotalAdvancesDeducted,
    decimal       TotalNetPayable,
    int           Status,
    string?       Notes,
    int?          JournalEntryId,
    int?          PaymentJournalEntryId,
    DateTime      CreatedAt,
    List<PayrollItemDto> Items
);

public record PayrollItemDto(
    int     Id,
    int     EmployeeId,
    string  EmployeeName,
    string  EmployeeNumber,
    string? JobTitle,
    decimal BasicSalary,
    decimal TransportationAllowance,
    decimal CommunicationAllowance,
    decimal BonusAmount,
    decimal FixedAllowance,
    decimal DeductionAmount,
    decimal AdvanceDeducted,
    int     AbsenceDays,
    decimal AbsenceDeduction,
    decimal OvertimeHours,
    decimal OvertimeAmount,
    decimal CommissionAmount,
    decimal NetPayable,
    string? Notes
);

public record PayrollRunSummaryDto(
    int           Id,
    string        PayrollNumber,
    int           PeriodYear,
    int           PeriodMonth,
    decimal       TotalNetPayable,
    int           EmployeeCount,
    int           Status,
    int?          JournalEntryId,
    int?          PaymentJournalEntryId,
    DateTime      CreatedAt
);

// ══════════════════════════════════════════════════════
// ADVANCE DTOs
// ══════════════════════════════════════════════════════

public record CreateAdvanceDto(
    int      EmployeeId,
    DateTime AdvanceDate,
    decimal  Amount,
    string?  Reason        = null,
    string?  Notes         = null,
    int?     CashAccountId = null,   // حساب الخزينة/البنك عند الصرف
    OrderSource? CostCenter = null
);

public record EmployeeAdvanceDto(
    int            Id,
    string         AdvanceNumber,
    int            EmployeeId,
    string         EmployeeName,
    DateTime       AdvanceDate,
    decimal        Amount,
    decimal        DeductedAmount,
    decimal        RemainingAmount,
    int            Status,
    string?        Reason,
    string?        Notes,
    int?           CashAccountId,
    string?        CashAccountName,
    int?           JournalEntryId,
    DateTime       CreatedAt,
    OrderSource?   CostCenter = null
);

// ══════════════════════════════════════════════════════
// BONUS DTOs
// ══════════════════════════════════════════════════════

public record CreateBonusDto(
    int       EmployeeId,
    DateTime  BonusDate,
    decimal   Amount,
    BonusType BonusType     = BonusType.Other,
    string?   Reason        = null,
    string?   Notes         = null,
    int?      CashAccountId = null,
    OrderSource? BonusCostCenter = null
);

public record EmployeeBonusDto(
    int       Id,
    string    BonusNumber,
    int       EmployeeId,
    string    EmployeeName,
    DateTime  BonusDate,
    decimal   Amount,
    int       BonusType,
    string?   Reason,
    string?   Notes,
    int?      PayrollRunId,
    int?      CashAccountId,
    string?   CashAccountName,
    int?      JournalEntryId,
    DateTime  CreatedAt,
    OrderSource? CostCenter = null
);

// ══════════════════════════════════════════════════════
// DEDUCTION DTOs
// ══════════════════════════════════════════════════════

public record CreateDeductionDto(
    int           EmployeeId,
    DateTime      DeductionDate,
    decimal       Amount,
    DeductionType DeductionType = DeductionType.Other,
    string?       Reason        = null,
    string?       Notes         = null,
    int?          CashAccountId = null,
    OrderSource?  CostCenter    = null
);

public record EmployeeDeductionDto(
    int           Id,
    string        DeductionNumber,
    int           EmployeeId,
    string        EmployeeName,
    DateTime      DeductionDate,
    decimal       Amount,
    int           DeductionType,
    string?       Reason,
    string?       Notes,
    int?          PayrollRunId,
    int?          CashAccountId,
    string?       CashAccountName,
    int?          JournalEntryId,
    DateTime      CreatedAt,
    OrderSource?  CostCenter = null
);

// ══════════════════════════════════════════════════════
// EMPLOYEE STATEMENT DTO
// ══════════════════════════════════════════════════════

public record EmployeeStatementRowDto(
    DateTime Date,
    string   Reference,
    string   Type,
    string   Description,
    decimal  Debit,    // مبالغ على الموظف (سلف مستحقة)
    decimal  Credit,   // مبالغ للموظف (رواتب)
    decimal  Balance,
    int?     PeriodYear    = null,
    int?     PeriodMonth   = null,
    string?  PayrollNumber = null,
    decimal? NetPayable    = null
);

public record EmployeeStatementDto(
    int     EmployeeId,
    string  EmployeeName,
    string  EmployeeNumber,
    string? JobTitle,
    string? AccountName,
    DateTime From,
    DateTime To,
    decimal OpeningBalance,
    List<EmployeeStatementRowDto> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal ClosingBalance
);

// ══════════════════════════════════════════════════════
// COMMISSION DTOs
// ══════════════════════════════════════════════════════

public record LinkUserDto(string? AppUserId);
public record CommissionSettingDto(int Id, int EmployeeId, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, List<CommissionTierDto> Tiers);
public record CommissionTierDto(int Id, decimal MinAmount, decimal MaxAmount, decimal Rate);
public record UpdateCommissionSettingDto(CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, int? CommissionSchemeId, List<CreateCommissionTierDto> Tiers);
public record CreateCommissionTierDto(decimal MinAmount, decimal MaxAmount, decimal Rate);
public record CommissionSchemeDto(int Id, string Name, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, List<CommissionTierDto> Tiers);
public record UpdateCommissionSchemeDto(string Name, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, List<CreateCommissionTierDto> Tiers);
public record EmployeeCommissionSummaryDto(int EmployeeId, string Name, string? JobTitle, int? CommissionSchemeId, CommissionType Type, CommissionBasis Basis, decimal DefaultRate, decimal TargetAmount, decimal TotalSales, decimal EarnedCommission, int? DepartmentId = null, string? DepartmentName = null, bool IsGroup = false, string? GroupName = null);

public record CommissionGroupDto(int Id, string Name, string? Description, int? CommissionSchemeId, List<int> MemberIds);
public record CreateCommissionGroupDto(string Name, string? Description, int? CommissionSchemeId, List<int> MemberIds);

// ══════════════════════════════════════════════════════
// ATTENDANCE DTOs
// ══════════════════════════════════════════════════════
public record EmployeeAttendanceDto(
    int Id,
    int EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    DateTime Date,
    DateTime? CheckIn,
    DateTime? CheckOut,
    decimal WorkHours,
    decimal OvertimeHours,
    decimal DelayMinutes,
    bool IsAbsent,
    string? Notes,
    string? CreatedByUserId,
    DateTime CreatedAt
);

public record CreateAttendanceDto(
    int EmployeeId,
    DateTime Date,
    DateTime? CheckIn,
    DateTime? CheckOut,
    decimal WorkHours,
    decimal OvertimeHours,
    decimal DelayMinutes,
    bool IsAbsent,
    string? Notes
);

public record UpdateAttendanceDto(
    DateTime? CheckIn,
    DateTime? CheckOut,
    decimal WorkHours,
    decimal OvertimeHours,
    decimal DelayMinutes,
    bool IsAbsent,
    string? Notes
);

public record RegisterDeviceDto(
    string SerialNumber,
    string Name,
    string? Notes = null
);

public record SyncPunchDto(
    string EmployeeNumber,
    DateTime Timestamp,
    string? SerialNumber = null
);

