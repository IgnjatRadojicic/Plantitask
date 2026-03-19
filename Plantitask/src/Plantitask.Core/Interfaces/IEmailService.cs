
namespace Plantitask.Core.Interfaces
{
    public interface IEmailService
    {
        Task SendGroupInvitationEmailAsync(string email, string inviterName, string groupName, string groupCode);
        Task SendPasswordResetEmailAsync(string email, string userName, string resetLink);
        Task SendTaskAssignmentEmailAsync(string email, string userName, string taskTitle, string groupName, string assignedBy);
        Task SendTaskCommentEmailAsync(string email, string userName, string commenterName, string taskTitle, string commentText);
        Task SendWelcomeEmailAsync(string email, string firstName);
        Task SendEmailVerificationCodeAsync(string email, string userName, string code);
        Task SendTaskDueSoonEmailAsync(string email, string userName, string taskTitle, DateTime dueDate);
        Task SendTaskOverdueEmailAsync(string email, string userName, string taskTitle, int daysOverdue);
    }
}