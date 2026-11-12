namespace Plantitask.Core.Interfaces;

public interface ITaskEmailSender
{
    Task SendTaskAssignmentEmailAsync(Guid assigneeUserId, string taskTitle, string groupName, string assignedByUserName);
    Task SendCommentEmailAsync(Guid taskAssignedToUserId, Guid commenterId, string taskTitle, string commentContent);

    Task TrySendTaskAssignmentEmailAsync(Guid assigneeUserId, string taskTitle, string groupName, string assignedByUserName);
    Task TrySendCommentEmailAsync(Guid taskAssignedToUserId, Guid commenterId, string taskTitle, string commentContent);
}
