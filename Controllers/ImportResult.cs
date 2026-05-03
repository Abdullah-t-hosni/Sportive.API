using System.Collections.Generic;

namespace Sportive.API.Controllers;

public class ImportResult
{
    public int Added { get; set; } = 0;
    public int Updated { get; set; } = 0;
    public int VariantsAdded { get; set; } = 0;
    public List<string> Skipped { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
