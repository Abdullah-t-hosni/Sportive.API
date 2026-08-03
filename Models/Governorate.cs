using System.Collections.Generic;

namespace Sportive.API.Models;

public class Governorate : BaseEntity
{
    public string BostaId { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public ICollection<District> Districts { get; set; } = new List<District>();
}
