using System;
using System.Linq.Expressions;
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
            PriorityName = t.Priority.Name,
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
    }
}
