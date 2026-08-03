namespace Sportive.API.Models;

public class District : BaseEntity
{
    public string BostaId { get; set; } = string.Empty;
    public int GovernorateId { get; set; }
    public Governorate Governorate { get; set; } = null!;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
}
