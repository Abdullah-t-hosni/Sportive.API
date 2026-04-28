namespace Sportive.API.Utils;

/// <summary>
/// مفاتيح الربط المحاسبي الثابتة — تُستخدم في جدول AccountSystemMappings
/// الهدف: القضاء على Magic Strings وتفادي أخطاء الكتابة (Typos)
/// </summary>
public static class MappingKeys
{
    // ── مبيعات ──────────────────────────────────────────
    public const string Sales             = "salesAccountID";
    public const string SalesDiscount     = "salesDiscountAccountID";
    public const string SalesReturn       = "salesReturnAccountID";
    public const string Customer          = "customerAccountID";
    public const string DeliveryRevenue   = "webDeliveryRevenueAccountID";

    // ── مشتريات ─────────────────────────────────────────
    public const string Purchase          = "purchaseAccountID";
    public const string Supplier          = "supplierAccountID";
    public const string PurchaseDiscount  = "purchaseDiscountAccountID";
    public const string PurchaseReturn    = "purchaseReturnAccountID";
    public const string Cash              = "cashAccountID";
    public const string PaymentVoucherCash = "paymentVoucherCashAccountID";

    // ── مخزون / تكلفة ───────────────────────────────────
    public const string Inventory         = "inventoryAccountID";
    public const string COGS              = "costOfGoodsSoldAccountID";

    // ── ضرائب ───────────────────────────────────────────
    public const string VatOutput         = "vatOutputAccountID";
    public const string VatInput          = "vatInputAccountID";

    // ── حسابات POS ──────────────────────────────────────
    public const string PosCash           = "posCashAccountID";
    public const string PosBank           = "posBankAccountID";
    public const string PosVodafone       = "posVodafoneAccountID";
    public const string PosInstaPay       = "posInstapayAccountID";

    // ── حسابات الموقع ────────────────────────────────────
    public const string WebCash           = "webCashAccountID";
    public const string WebBank           = "webBankAccountID";
    public const string WebVodafone       = "webVodafoneAccountID";
    public const string WebInstaPay       = "webInstapayAccountID";

    // ── حقوق ملكية / افتتاحي ────────────────────────────
    public const string OpeningEquity     = "openingEquityAccountID";

    // ── تسويات المخزون ──────────────────────────────────
    public const string InventoryVariance = "inventoryVarianceAccountID";

    public const string DepreciationExpense      = "depreciationExpenseAccountID";
    public const string AccumulatedDepreciation = "accumulatedDepreciationAccountID";

    // ── حسابات الرواتب (HR) ───────────────────────────────
    public const string SalaryExpense          = "salaryExpenseAccountID";
    public const string SalariesPayable        = "salariesPayableAccountID";
    public const string EmployeeAdvances       = "employeeAdvancesAccountID";
    public const string EmployeeBonuses        = "employeeBonusesAccountID";
    public const string EmployeeDeductions     = "employeeDeductionsAccountID";
    public const string TransportationAllowanceExpense = "transportationAllowanceExpenseAccountID";
    public const string CommunicationAllowanceExpense  = "communicationAllowanceExpenseAccountID";
    public const string FixedAllowanceExpense          = "fixedAllowanceExpenseAccountID";
    public const string FixedDeductionRevenue          = "fixedDeductionRevenueAccountID";

    // ── تقفيلات الـ POS ──────────────────────────────────
    public const string PosDailyClosure        = "posDailyClosureAccountID";
}
