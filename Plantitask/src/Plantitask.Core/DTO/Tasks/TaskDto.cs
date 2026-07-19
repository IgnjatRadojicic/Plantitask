using Plantitask.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Plantitask.Core.DTO.Tasks
{
    public class TaskDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; } = string.Empty;
        public Guid GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;

        public int StatusId { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public string StatusDisplayName { get; set; } = string.Empty;
        public string? StatusColor { get; set; }

        public int PriorityId { get; set; }
        public string PriorityName { get; set; } = string.Empty;
        public string? PriorityColor { get; set; }

        public Guid? AssignedToId { get; set; }
        public string? AssignedToUserName { get; set; }

        public DateTime? DueDate { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid CreatedBy { get; set; }
        public string CreatedByUserName { get; set; } = string.Empty;

        public int AttachmentCount { get; set; }

        public static Expression<Func<TaskItem, TaskDto>> Projection => t => new TaskDto
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
