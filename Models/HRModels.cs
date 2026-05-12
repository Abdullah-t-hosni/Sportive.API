namespace Sportive.API.Models;

// ══════════════════════════════════════════════════════
// نظام الموارد البشرية والرواتب — HR & Payroll Models
// ══════════════════════════════════════════════════════

// ─── Enums ────────────────────────────────────────────

public enum EmployeeStatus
{
    Active     = 1,  // نشط
    Inactive   = 2,  // غير نشط
    Terminated = 3,  // منتهي الخدمة
}

public enum PayrollStatus
{
    Draft  = 1,  // مسودة
    Posted = 2,  // مرحّل
    Paid   = 3,  // مسدد
}

public enum AdvanceStatus
{
    Pending            = 1,  // لم يُخصم بعد
    FullyDeducted      = 2,  // مخصوم بالكامل
    PartiallyDeducted  = 3,  // مخصوم جزئياً
}

public enum DeductionType
{
    Absence    = 1,  // غياب
    Delay      = 2,  // تأخير
    Penalty    = 3,  // جزاء
    Other      = 4,  // أخرى
}

public enum BonusType
{
    Performance = 1,  // أداء
    Annual      = 2,  // سنوي
    Holiday     = 3,  // عيد / مناسبة
    Other       = 4,  // أخرى
}

public class Department : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();

    // Default Payroll Configuration for this department
    public decimal WorkHoursPerDay         { get; set; } = 9;
    public decimal OvertimeMultiplier      { get; set; } = 1.5m;
    public int     DaysPerMonth            { get; set; } = 26;
}

// ══════════════════════════════════════════════════════
// تبويبة الموظفين — Employee
// ══════════════════════════════════════════════════════

public class Employee : BaseEntity
{
    public string  EmployeeNumber  { get; set; } = string.Empty; // EMP-0001
    public string  Name            { get; set; } = string.Empty;
    public string? Phone           { get; set; }
    public string? Email           { get; set; }
    public string? NationalId      { get; set; }
    public string? JobTitle        { get; set; }  // المسمى الوظيفي
    
    // القسم (كفئة)
    public int?    DepartmentId    { get; set; }
    public Department? Department  { get; set; }

    public DateTime HireDate       { get; set; }
    public DateTime? TerminationDate { get; set; }
    
    public decimal BaseSalary              { get; set; } = 0;
    public decimal TransportationAllowance { get; set; } = 0;
    public decimal CommunicationAllowance  { get; set; } = 0;
    public decimal BonusAmount             { get; set; } = 0;
    public decimal FixedAllowance          { get; set; } = 0;
    public decimal FixedDeduction          { get; set; } = 0;

    // Payroll Configuration (Configurable per employee)
    public decimal WorkHoursPerDay         { get; set; } = 9;   // Default 9 hours
    public decimal OvertimeMultiplier      { get; set; } = 1.5m; // Default 1.5x
    public int     DaysPerMonth            { get; set; } = 26;  // Default 26 days

    public string? BankAccount     { get; set; }  // رقم الحساب البنكي
    public string? Notes           { get; set; }
    public string? AttachmentUrl   { get; set; }
    public string? AttachmentPublicId { get; set; }
    public EmployeeStatus Status   { get; set; } = EmployeeStatus.Active;
    public string? CreatedByUserId { get; set; }

    // ربط بحساب النظام (AppUser) — اختياري، للموظفين اللي عندهم login
    public string? AppUserId { get; set; }
    public AppUser? AppUser  { get; set; }

    // ربط محاسبي — حساب الموظف في دليل الحسابات (للكشف)
    public int?    AccountId { get; set; }
    public Account? Account  { get; set; }

    public OrderSource? CostCenter { get; set; } // مركز التكلفة (موقع/محل/عام)

    public ICollection<PayrollItem>       PayrollItems { get; set; } = new List<PayrollItem>();
    public ICollection<EmployeeAdvance>   Advances     { get; set; } = new List<EmployeeAdvance>();
    public ICollection<EmployeeBonus>     Bonuses      { get; set; } = new List<EmployeeBonus>();
    public ICollection<EmployeeDeduction> Deductions   { get; set; } = new List<EmployeeDeduction>();
}

// ══════════════════════════════════════════════════════
// تبويبة مسير الرواتب — Payroll Run
// ══════════════════════════════════════════════════════

public class PayrollRun : BaseEntity
{
    public string  PayrollNumber    { get; set; } = string.Empty; // PAY-2604-0001
    public int     PeriodYear       { get; set; }
    public int     PeriodMonth      { get; set; }

    // ملخص مالي (يُحسب عند الترحيل)
    public decimal TotalBasicSalary          { get; set; } = 0;
    public decimal TotalTransportation       { get; set; } = 0;
    public decimal TotalCommunication        { get; set; } = 0;
    public decimal TotalBonuses              { get; set; } = 0;
    public decimal TotalFixedAllowances      { get; set; } = 0;
    public decimal TotalOvertimeAmount       { get; set; } = 0;
    public decimal TotalDeductions           { get; set; } = 0;
    public decimal TotalAbsenceDeduction     { get; set; } = 0;
    public decimal TotalAdvancesDeducted     { get; set; } = 0;
    public decimal TotalNetPayable           { get; set; } = 0;  // = Basic + Trans + Comm + Bonuses + FixedAllowance + Overtime - TotalDeductions - TotalAbsenceDeduction - Advances

    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;
    public string?  Notes           { get; set; }
    public string?  CreatedByUserId { get; set; }

    // الربط المحاسبي على مستوى المسير (يلغي افتراضيات النظام)
    public int?    WagesExpenseAccountId    { get; set; } // مدين: رواتب وأجور
    public Account? WagesExpenseAccount    { get; set; }
    public int?    AccruedSalariesAccountId { get; set; } // دائن: رواتب مستحقة
    public Account? AccruedSalariesAccount { get; set; }
    public int?    DeductionRevenueAccountId { get; set; } // دائن: إيرادات الخصومات
    public Account? DeductionRevenueAccount { get; set; }
    public int?    AdvancesAccountId        { get; set; } // دائن: سلف الموظفين
    public Account? AdvancesAccount         { get; set; }

    public int?          JournalEntryId { get; set; }
    public JournalEntry? JournalEntry   { get; set; }

    public int?          PaymentJournalEntryId { get; set; }
    public JournalEntry? PaymentJournalEntry   { get; set; }

    public ICollection<PayrollItem> Items { get; set; } = new List<PayrollItem>();
}

public class PayrollItem : BaseEntity
{
    public int        PayrollRunId { get; set; }
    public PayrollRun PayrollRun   { get; set; } = null!;

    public int      EmployeeId   { get; set; }
    public Employee Employee     { get; set; } = null!;

    public decimal BasicSalary             { get; set; } = 0;
    public decimal TransportationAllowance { get; set; } = 0;
    public decimal CommunicationAllowance  { get; set; } = 0;
    public decimal BonusAmount             { get; set; } = 0;
    public decimal FixedAllowance          { get; set; } = 0;
    public decimal DeductionAmount         { get; set; } = 0;
    public decimal AdvanceDeducted         { get; set; } = 0;
    
    public int     AbsenceDays             { get; set; } = 0;
    public decimal AbsenceDeduction        { get; set; } = 0;

    public decimal OvertimeHours           { get; set; } = 0;
    public decimal OvertimeAmount          { get; set; } = 0;

    public decimal NetPayable => BasicSalary + TransportationAllowance + CommunicationAllowance + BonusAmount + FixedAllowance + OvertimeAmount - DeductionAmount - AdvanceDeducted - AbsenceDeduction;

    public string? Notes { get; set; }
}

// ══════════════════════════════════════════════════════
// تبويبة السلف — Employee Advance
// ══════════════════════════════════════════════════════

public class EmployeeAdvance : BaseEntity
{
    public string  AdvanceNumber    { get; set; } = string.Empty; // ADV-2604-0001
    public int     EmployeeId       { get; set; }
    public Employee Employee        { get; set; } = null!;

    public DateTime AdvanceDate     { get; set; }
    public decimal  Amount          { get; set; } = 0;
    public decimal  DeductedAmount  { get; set; } = 0;  // ما تم خصمه حتى الآن
    public decimal  RemainingAmount => Amount - DeductedAmount;

    public AdvanceStatus Status     { get; set; } = AdvanceStatus.Pending;
    public string?  Reason          { get; set; }
    public string?  Notes           { get; set; }
    public string?  CreatedByUserId { get; set; }
    public OrderSource? CostCenter  { get; set; } // مركز التكلفة

    // الحساب الدائن عند صرف السلفة (خزينة / بنك)
    public int?    CashAccountId { get; set; }
    public Account? CashAccount  { get; set; }

    public int?          JournalEntryId { get; set; }
    public JournalEntry? JournalEntry   { get; set; }
}

// ══════════════════════════════════════════════════════
// تبويبة المكافآت — Employee Bonus
// ══════════════════════════════════════════════════════

public class EmployeeBonus : BaseEntity
{
    public string   BonusNumber     { get; set; } = string.Empty; // BON-2604-0001
    public int      EmployeeId      { get; set; }
    public Employee Employee        { get; set; } = null!;

    public DateTime BonusDate       { get; set; }
    public decimal  Amount          { get; set; } = 0;
    public BonusType BonusType      { get; set; } = BonusType.Other;
    public string?  Reason          { get; set; }
    public string?  Notes           { get; set; }
    public string?  CreatedByUserId { get; set; }
    public OrderSource? CostCenter  { get; set; } // مركز التكلفة

    // مرتبط بمسير رواتب (اختياري)
    public int?        PayrollRunId { get; set; }
    public PayrollRun? PayrollRun   { get; set; }
    // الحساب الدائن عند صرف المكافأة (إن كانت فورية)
    public int?    CashAccountId { get; set; }
    public Account? CashAccount  { get; set; }

    public int?          JournalEntryId { get; set; }
    public JournalEntry? JournalEntry   { get; set; }
}

// ══════════════════════════════════════════════════════
// تبويبة الخصومات — Employee Deduction
// ══════════════════════════════════════════════════════

public class EmployeeDeduction : BaseEntity
{
    public string   DeductionNumber { get; set; } = string.Empty; // DED-2604-0001
    public int      EmployeeId      { get; set; }
    public Employee Employee        { get; set; } = null!;

    public DateTime DeductionDate   { get; set; }
    public decimal  Amount          { get; set; } = 0;
    public DeductionType DeductionType { get; set; } = DeductionType.Other;
    public string?  Reason          { get; set; }
    public string?  Notes           { get; set; }
    public string?  CreatedByUserId { get; set; }
    public OrderSource? CostCenter  { get; set; } // مركز التكلفة

    // مرتبط بمسير رواتب (اختياري)
    public int?        PayrollRunId { get; set; }
    public PayrollRun? PayrollRun   { get; set; }

    // الحساب المدين عند تحصيل الخصم (إن كان فورياً)
    public int?    CashAccountId { get; set; }
    public Account? CashAccount  { get; set; }

    public int?          JournalEntryId { get; set; }
    public JournalEntry? JournalEntry   { get; set; }
}
