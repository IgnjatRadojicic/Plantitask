using Microsoft.Extensions.Logging;
using Plantitask.Contracts.Enums;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services;

/// <summary>
/// The emails a task change sends: who gets one, whether they opted out, and the address as it
/// is at send time. The Send methods let a mail failure escape, so a durable handler can retry.
/// The Try methods are the same sends for the controllers, which run after the commit and can
/// only log. They go when the controllers stop sending mail.
/// </summary>
public class TaskEmailSender : ITaskEmailSender
{
    private readonly INotificationService _notifications;
    private readonly IEmailService _emailService;
    private readonly ILogger<TaskEmailSender> _logger;

    public TaskEmailSender(
        INotificationService notifications,
        IEmailService emailService,
        ILogger<TaskEmailSender> logger)
    {
        _notifications = notifications;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task SendTaskAssignmentEmailAsync(Guid assigneeUserId, string taskTitle, string groupName, string assignedByUserName)
    {
        if (!await _notifications.ShouldEmailAsync(assigneeUserId, NotificationType.TaskAssigned))
            return;

        var contact = await _notifications.GetUserContactAsync(assigneeUserId);
        if (contact == null)
            return;

        await _emailService.SendTaskAssignmentEmailAsync(
            contact.Value.Email,
            contact.Value.UserName,
            taskTitle,
            groupName,
            assignedByUserName);
    }

    /// <summary>Tells the assignee about a new comment, skipping self-comments and unassigned tasks.</summary>
    public async Task SendCommentEmailAsync(Guid taskAssignedToUserId, Guid commenterId, string taskTitle, string commentContent)
    {
        if (taskAssignedToUserId == Guid.Empty || taskAssignedToUserId == commenterId)
            return;

        if (!await _notifications.ShouldEmailAsync(taskAssignedToUserId, NotificationType.TaskCommentAdded))
            return;

        var assignee = await _notifications.GetUserContactAsync(taskAssignedToUserId);
        var commenter = await _notifications.GetUserContactAsync(commenterId);

        if (assignee == null || commenter == null)
            return;

        await _emailService.SendTaskCommentEmailAsync(
            assignee.Value.Email,
            assignee.Value.UserName,
            commenter.Value.UserName,
            taskTitle,
            commentContent);
    }

    public async Task TrySendTaskAssignmentEmailAsync(Guid assigneeUserId, string taskTitle, string groupName, string assignedByUserName)
    {
        try
        {
            await SendTaskAssignmentEmailAsync(assigneeUserId, taskTitle, groupName, assignedByUserName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task assignment email to user {UserId}", assigneeUserId);
        }
    }

    public async Task TrySendCommentEmailAsync(Guid taskAssignedToUserId, Guid commenterId, string taskTitle, string commentContent)
    {
        try
        {
            await SendCommentEmailAsync(taskAssignedToUserId, commenterId, taskTitle, commentContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send comment email to user {UserId}", taskAssignedToUserId);
        }
    }
}
