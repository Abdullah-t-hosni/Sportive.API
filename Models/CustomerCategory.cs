using System.Collections.Generic;

namespace Sportive.API.Models;

public class CustomerCategory : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DefaultDiscount { get; set; } = 0;
    public decimal MinimumSpending { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
}
