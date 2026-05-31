using System;

namespace Sportive.API.Models
{
    public class ZkDevice : BaseEntity
    {
        public string SerialNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime? LastActive { get; set; }
        public string? Notes { get; set; }
    }
}
