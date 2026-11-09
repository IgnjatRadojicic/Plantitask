using Plantitask.Core.Entities;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events.Handlers.Audit
{
    public abstract class AuditHandler
    {
        // A comment can be 2000 characters and a user agent is whatever the client sends. A value
        // over the column limit would fail the save on every retry and park the message.
        private const int MaxValueLength = 1000;
        private const int MaxUserAgentLength = 500;

        protected ApplicationDbContext Db { get; }

        protected AuditHandler(ApplicationDbContext db)
        {
            Db = db;
        }

        public string Name => "audit";

        // Only adds. The drainer saves this row and the inbox row in one transaction.
        protected Task Write(
            EventContext context,
            string entityType,
            Guid entityId,
            string action,
            Guid? groupId = null,
            string? propertyName = null,
            string? oldValue = null,
            string? newValue = null,
            EventActor? person = null,
            string? ipAddress = null,
            string? userAgent = null)
        {
            var actor = person ?? context.Actor;

            Db.AuditLogs.Add(new AuditLog
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                GroupId = groupId,
                PropertyName = propertyName,
                OldValue = Limit(oldValue, MaxValueLength),
                NewValue = Limit(newValue, MaxValueLength),
                UserId = actor?.Id,
                UserName = actor?.UserName,
                UserEmail = actor?.Email,
                IpAddress = ipAddress,
                UserAgent = Limit(userAgent, MaxUserAgentLength),
                Source = context.Source,
                OccurredAt = context.OccurredAt,
            });

            return Task.CompletedTask;
        }

        private static string? Limit(string? value, int maxLength) =>
            value is not null && value.Length > maxLength ? value[..maxLength] : value;
    }
}
