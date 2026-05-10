

namespace Plantitask.Core.DTO.Audit
{
    public class CreateAuditLogRequest
    {
        public required string EntityType { get; init; }
        public required Guid EntityId { get; init; }
        public required string Action { get; init; }
        public required Guid UserId { get; init; }
        public required string UserName { get; init; }
        public required string UserEmail { get; init; }
        public Guid? GroupId { get; init; }
        public string? IpAddress { get; init; }
        public string? UserAgent { get; init; }
        public string? PropertyName { get; init; }
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }
        public string? Reason { get; init; }
    }
}
