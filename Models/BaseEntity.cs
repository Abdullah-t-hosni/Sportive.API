using Sportive.API.Utils;

namespace Sportive.API.Models;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
