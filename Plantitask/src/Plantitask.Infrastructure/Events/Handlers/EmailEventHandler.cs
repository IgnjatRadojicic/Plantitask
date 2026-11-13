using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Events.Handlers
{
    public class EmailEventHandler :
        IDurableEventHandler<TaskCreated>,
        IDurableEventHandler<TaskAssigned>,
        IDurableEventHandler<CommentAdded>
    {
        private readonly ITaskEmailSender _emails;

        public EmailEventHandler(ITaskEmailSender emails)
        {
            _emails = emails;
        }

        public string Name => "email";

        public async Task HandleAsync(TaskCreated e, EventContext context, CancellationToken ct)
        {
            if (e.AssigneeId is not Guid assigneeId)
                return;

            await SendAssignmentAsync(assigneeId, e.Title, e.GroupName, context);
        }

        public async Task HandleAsync(TaskAssigned e, EventContext context, CancellationToken ct)
        {
            await SendAssignmentAsync(e.AssigneeId, e.Title, e.GroupName, context);
        }

        public async Task HandleAsync(CommentAdded e, EventContext context, CancellationToken ct)
        {
            if (context.Actor is not { } commenter)
                return;

            await _emails.SendCommentEmailAsync(
                e.TaskAssigneeId ?? Guid.Empty, commenter.Id, e.TaskTitle, e.Content);
        }

        private async Task SendAssignmentAsync(Guid assigneeId, string title, string groupName, EventContext context)
        {
            if (context.Actor is not { } actor || actor.Id == assigneeId)
                return;

            await _emails.SendTaskAssignmentEmailAsync(assigneeId, title, groupName, actor.UserName);
        }
    }
}
