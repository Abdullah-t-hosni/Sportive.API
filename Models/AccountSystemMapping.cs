namespace Sportive.API.Models;

/// <summary>
/// يربط مفاتيح النظام (مبيعات، مخزون، نقدية...) بحسابات شجرة الحسابات.
/// </summary>
public class AccountSystemMapping : BaseEntity
{
    public const int MaxKeyLength = 120;

    public string Key { get; set; } = string.Empty;

    public int? AccountId { get; set; }
    public Account? Account { get; set; }
}
