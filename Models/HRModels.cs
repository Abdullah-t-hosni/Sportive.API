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

public enum CommissionType
{
    PercentageOfSales = 1, // نسبة مئوية من المبيعات
    FixedAmountPerItem = 2, // مبلغ ثابت على كل قطعة
    TieredPercentage = 3,  // شرائح تصاعدية
    ProductSpecific = 4,     // مخصصة حسب المنتج
    TargetAchievementTiers = 5 // شرائح نسبة الإنجاز من المستهدف
}

public enum CommissionBasis
{
    NetSales = 1,  // صافي المبيعات (بعد الخصم والمرتجع)
    GrossSales = 2, // إجمالي المبيعات (قبل الخصم)
}

public enum AttendanceMode
{
    Fixed        = 1, // ثابت — بميعاد حضور يومي
    Flexible     = 2, // مرن — ساعات عمل يومية فقط
    MonthlyTotal = 3  // إجمالي شهري — مجموع ساعات الشهر (للوردية المقسّمة)
}

public class Department : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();

    // Hierarchy
    public int? ParentDepartmentId { get; set; }
    public Department? ParentDepartment { get; set; }
    public ICollection<Department> SubDepartments { get; set; } = new List<Department>();

    // Manager
    public int? ManagerEmployeeId { get; set; }
    public Employee? Manager { get; set; }

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

    // Payroll Configuration (Configurable per employee)
    public decimal WorkHoursPerDay         { get; set; } = 9;   // Default 9 hours
    public decimal OvertimeMultiplier      { get; set; } = 1.5m; // Default 1.5x
    public int     DaysPerMonth            { get; set; } = 26;  // Default 26 days

    public AttendanceMode AttendanceMode   { get; set; } = AttendanceMode.Fixed;
    public string         ShiftStartTime   { get; set; } = "09:00";
    public string         WeeklyDaysOff    { get; set; } = "Friday"; // Comma-separated list of days (e.g. "Friday,Saturday")

    // ─── إعدادات نظام الحضور الشهري المجمّع (MonthlyTotal) ───────────
    // عدد أيام الإجازة/العطلات المقررة في الشهر (تُخصم من DaysPerMonth)
    // المستهدف الشهري = (DaysPerMonth - MonthlyVacationDays) × WorkHoursPerDay
    public int MonthlyVacationDays { get; set; } = 0;

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

    // ربط الفرع
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public OrderSource? CostCenter { get; set; } // مركز التكلفة (موقع/محل/عام)

    public ICollection<PayrollItem>       PayrollItems { get; set; } = new List<PayrollItem>();
    public ICollection<EmployeeAdvance>   Advances     { get; set; } = new List<EmployeeAdvance>();
    public ICollection<EmployeeBonus>     Bonuses      { get; set; } = new List<EmployeeBonus>();
    public ICollection<EmployeeDeduction> Deductions   { get; set; } = new List<EmployeeDeduction>();
    public ICollection<EmployeeAttendance> Attendances   { get; set; } = new List<EmployeeAttendance>();

    public EmployeeCommissionSetting? CommissionSetting { get; set; }

    public int? CommissionGroupId { get; set; }
    public virtual CommissionGroup? CommissionGroup { get; set; }
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

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

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
    public decimal CommissionAmount        { get; set; } = 0;
    
    public int     AbsenceDays             { get; set; } = 0;
    public decimal AbsenceDeduction        { get; set; } = 0;

    public decimal OvertimeHours           { get; set; } = 0;
    public decimal OvertimeAmount          { get; set; } = 0;

    public decimal NetPayable => BasicSalary + TransportationAllowance + CommunicationAllowance + BonusAmount + FixedAllowance + OvertimeAmount + CommissionAmount - DeductionAmount - AdvanceDeducted - AbsenceDeduction;

    public string? Notes { get; set; }

    // Partial payment tracking
    public bool      IsPaid               { get; set; } = false;
    public decimal   PaidAmount           { get; set; } = 0;
    public DateTime? PaidAt               { get; set; }
    public int?      PaymentJournalEntryId { get; set; }
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

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
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

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
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

    // الحساب المدين عند تحصيل الخم (إن كان فورياً)
    public int?    CashAccountId { get; set; }
    public Account? CashAccount  { get; set; }

    public int?          JournalEntryId { get; set; }
    public JournalEntry? JournalEntry   { get; set; }

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
}

// ══════════════════════════════════════════════════════
// تبويبة العمولات — Employee Commission
// ══════════════════════════════════════════════════════

public class EmployeeCommissionSetting : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public CommissionType Type { get; set; } = CommissionType.PercentageOfSales;
    public CommissionBasis Basis { get; set; } = CommissionBasis.NetSales;

    public decimal DefaultRate { get; set; } = 0; // نسبة أو مبلغ ثابت
    public decimal TargetAmount { get; set; } = 0; // المستهدف لتفعيل العمولة

    public int? CommissionSchemeId { get; set; }
    public virtual CommissionScheme? CommissionScheme { get; set; }

    public ICollection<CommissionTier> Tiers { get; set; } = new List<CommissionTier>();
}

public class CommissionTier : BaseEntity
{
    public int SettingId { get; set; }
    public EmployeeCommissionSetting Setting { get; set; } = null!;

    public decimal MinAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public decimal Rate { get; set; } // النسبة أو المبلغ لهذه الشريحة
}

public class CommissionScheme : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public CommissionType Type { get; set; } = CommissionType.PercentageOfSales;
    public CommissionBasis Basis { get; set; } = CommissionBasis.NetSales;
    public decimal DefaultRate { get; set; } = 0;
    public decimal TargetAmount { get; set; } = 0;
    public virtual ICollection<CommissionSchemeTier> Tiers { get; set; } = new List<CommissionSchemeTier>();
}

public class CommissionSchemeTier : BaseEntity
{
    public int CommissionSchemeId { get; set; }
    public virtual CommissionScheme CommissionScheme { get; set; } = null!;

    public decimal MinAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public decimal Rate { get; set; }
}

public class CommissionGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    public CommissionType Type { get; set; } = CommissionType.PercentageOfSales;
    public CommissionBasis Basis { get; set; } = CommissionBasis.NetSales;
    
    public decimal DefaultRate { get; set; } = 0;
    public decimal TargetAmount { get; set; } = 0;
    
    public int? CommissionSchemeId { get; set; }
    public virtual CommissionScheme? CommissionScheme { get; set; }
    
    public virtual ICollection<Employee> Members { get; set; } = new List<Employee>();
    public virtual ICollection<CommissionGroupTier> Tiers { get; set; } = new List<CommissionGroupTier>();
}

public class CommissionGroupTier : BaseEntity
{
    public int CommissionGroupId { get; set; }
    public virtual CommissionGroup CommissionGroup { get; set; } = null!;
    
    public decimal MinAmount { get; set; }
    public decimal MaxAmount { get; set; }
    public decimal Rate { get; set; }
}

// ══════════════════════════════════════════════════════
// سجل الحضور والانصراف — Employee Attendance
// ══════════════════════════════════════════════════════
public class EmployeeAttendance : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    
    public DateTime Date { get; set; } // يوم العمل
    public DateTime? CheckIn { get; set; } // وقت الدخول الفعلي
    public DateTime? CheckOut { get; set; } // وقت الانصراف الفعلي
    
    public decimal WorkHours { get; set; } = 0; // ساعات العمل المحتسبة
    public decimal OvertimeHours { get; set; } = 0; // العمل الإضافي المحتسب
    public decimal DelayMinutes { get; set; } = 0; // التأخير بالدقائق
    public bool IsAbsent { get; set; } = false; // غياب
    
    public string? Notes { get; set; }
    public string? CreatedByUserId { get; set; }
}

// ══════════════════════════════════════════════════════
// تبويبة المهام والمسؤوليات — Employee Tasks & Responsibilities
// ══════════════════════════════════════════════════════

public class ResponsibilityType : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Code { get; set; } = string.Empty; // e.g., INVENTORY, CLEANING, SALES
    public bool IsActive { get; set; } = true;
}

public enum EmployeeTaskStatus
{
    Pending = 1,      // قيد الانتظار
    InProgress = 2,   // جاري العمل
    Submitted = 3,    // تم التقديم (بانتظار الاعتماد)
    Approved = 4,     // معتمدة (تم التقييم)
    Rejected = 5,     // مرفوضة
    Rework = 6        // إعادة عمل
}

public class EmployeeTask : BaseEntity
{
    public int EmployeeId { get; set; }
    public virtual Employee? Employee { get; set; }

    public int ResponsibilityTypeId { get; set; }
    public virtual ResponsibilityType? ResponsibilityType { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DateTime TaskDate { get; set; }
    public DateTime? DueDate { get; set; }
    
    public EmployeeTaskStatus Status { get; set; } = EmployeeTaskStatus.Pending;

    // Targets and Evaluation
    public decimal TargetQuantity { get; set; } = 1;
    public decimal CompletedQuantity { get; set; } = 0;

    // Potential Bonus / Deduction
    public decimal MaxBonusAmount { get; set; } = 0;
    public decimal MaxDeductionAmount { get; set; } = 0;

    // Actual Calculated amounts after evaluation
    public decimal ActualBonusAmount { get; set; } = 0;
    public decimal ActualDeductionAmount { get; set; } = 0;

    // Filters or configurations in JSON (e.g., {"CategoryId": 5, "Status": "Active"})
    public string? CriteriaJson { get; set; }

    public string? ManagerNotes { get; set; }
    public string? EmployeeNotes { get; set; }

    public string? CreatedByUserId { get; set; }

    // Navigation properties for linked financial impact
    public int? EmployeeBonusId { get; set; }
    public virtual EmployeeBonus? EmployeeBonus { get; set; }

    public int? EmployeeDeductionId { get; set; }
    public virtual EmployeeDeduction? EmployeeDeduction { get; set; }

    public virtual ICollection<EmployeeTaskItem> Items { get; set; } = new List<EmployeeTaskItem>();
}

public class EmployeeTaskItem : BaseEntity
{
    public int EmployeeTaskId { get; set; }
    public virtual EmployeeTask EmployeeTask { get; set; } = null!;

    public int? ProductId { get; set; }
    // Avoid strong foreign key to avoid circular/cascade issues if not needed, or add it if context allows:
    // public virtual Product? Product { get; set; }

    public string? ItemName { get; set; } // If not linked to a specific product (e.g. Checklist item)

    public decimal ExpectedQuantity { get; set; } = 0;
    public decimal ActualQuantity { get; set; } = 0;

    public bool IsCompleted { get; set; } = false;
    public string? Notes { get; set; }
}
