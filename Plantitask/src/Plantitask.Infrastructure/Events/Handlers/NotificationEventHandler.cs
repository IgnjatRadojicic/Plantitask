using Plantitask.Contracts.DTO.Notifications;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Events.Handlers
{
    public class NotificationEventHandler :
        IDurableEventHandler<TaskCreated>,
        IDurableEventHandler<TaskUpdated>,
        IDurableEventHandler<TaskStatusChanged>,
        IDurableEventHandler<TaskPriorityChanged>,
        IDurableEventHandler<TaskAssigned>,
        IDurableEventHandler<TaskUnassigned>,
        IDurableEventHandler<CommentAdded>
    {
        private readonly INotificationService _notifications;
        private readonly INotificationBroadcaster _broadcaster;

        public NotificationEventHandler(INotificationService notifications, INotificationBroadcaster broadcaster)
        {
            _notifications = notifications;
            _broadcaster = broadcaster;
        }

        public string Name => "notifications";

        public async Task HandleAsync(TaskCreated e, EventContext context, CancellationToken ct)
        {
            var sent = await _notifications.NotifyAssignmentAsync(
                ActorId(context),
                new TaskSnapshot { Id = e.TaskId, Title = e.Title, AssigneeId = e.AssigneeId });

            await PushAsync(sent);
        }

        public async Task HandleAsync(TaskUpdated e, EventContext context, CancellationToken ct)
        {
            var sent = await _notifications.NotifyTaskUpdatedAsync(
                ActorId(context),
                new TaskSnapshot { Id = e.TaskId, Title = e.Title, AssigneeId = e.AssigneeId });

            await PushAsync(sent);
        }

        public async Task HandleAsync(TaskStatusChanged e, EventContext context, CancellationToken ct)
        {
            var sent = await _notifications.NotifyTaskStatusChangedAsync(
                ActorId(context),
                new TaskSnapshot { Id = e.TaskId, Title = e.Title, AssigneeId = e.AssigneeId },
                createdById: e.CreatedById,
                e.OldStatusName,
                e.NewStatusName);

            await PushAsync(sent);
        }

        public async Task HandleAsync(TaskPriorityChanged e, EventContext context, CancellationToken ct)
        {
            var sent = await _notifications.NotifyTaskPriorityChangedAsync(
                ActorId(context),
                new TaskSnapshot { Id = e.TaskId, Title = e.Title, AssigneeId = e.AssigneeId },
                e.OldPriorityName,
                e.NewPriorityName);

            await PushAsync(sent);
        }

        public async Task HandleAsync(TaskAssigned e, EventContext context, CancellationToken ct)
        {
            var sent = await _notifications.NotifyAssignmentAsync(
                ActorId(context),
                new TaskSnapshot { Id = e.TaskId, Title = e.Title, AssigneeId = e.AssigneeId });

            await PushAsync(sent);
        }

        public async Task HandleAsync(TaskUnassigned e, EventContext context, CancellationToken ct)
        {
            var sent = await _notifications.NotifyUnassignedAsync(
                ActorId(context),
                new TaskSnapshot { Id = e.TaskId, Title = e.Title, AssigneeId = null },
                e.PreviousAssigneeId);

            await PushAsync(sent);
        }

        public async Task HandleAsync(CommentAdded e, EventContext context, CancellationToken ct)
        {
            var sent = await _notifications.NotifyTaskCommentAddedAsync(
                ActorId(context),
                new TaskSnapshot { Id = e.TaskId, Title = e.TaskTitle, AssigneeId = e.TaskAssigneeId },
                createdById: e.TaskCreatedById);

            await PushAsync(sent);
        }

        private async Task PushAsync(NotificationDto? notification)
        {
            if (notification != null)
                await _broadcaster.BroadcastNotificationAsync(notification);
        }

        private async Task PushAsync(List<NotificationDto> notifications)
        {
            foreach (var notification in notifications)
                await _broadcaster.BroadcastNotificationAsync(notification);
        }

        private static Guid ActorId(EventContext context) => context.Actor?.Id ?? Guid.Empty;
    }
}
