using Sportive.API.Models;

namespace Sportive.API.DTOs;

// ══════════════════════════════════════════════════════
// DEPARTMENT DTOs
// ══════════════════════════════════════════════════════

public record DepartmentDto(int Id, string Name, string? Description, int EmployeeCount);
public record CreateDepartmentDto(string Name, string? Description);

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
    string?       AppUserId          = null   // ربط اختياري بحساب النظام
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
    string?       AttachmentPublicId = null
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
    string?        AppUserName  = null
);

public record EmployeeBasicDto(int Id, string EmployeeNumber, string Name, string? JobTitle, int? DepartmentId, string? DepartmentName, decimal BaseSalary, decimal TransportationAllowance, decimal CommunicationAllowance, decimal BonusAmount, decimal FixedAllowance, decimal PendingAdvancesAmount, decimal PendingBonusesAmount, decimal PendingDeductionsAmount, int Status);

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

public record CreatePayrollItemDto(
    int      EmployeeId,
    decimal? OverrideBasicSalary = null,  // لو null يأخذ راتب الموظف
    decimal  TransportationAllowance = 0,
    decimal  CommunicationAllowance  = 0,
    decimal  BonusAmount             = 0,
    decimal  FixedAllowance          = 0,
    decimal  DeductionAmount         = 0,
    decimal  AdvanceDeducted         = 0,
    string?  Notes               = null
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
    decimal       TotalDeductions,
    decimal       TotalAdvancesDeducted,
    decimal       TotalNetPayable,
    int           Status,
    string?       Notes,
    int?          JournalEntryId,
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
    int?     CashAccountId = null   // حساب الخزينة/البنك عند الصرف
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
    DateTime       CreatedAt
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
    int?      CashAccountId = null
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
    DateTime  CreatedAt
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
    int?          CashAccountId = null
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
    DateTime      CreatedAt
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
