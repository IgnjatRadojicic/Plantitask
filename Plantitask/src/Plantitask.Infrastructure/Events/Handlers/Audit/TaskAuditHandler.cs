using Plantitask.Core.Constants;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events.Handlers.Audit
{
    public class TaskAuditHandler : AuditHandler,
        IDurableEventHandler<TaskCreated>,
        IDurableEventHandler<TaskUpdated>,
        IDurableEventHandler<TaskStatusChanged>,
        IDurableEventHandler<TaskPriorityChanged>,
        IDurableEventHandler<TaskAssigned>,
        IDurableEventHandler<TaskUnassigned>,
        IDurableEventHandler<TaskDeleted>,
        IDurableEventHandler<TaskMoved>
    {
        public TaskAuditHandler(ApplicationDbContext db) : base(db)
        {
        }

        public Task HandleAsync(TaskCreated e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.Created, e.GroupId);

        public Task HandleAsync(TaskUpdated e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.Updated, e.GroupId);

        public Task HandleAsync(TaskStatusChanged e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.StatusChanged, e.GroupId,
                propertyName: "StatusId",
                oldValue: e.OldStatusName,
                newValue: e.NewStatusName);

        public Task HandleAsync(TaskPriorityChanged e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.PriorityChanged, e.GroupId,
                propertyName: "Priority",
                oldValue: e.OldPriorityName,
                newValue: e.NewPriorityName);

        public Task HandleAsync(TaskAssigned e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.Assigned, e.GroupId,
                propertyName: "AssignedTo",
                newValue: e.AssigneeName);

        public Task HandleAsync(TaskUnassigned e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.Unassigned, e.GroupId,
                propertyName: "AssignedTo",
                oldValue: e.PreviousAssigneeName);

        public Task HandleAsync(TaskDeleted e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.Deleted, e.GroupId);

        public Task HandleAsync(TaskMoved e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.Moved, e.GroupId,
                propertyName: "StatusId",
                oldValue: e.OldStatusName,
                newValue: e.NewStatusName);
    }
}
