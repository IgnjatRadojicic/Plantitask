using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Events.Handlers
{
    public class EmailEventHandler :
        IDurableEventHandler<TaskCreated>,
        IDurableEventHandler<TaskAssigned>,
        IDurableEventHandler<CommentAdded>,
        IDurableEventHandler<UserRegistered>
    {
        private readonly ITaskEmailSender _emails;
        private readonly IEmailService _emailService;

        public EmailEventHandler(ITaskEmailSender emails, IEmailService emailService)
        {
            _emails = emails;
            _emailService = emailService;
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

        public async Task HandleAsync(UserRegistered e, EventContext context, CancellationToken ct)
        {
            await _emailService.SendWelcomeEmailAsync(e.Email, e.DisplayName);
        }

        private async Task SendAssignmentAsync(Guid assigneeId, string title, string groupName, EventContext context)
        {
            if (context.Actor is not { } actor || actor.Id == assigneeId)
                return;

            await _emails.SendTaskAssignmentEmailAsync(assigneeId, title, groupName, actor.UserName);
        }
    }
}
