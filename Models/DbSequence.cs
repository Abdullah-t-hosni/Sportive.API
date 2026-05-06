namespace Sportive.API.Models;

public class DbSequence
{
    public int Id { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string Stamp { get; set; } = string.Empty; // e.g. "2504" for YYMM
    public int LastValue { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
