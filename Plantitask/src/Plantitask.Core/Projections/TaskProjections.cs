using System;
using System.Linq.Expressions;
using Plantitask.Core.DTO.Dashboard;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;

namespace Plantitask.Core.Projections
{
    public static class TaskProjections
    {
        public static Expression<Func<TaskItem, TaskDto>> ToTaskDto => t => new TaskDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            GroupId = t.GroupId,
            GroupName = t.Group.Name,
            StatusId = t.StatusId,
            StatusName = t.Status.Name,
            StatusDisplayName = t.Status.DisplayName,
            StatusColor = t.Status.Color,
            PriorityId = t.PriorityId,
            PriorityName = t.Priority.DisplayName,
            PriorityColor = t.Priority.Color,
            AssignedToId = t.AssignedToId,
            AssignedToUserName = t.AssignedTo != null ? t.AssignedTo.UserName : null,
            DueDate = t.DueDate,
            CompletedAt = t.CompletedAt,
            CreatedAt = t.CreatedAt,
            CreatedBy = t.CreatedBy,
            CreatedByUserName = t.Creator.UserName,
            AttachmentCount = t.Attachments.Count
        };

        public static Expression<Func<TaskItem, TaskSummaryDto>> ToTaskSummary => t => new TaskSummaryDto
        {
            Id = t.Id,
            Title = t.Title,
            GroupId = t.GroupId,
            GroupName = t.Group.Name,
            StatusName = t.Status.DisplayName,
            StatusColor = t.Status.Color,
            PriorityName = t.Priority.DisplayName,
            PriorityColor = t.Priority.Color,
            DueDate = t.DueDate,
            CompletedAt = t.CompletedAt
        };

        public static Expression<Func<TaskItem, TaskReminder>> ToReminder => t => new TaskReminder
        {
            Id = t.Id,
            Title = t.Title,
            StatusId = t.StatusId,
            DueDate = t.DueDate,
            AssignedToId = t.AssignedToId,
            AssigneeEmail = t.AssignedTo != null ? t.AssignedTo.Email : null,
            AssigneeName = t.AssignedTo != null ? t.AssignedTo.UserName : null
        };
    }
}
