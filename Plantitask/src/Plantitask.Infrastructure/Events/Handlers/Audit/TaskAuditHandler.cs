using Microsoft.EntityFrameworkCore;
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

        public async Task HandleAsync(TaskStatusChanged e, EventContext context, CancellationToken ct) =>
            await Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.StatusChanged, e.GroupId,
                propertyName: "StatusId",
                oldValue: await StatusNameAsync(e.OldStatusId, ct),
                newValue: await StatusNameAsync(e.NewStatusId, ct));

        public async Task HandleAsync(TaskPriorityChanged e, EventContext context, CancellationToken ct) =>
            await Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.PriorityChanged, e.GroupId,
                propertyName: "Priority",
                oldValue: await PriorityNameAsync(e.OldPriorityId, ct),
                newValue: await PriorityNameAsync(e.NewPriorityId, ct));

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

        public async Task HandleAsync(TaskMoved e, EventContext context, CancellationToken ct) =>
            await Write(context, EntityTypes.TaskItem, e.TaskId, AuditActions.Moved, e.GroupId,
                propertyName: "StatusId",
                oldValue: await StatusNameAsync(e.OldStatusId, ct),
                newValue: await StatusNameAsync(e.NewStatusId, ct));

        private Task<string> StatusNameAsync(int id, CancellationToken ct) =>
            Db.TaskStatuses.Where(s => s.Id == id).Select(s => s.DisplayName).SingleAsync(ct);

        private Task<string> PriorityNameAsync(int id, CancellationToken ct) =>
            Db.TaskPriorities.Where(p => p.Id == id).Select(p => p.DisplayName).SingleAsync(ct);
    }
}
