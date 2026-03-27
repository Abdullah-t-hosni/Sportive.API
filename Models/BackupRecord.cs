using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class BackupRecord : BaseEntity
{
    [MaxLength(200)]
    public string FileName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? FilePath { get; set; }
    
    public long FileSizeBytes { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; } = true;
    public bool EmailSent { get; set; } = false;
    public string? EmailError { get; set; }
    public string? Error { get; set; }
    
    [MaxLength(20)]
    public string TriggerType { get; set; } = "auto"; // auto or manual
    
    [MaxLength(50)]
    public string? TriggeredBy { get; set; }
}
