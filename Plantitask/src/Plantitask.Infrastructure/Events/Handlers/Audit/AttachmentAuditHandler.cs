using Plantitask.Core.Constants;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events.Handlers.Audit
{
    public class AttachmentAuditHandler : AuditHandler,
        IDurableEventHandler<AttachmentUploaded>,
        IDurableEventHandler<AttachmentDeleted>
    {
        public AttachmentAuditHandler(ApplicationDbContext db) : base(db)
        {
        }

        public Task HandleAsync(AttachmentUploaded e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskAttachment, e.AttachmentId, AuditActions.Uploaded, e.GroupId,
                propertyName: "FileName",
                newValue: e.FileName);

        public Task HandleAsync(AttachmentDeleted e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskAttachment, e.AttachmentId, AuditActions.Deleted, e.GroupId,
                propertyName: "FileName",
                oldValue: e.FileName);
    }
}
