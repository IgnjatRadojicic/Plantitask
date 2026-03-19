using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Constants;


namespace Plantitask.Infrastructure.Services
{
    public class TaskService : ITaskService
    {
        private readonly IApplicationDbContext _context;
        private readonly ILogger<TaskService> _logger;
        private readonly IBackgroundJobService _backgroundJobService;

        public TaskService(
            IApplicationDbContext context,
            ILogger<TaskService> logger,
            IBackgroundJobService backgroundJobService)
        {
            _context = context;
            _logger = logger;
            _backgroundJobService = backgroundJobService;
        }

        public async Task<Result<TaskDto>> CreateTaskAsync(Guid groupId, CreateTaskDto createTaskDto, Guid userId)
        {
            _logger.LogInformation("User {UserId} creating task in group {GroupId}", userId, groupId);

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of this group to create tasks");

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead)
                return Error.Forbidden("Only Team Leads, Managers, and Owners can create tasks");

            var priorityExists = await _context.TaskPriorities
                .AnyAsync(p => p.Id == createTaskDto.PriorityId && p.IsActive);

            if (!priorityExists)
                return Error.BadRequest("Invalid priority selected");

            if (createTaskDto.AssignedToUserId.HasValue)
            {
                var assigneeIsMember = await _context.GroupMembers
                    .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == createTaskDto.AssignedToUserId.Value);

                if (!assigneeIsMember)
                    return Error.BadRequest("Cannot assign task to user who is not a group member");
            }

            var task = new TaskItem
            {
                Title = createTaskDto.Title,
                Description = createTaskDto.Description,
                GroupId = groupId,
                StatusId = (int)TaskStatusItem.NotStarted,
                PriorityId = createTaskDto.PriorityId,
                DueDate = createTaskDto.DueDate,
                AssignedToId = createTaskDto.AssignedToUserId,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            var result = await GetTaskByIdAsync(task.Id, userId);

            if (task.DueDate.HasValue && task.AssignedToId.HasValue)
            {
                _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, task.AssignedToId.Value, task.DueDate.Value);
            }

            _logger.LogInformation("Task {TaskId} created in group {GroupId} by user {UserId}",
                task.Id, groupId, userId);

            return result;
        }

        public async Task<Result<List<TaskDto>>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter, Guid userId)
        {
            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of this group to view its tasks");

            var query = _context.Tasks
                .Where(t => t.GroupId == groupId)
                .AsQueryable();

            if (filter != null)
            {
                if (filter.StatusId.HasValue)
                    query = query.Where(t => t.StatusId == filter.StatusId.Value);

                if (filter.PriorityId.HasValue)
                    query = query.Where(t => t.PriorityId == filter.PriorityId.Value);

                if (filter.AssignedToUserId.HasValue)
                    query = query.Where(t => t.AssignedToId == filter.AssignedToUserId.Value);

                if (filter.IsOverDue == true)
                {
                    query = query.Where(t => t.DueDate.HasValue &&
                                            t.DueDate.Value < DateTime.UtcNow &&
                                            t.StatusId != (int)TaskStatusItem.Completed);
                }

                if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
                {
                    var searchLower = filter.SearchTerm.ToLower();
                    query = query.Where(t =>
                        t.Title.ToLower().Contains(searchLower) ||
                        t.Description.ToLower().Contains(searchLower));
                }
            }

            var tasks = await query
                .Include(t => t.Status)
                .Include(t => t.Priority)
                .Include(t => t.Group)
                .Include(t => t.AssignedTo)
                .Include(t => t.Creator)
                .Include(t => t.Attachments)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TaskDto
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
                })
                .ToListAsync();

            return tasks;
        }

        public async Task<Result<TaskDto>> GetTaskByIdAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Status)
                .Include(t => t.Priority)
                .Include(t => t.Group)
                .Include(t => t.AssignedTo)
                .Include(t => t.Creator)
                .Include(t => t.Attachments)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to view this task");

            return new TaskDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                GroupId = task.GroupId,
                GroupName = task.Group.Name,
                StatusId = task.StatusId,
                StatusName = task.Status.Name,
                StatusDisplayName = task.Status.DisplayName,
                StatusColor = task.Status.Color,
                PriorityId = task.PriorityId,
                PriorityName = task.Priority.Name,
                PriorityColor = task.Priority.Color,
                AssignedToId = task.AssignedToId,
                AssignedToUserName = task.AssignedTo?.UserName,
                DueDate = task.DueDate,
                CompletedAt = task.CompletedAt,
                CreatedAt = task.CreatedAt,
                CreatedBy = task.CreatedBy,
                CreatedByUserName = task.Creator.UserName,
                AttachmentCount = task.Attachments.Count
            };
        }

        public async Task<Result<TaskDto>> UpdateTaskAsync(Guid taskId, UpdateTaskDto updateTaskDto, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of this group");

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead && task.CreatedBy != userId)
                return Error.Forbidden("Only the task creator or Team Leads and above can change task priority");

            if (!string.IsNullOrWhiteSpace(updateTaskDto.Title))
                task.Title = updateTaskDto.Title;

            if (!string.IsNullOrWhiteSpace(updateTaskDto.Description))
                task.Description = updateTaskDto.Description;

            if (updateTaskDto.PriorityId.HasValue)
            {
                var priorityExists = await _context.TaskPriorities
                    .AnyAsync(p => p.Id == updateTaskDto.PriorityId.Value && p.IsActive);

                if (!priorityExists)
                    return Error.BadRequest("Invalid priority selected");

                task.PriorityId = updateTaskDto.PriorityId.Value;
            }

            if (updateTaskDto.DueDate.HasValue)
                task.DueDate = updateTaskDto.DueDate.Value;

            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var result = await GetTaskByIdAsync(task.Id, userId);

            if (task.DueDate.HasValue && task.AssignedToId.HasValue)
            {
                _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, task.AssignedToId.Value, task.DueDate.Value);
            }

            _logger.LogInformation("Task {TaskId} updated by user {UserId}", taskId, userId);

            return result;
        }

        public async Task<Result<TaskStatusChangeResult>> ChangeTaskStatusAsync(Guid taskId, ChangeTaskStatusDto statusDto, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Status)
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of this group");

            var canChangeStatus = membership.Role.PermissionLevel >= PermissionLevels.TeamLead
                || task.AssignedToId == userId
                || task.CreatedBy == userId;

            if (!canChangeStatus)
                return Error.Forbidden("You don't have permission to change this task's status");

            var oldStatus = task.Status.DisplayName;

            var newStatus = await _context.TaskStatuses
                .FirstOrDefaultAsync(s => s.Id == statusDto.NewStatusId && s.IsActive);

            if (newStatus == null)
                return Error.BadRequest("Invalid status selected");

            task.StatusId = statusDto.NewStatusId;
            task.CompletedAt = statusDto.NewStatusId == (int)TaskStatusItem.Completed
                ? DateTime.UtcNow
                : null;
            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var taskDtoResult = await GetTaskByIdAsync(taskId, userId);

            _logger.LogInformation("Task {TaskId} status changed to {StatusId} by user {UserId}",
                taskId, statusDto.NewStatusId, userId);

            if (taskDtoResult.IsFailure)
                return taskDtoResult.Error!;

            return new TaskStatusChangeResult
            {
                Task = taskDtoResult.Value!,
                OldStatus = oldStatus,
                NewStatus = newStatus.DisplayName
            };
        }

        public async Task<Result<TaskPriorityChangeResult>> ChangeTaskPriorityAsync(Guid taskId, int newPriorityId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Priority)
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of this group");

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead)
                return Error.Forbidden("Only Team Leads and above can change task priority");

            var oldPriority = task.Priority.Name;

            var newPriority = await _context.TaskPriorities
                .FirstOrDefaultAsync(p => p.Id == newPriorityId && p.IsActive);

            if (newPriority == null)
                return Error.BadRequest("Invalid priority selected");

            task.PriorityId = newPriorityId;
            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} priority changed to {PriorityId} by user {UserId}",
                taskId, newPriorityId, userId);

            var taskDtoResult = await GetTaskByIdAsync(taskId, userId);
            if (taskDtoResult.IsFailure)
                return taskDtoResult.Error!;

            return new TaskPriorityChangeResult
            {
                Task = taskDtoResult.Value!,
                OldPriority = oldPriority,
                NewPriority = newPriority.Name
            };
        }

        public async Task<Result> AssignTaskAsync(Guid taskId, AssignTaskDto assignDto, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of this group");

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead)
                return Error.Forbidden("Only Team Leads and above can assign tasks");

            var assigneeIsMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == assignDto.UserId);

            if (!assigneeIsMember)
                return Error.BadRequest("Cannot assign task to user who is not a group member");

            task.AssignedToId = assignDto.UserId;
            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (task.DueDate.HasValue)
            {
                _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, assignDto.UserId, task.DueDate.Value);
            }

            _logger.LogInformation("Task {TaskId} assigned to user {AssignedUserId} by {UserId}",
                taskId, assignDto.UserId, userId);

            return Result.Success();
        }

        public async Task<Result> UnassignTaskAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .Include(t => t.Attachments)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of this group");

            var canUnassign = membership.Role.PermissionLevel >= PermissionLevels.TeamLead
                || task.AssignedToId == userId;

            if (!canUnassign)
                return Error.Forbidden("Only Team Leads and above or the assigned user can unassign tasks");

            task.AssignedToId = null;
            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} unassigned by user {UserId}", taskId, userId);

            return Result.Success();
        }

        public async Task<Result> DeleteTaskAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .Include(t => t.Attachments)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of this group");

            if (membership.Role.PermissionLevel < PermissionLevels.Manager)
                return Error.Forbidden("Only Managers and Owners can delete tasks");

            task.IsDeleted = true;
            task.DeletedAt = DateTime.UtcNow;
            task.DeletedBy = userId;

            foreach (var attachment in task.Attachments)
            {
                attachment.IsDeleted = true;
                attachment.DeletedAt = DateTime.UtcNow;
                attachment.DeletedBy = userId;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} deleted by user {UserId}", taskId, userId);

            return Result.Success();
        }

        public async Task<Result<List<Guid>>> GetTaskGroupMembersAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of this group");

            var memberIds = await _context.GroupMembers
                .Where(gm => gm.GroupId == task.GroupId)
                .Select(gm => gm.UserId)
                .ToListAsync();

            return memberIds;
        }

        public async Task<Result<KanbanBoardDto>> GetKanbanBoardAsync(Guid groupId, Guid userId)
        {
            var membership = await _context.GroupMembers
                .AsNoTracking()
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You are not a member of this group");

            var group = await _context.Groups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
                return Error.NotFound("Group not found");

            var statuses = await _context.TaskStatuses
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.DisplayOrder)
                .ToListAsync();

            var tasks = await _context.Tasks
                .AsNoTracking()
                .Where(t => t.GroupId == groupId)
                .Select(t => new KanbanTaskDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    StatusId = t.StatusId,
                    PriorityId = t.PriorityId,
                    PriorityName = t.Priority.Name,
                    PriorityColor = t.Priority.Color,
                    AssignedToId = t.AssignedToId,
                    AssignedToUserName = t.AssignedTo != null ? t.AssignedTo.UserName : null,
                    DisplayOrder = t.DisplayOrder,
                    DueDate = t.DueDate,
                    CommentCount = t.Comments.Count,
                    AttachmentCount = t.Attachments.Count
                })
                .OrderBy(t => t.DisplayOrder)
                .ToListAsync();

            return new KanbanBoardDto
            {
                GroupId = group.Id,
                GroupName = group.Name,
                Columns = statuses.Select(status => new KanbanColumnDto
                {
                    StatusId = status.Id,
                    StatusName = status.Name,
                    DisplayName = status.DisplayName,
                    Color = status.Color,
                    DisplayOrder = status.DisplayOrder,
                    Tasks = tasks.Where(t => t.StatusId == status.Id).ToList()
                }).ToList()
            };
        }

        public async Task<Result> MoveTaskAsync(Guid taskId, MoveTaskDto moveDto, Guid userId)
        {
            const int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;

                try
                {
                    var task = await _context.Tasks
                        .Include(t => t.Group)
                        .FirstOrDefaultAsync(t => t.Id == taskId);

                    if (task == null)
                        return Error.NotFound("Task not found");

                    var membership = await _context.GroupMembers
                        .AsNoTracking()
                        .Include(gm => gm.Role)
                        .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

                    if (membership == null || membership.Role.PermissionLevel < PermissionLevels.Member)
                        return Error.Forbidden("You don't have permission to move tasks");

                    var oldStatusId = task.StatusId;
                    var oldDisplayOrder = task.DisplayOrder;

                    if (oldStatusId == moveDto.NewStatusId)
                    {
                        await ReorderWithinSameColumnAsync(
                            task.GroupId, task.StatusId, taskId, oldDisplayOrder, moveDto.NewDisplayOrder);
                        task.DisplayOrder = moveDto.NewDisplayOrder;
                    }
                    else
                    {
                        await ReorderAcrossColumnsAsync(
                            task.GroupId, oldStatusId, moveDto.NewStatusId, taskId, moveDto.NewDisplayOrder);

                        task.StatusId = moveDto.NewStatusId;
                        task.DisplayOrder = moveDto.NewDisplayOrder;

                        if (moveDto.NewStatusId == (int)TaskStatusItem.Completed)
                            task.CompletedAt = DateTime.UtcNow;
                        else if (oldStatusId == (int)TaskStatusItem.Completed)
                            task.CompletedAt = null;
                    }

                    task.UpdatedBy = userId;
                    task.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "Task {TaskId} moved from Status {OldStatus} Order {OldOrder} to Status {NewStatus} Order {NewOrder} by {UserId}",
                        taskId, oldStatusId, oldDisplayOrder, moveDto.NewStatusId, moveDto.NewDisplayOrder, userId);

                    return Result.Success();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogWarning(ex,
                        "Concurrency conflict on attempt {Attempt}/{MaxRetries} for task {TaskId}",
                        attempt, maxRetries, taskId);

                    if (attempt >= maxRetries)
                    {
                        _logger.LogError(ex,
                            "Failed to move task {TaskId} after {MaxRetries} attempts",
                            taskId, maxRetries);

                        return Error.Conflict(
                            "The task was modified by another user. Please refresh the board and try again.");
                    }

                    _context.ClearChangeTracker();
                    await Task.Delay(50 * attempt);
                }
            }

            return Error.Internal("Unexpected error during task move");
        }

        private async Task ReorderWithinSameColumnAsync(
            Guid groupId, int statusId, Guid movingTaskId, int oldPosition, int newPosition)
        {
            var otherTasks = await _context.Tasks
                .Where(t => t.GroupId == groupId && t.StatusId == statusId && t.Id != movingTaskId)
                .OrderBy(t => t.DisplayOrder)
                .ToListAsync();

            if (newPosition > oldPosition)
            {
                foreach (var t in otherTasks.Where(t => t.DisplayOrder > oldPosition && t.DisplayOrder <= newPosition))
                    t.DisplayOrder--;
            }
            else if (newPosition < oldPosition)
            {
                foreach (var t in otherTasks.Where(t => t.DisplayOrder >= newPosition && t.DisplayOrder < oldPosition))
                    t.DisplayOrder++;
            }
        }

        private async Task ReorderAcrossColumnsAsync(
            Guid groupId, int oldStatusId, int newStatusId, Guid movingTaskId, int newPosition)
        {
            var oldColumnTasks = await _context.Tasks
                .Where(t => t.GroupId == groupId && t.StatusId == oldStatusId && t.Id != movingTaskId)
                .OrderBy(t => t.DisplayOrder)
                .ToListAsync();

            for (int i = 0; i < oldColumnTasks.Count; i++)
                oldColumnTasks[i].DisplayOrder = i;

            var newColumnTasks = await _context.Tasks
                .Where(t => t.GroupId == groupId && t.StatusId == newStatusId)
                .OrderBy(t => t.DisplayOrder)
                .ToListAsync();

            foreach (var t in newColumnTasks.Where(t => t.DisplayOrder >= newPosition))
                t.DisplayOrder++;
        }
    }
}