using System.Collections.Generic;

namespace Sportive.API.Models;

public class SizeGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty; // e.g. "Shoes EU", "Apparel Alpha"
    public string? Description { get; set; }
    public ICollection<SizeValue> Values { get; set; } = new List<SizeValue>();
}

public class SizeValue : BaseEntity
{
    public int SizeGroupId { get; set; }
    public SizeGroup SizeGroup { get; set; } = null!;
    public string Value { get; set; } = string.Empty; // e.g. "40", "XL"
    public int SortOrder { get; set; } = 0;
}
