using Plantitask.Core.Constants;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events.Handlers.Audit
{
    public class CommentAuditHandler : AuditHandler,
        IDurableEventHandler<CommentAdded>,
        IDurableEventHandler<CommentEdited>,
        IDurableEventHandler<CommentDeleted>
    {
        public CommentAuditHandler(ApplicationDbContext db) : base(db)
        {
        }

        public Task HandleAsync(CommentAdded e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskComment, e.CommentId, AuditActions.Created, e.GroupId,
                propertyName: "Content",
                newValue: e.Content);

        public Task HandleAsync(CommentEdited e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskComment, e.CommentId, AuditActions.Updated, e.GroupId,
                propertyName: "Content",
                newValue: e.Content);

        public Task HandleAsync(CommentDeleted e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskComment, e.CommentId, AuditActions.Deleted, e.GroupId);
    }
}
