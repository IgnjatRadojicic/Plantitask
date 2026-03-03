using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskManagement.Core.DTO.Kanban;
using TaskManagement.Core.DTO.Tasks;
using TaskManagement.Core.Entities;
using TaskManagement.Core.Enums;
using TaskManagement.Core.Interfaces;
using TaskManagement.Core.Constants;


namespace TaskManagement.Infrastructure.Services
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

        public async Task<TaskDto> CreateTaskAsync(Guid groupId, CreateTaskDto createTaskDto, Guid userId)
        {
            _logger.LogInformation("User {UserId} creating task in group {GroupId}", userId, groupId);



            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You must be a member of this group to create tasks");
            }

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead)
            {
                throw new UnauthorizedAccessException("Only Team Leads, Managers, and Owners can create tasks");
            }

            var priorityExists = await _context.TaskPriorities
                .AnyAsync(p => p.Id == createTaskDto.PriorityId && p.IsActive);

            if (!priorityExists)
            {
                throw new InvalidOperationException("Invalid priority selected");
            }

            if (createTaskDto.AssignedToUserId.HasValue)
            {
                var assigneeIsMember = await _context.GroupMembers
                    .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == createTaskDto.AssignedToUserId.Value);

                if (!assigneeIsMember)
                {
                    throw new InvalidOperationException("Cannot assign task to user who is not a group member");
                }
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

            if (task.DueDate.HasValue && task.AssignedToId.HasValue)
            {
                _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, task.AssignedToId.Value, task.DueDate.Value);
            }

            _logger.LogInformation("Task {TaskId} created in group {GroupId} by user {UserId}",
                task.Id, groupId, userId);

            return await GetTaskByIdAsync(task.Id, userId);

        }

        public async Task<List<TaskDto>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter, Guid userId)
        {
            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You must be a member of this group to view its tasks");
            }

            var query = _context.Tasks
                .Where(t => t.GroupId == groupId)
                .AsQueryable();

            if (filter != null)
            {
                if (filter.StatusId.HasValue)
                {
                    query = query.Where(t => t.StatusId == filter.StatusId.Value);
                }
                if (filter.PriorityId.HasValue)
                {
                    query = query.Where(t => t.PriorityId == filter.PriorityId.Value);
                }
                if (filter.AssignedToUserId.HasValue)
                {
                    query = query.Where(t => t.AssignedToId == filter.AssignedToUserId.Value);
                }

                if (filter.IsOverDue == true)
                {
                    query = query.Where(t => t.DueDate.HasValue &&
                                            t.DueDate.Value < DateTime.UtcNow &&
                                            t.StatusId != 4);
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

        public async Task<TaskDto> GetTaskByIdAsync(Guid taskId, Guid userId)
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
            {
                throw new KeyNotFoundException("Task not found");
            }

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You must be a member of the group to view this task");
            }

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

        public async Task<TaskDto> UpdateTaskAsync(Guid taskId, UpdateTaskDto updateTaskDto, Guid userId)
        {
            var task = await _context.Tasks
                 .Include(t => t.Group)
                 .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
            {
                throw new KeyNotFoundException("Task not found");
            }


            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You must be a member of this group");
            }

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead && task.CreatedBy != userId)
            {
                throw new UnauthorizedAccessException("Only the task creator or Team Leads and above can change task priority");
            }

            if (!string.IsNullOrWhiteSpace(updateTaskDto.Title))
            {
                task.Title = updateTaskDto.Title;
            }

            if (!string.IsNullOrWhiteSpace(updateTaskDto.Description))
            {
                task.Description = updateTaskDto.Description;
            }

            if (updateTaskDto.PriorityId.HasValue)
            {
                var priorityExists = await _context.TaskPriorities
                    .AnyAsync(p => p.Id == updateTaskDto.PriorityId.Value && p.IsActive);

                if (!priorityExists)
                {
                    throw new InvalidOperationException("Invalid priority selected");
                }

                task.PriorityId = updateTaskDto.PriorityId.Value;
            }

            if (updateTaskDto.DueDate.HasValue)
            {
                task.DueDate = updateTaskDto.DueDate.Value;
            }

            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (task.DueDate.HasValue && task.AssignedToId.HasValue)
            {
                _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, task.AssignedToId.Value, task.DueDate.Value);
            }

            _logger.LogInformation("Task {TaskId} updated by user {UserId}", taskId, userId);

            return await GetTaskByIdAsync(taskId, userId);
        }

        public async Task<TaskStatusChangeResult> ChangeTaskStatusAsync(Guid taskId, ChangeTaskStatusDto statusDto, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Status)
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
            {
                throw new KeyNotFoundException("Task not found");
            }

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You must be a member of this group");
            }

            var canChangeStatus = membership.Role.PermissionLevel >= PermissionLevels.TeamLead || task.AssignedToId == userId || task.CreatedBy == userId;

            if (!canChangeStatus)
            {
                throw new UnauthorizedAccessException("You don't have permission to change this task's status");
            }

            var oldStatus = task.Status.DisplayName;

            var statusExists = await _context.TaskStatuses
                .AnyAsync(s => s.Id == statusDto.NewStatusId && s.IsActive);

            if (!statusExists)
            {
                throw new InvalidOperationException("Invalid status selected");
            }

            var newStatus = await _context.TaskStatuses
                .FirstOrDefaultAsync(s => s.Id == statusDto.NewStatusId);

            task.StatusId = statusDto.NewStatusId;

            if (statusDto.NewStatusId == (int)TaskStatusItem.Completed)
            {
                task.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                task.CompletedAt = null;
            }

            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} status changed to {StatusId} by user {UserId}",
                taskId, statusDto.NewStatusId, userId);

            var taskDto = await GetTaskByIdAsync(taskId, userId);

            return new TaskStatusChangeResult
            {
                Task = taskDto,
                OldStatus = oldStatus,
                NewStatus = newStatus.DisplayName
            };
        }

        public async Task<TaskPriorityChangeResult> ChangeTaskPriorityAsync(Guid taskId, int newPriorityId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Priority)
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
            {
                throw new KeyNotFoundException("Task not found");
            }

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You must be a member of this group");
            }

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead)
            {
                throw new UnauthorizedAccessException("Only Team Leads and above can change task priority");
            }

            var oldPriority = task.Priority.Name;

            var priorityExists = await _context.TaskPriorities
                .AnyAsync(p => p.Id == newPriorityId && p.IsActive);

            if (!priorityExists)
            {
                throw new InvalidOperationException("Invalid priority selected");
            }

            var newPriority = await _context.TaskPriorities
                .FirstOrDefaultAsync(p => p.Id == newPriorityId);

            task.PriorityId = newPriorityId;
            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} priority changed to {PriorityId} by user {UserId}",
                taskId, newPriorityId, userId);

            var taskDto = await GetTaskByIdAsync(taskId, userId);

            return new TaskPriorityChangeResult
            {
                Task = taskDto,
                OldPriority = oldPriority,
                NewPriority = newPriority.Name
            };
        }

        public async Task AssignTaskAsync(Guid taskId, AssignTaskDto assignDto, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
            {
                throw new KeyNotFoundException("Task not found");
            }

            if (task.DueDate.HasValue)
            {
                _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, assignDto.UserId, task.DueDate.Value);
            }

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You must be a member of this group");
            }

            if (membership.Role.PermissionLevel < PermissionLevels.TeamLead)
            {
                throw new UnauthorizedAccessException("Only Team Leads and above can assign tasks");
            }
            var assigneeIsMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == assignDto.UserId);

            if (!assigneeIsMember)
            {
                throw new InvalidOperationException("Cannot assign task to user who is not a group member");
            }

            task.AssignedToId = assignDto.UserId;
            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} assigned to user {AssignedUserId} by {UserId}",
                taskId, assignDto.UserId, userId);
        }

        public async Task UnassignTaskAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .Include(t => t.Attachments)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
            {
                throw new KeyNotFoundException("Task not found");
            }
            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You must be a member of this group");
            }

            var canUnassign = membership.Role.PermissionLevel >= PermissionLevels.TeamLead || task.AssignedToId == userId;

            if (!canUnassign)
            {
                throw new UnauthorizedAccessException("Only Team Leads and above or the assigned user can unassign tasks");
            }

            task.AssignedToId = null;
            task.UpdatedBy = userId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} unassigned by user {UserId}", taskId, userId);
        }

        public async Task DeleteTaskAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
            {
                throw new KeyNotFoundException("Task not found");
            }
            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You must be a member of this group");
            }

            if (membership.Role.PermissionLevel < PermissionLevels.Manager)
            {
                throw new UnauthorizedAccessException("Only Managers and Owners can delete tasks");
            }

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
        }


        public async Task<List<Guid>> GetTaskGroupMembersAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
            {
                throw new KeyNotFoundException("Task not found");
            }

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You must be a member of this group");
            }

            return await _context.GroupMembers
            .Where(gm => gm.GroupId == task.GroupId)
            .Select(gm => gm.UserId)
            .ToListAsync();
        }

        public async Task<KanbanBoardDto> GetKanbanBoardAsync(Guid groupId, Guid userId)
        {
            var membership = await _context.GroupMembers
                .AsNoTracking()
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (membership == null)
                throw new UnauthorizedAccessException("You are not a member of this group");

            var group = await _context.Groups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
                throw new KeyNotFoundException("Group not found");

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

            var board = new KanbanBoardDto
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

            return board;
        }

        public async Task MoveTaskAsync(Guid taskId, MoveTaskDto moveDto, Guid userId)
        {
            const int maxRetries = 3;
            int attempt = 0;
        }
    }
}

