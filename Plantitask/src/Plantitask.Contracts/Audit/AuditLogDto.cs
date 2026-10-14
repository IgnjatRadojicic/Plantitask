using System;

namespace Plantitask.Contracts.DTO.Audit
{
    public class AuditLogDto
    {
        public Guid Id { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? PropertyName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public Guid? UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }

        public DateTime Timestamp { get; set; }
        public string? Reason { get; set; }
        public string? IpAddress { get; set; }
    }
}
