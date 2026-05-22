using System;

namespace Sportive.API.DTOs
{
    public class PosHeldCartDto
    {
        public int Id { get; set; }
        public string ReferenceId { get; set; }
        public string Name { get; set; }
        public string? Phone { get; set; }
        public string ItemsJson { get; set; }
        public decimal Total { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreatePosHeldCartDto
    {
        public string ReferenceId { get; set; }
        public string Name { get; set; }
        public string? Phone { get; set; }
        public string ItemsJson { get; set; }
        public decimal Total { get; set; }
    }
}
