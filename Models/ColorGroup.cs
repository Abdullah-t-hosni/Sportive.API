using System.Collections.Generic;

namespace Sportive.API.Models;

public class ColorGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty; // e.g. "Basic Colors", "Neon Summer"
    public string? Description { get; set; }
    public ICollection<ColorValue> Values { get; set; } = new List<ColorValue>();
}

public class ColorValue : BaseEntity
{
    public int ColorGroupId { get; set; }
    [global::System.Text.Json.Serialization.JsonIgnore] public ColorGroup? ColorGroup { get; set; }
    public string Value { get; set; } = string.Empty; // e.g. "Red", "Blue"
    public int SortOrder { get; set; } = 0;
}
