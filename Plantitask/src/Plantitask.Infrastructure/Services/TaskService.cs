using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Projections;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using System.Text.RegularExpressions;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Task CRUD, assignment, status flow and the kanban board. Authorization is rank-based
    /// through GroupService: creating, assigning and priority changes need TeamLead and above,
    /// deleting needs Manager, and status changes are open to the assignee and the creator too.
    /// </summary>
    public class TaskService : ITaskService
    {
        private readonly IApplicationDbContext _context;
        private readonly ILogger<TaskService> _logger;
        private readonly IBackgroundJobService _backgroundJobService;
        private readonly IGroupService _groupService;
        private readonly IMemoryCache _cache;

        public TaskService(
            IApplicationDbContext context,
            ILogger<TaskService> logger,
            IGroupService groupService,
            IMemoryCache cache,
            IBackgroundJobService backgroundJobService)
        {
            _cache = cache;
            _context = context;
            _groupService = groupService;
            _logger = logger;
            _backgroundJobService = backgroundJobService;
        }

        /// <summary>
        /// Creates a task at the end of the Not Started column, TeamLead and above. The assignee
        /// (when given) must be a group member. A due-soon reminder is scheduled before the save
        /// so its Hangfire JobId lands in the same insert; if the save then fails, the orphaned
        /// job fires against a missing task and no-ops.
        /// </summary>
        public async Task<Result<TaskDto>> CreateTaskAsync(Guid groupId, CreateTaskDto createTaskDto, Guid userId)
        {
            _logger.LogInformation("User {UserId} creating task in group {GroupId}", userId, groupId);

            var callerRole = await _groupService.GetUserRoleAsync(groupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group to create tasks");

            if (callerRole < GroupRole.TeamLead)
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

            var displayOrder = await NextDisplayOrderAsync(groupId, (int)TaskStatusItem.NotStarted);
            var task = new TaskItem
            {
                Title = createTaskDto.Title,
                Description = createTaskDto.Description,
                GroupId = groupId,
                StatusId = (int)TaskStatusItem.NotStarted,
                PriorityId = createTaskDto.PriorityId,
                DueDate = createTaskDto.DueDate,
                DisplayOrder = displayOrder,
                AssignedToId = createTaskDto.AssignedToUserId,
                CreatedBy = userId,
            };

            if (task.DueDate.HasValue && task.AssignedToId.HasValue)
            {
                try
                {
                    task.DueSoonJobId = await _backgroundJobService.ScheduleTaskDueSoonNotification(
                        task.Id, task.AssignedToId.Value, task.DueDate.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to schedule due date notification for task {TaskId}", task.Id);
                }
            }

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            var result = await GetTaskByIdAsync(task.Id, userId);

            _logger.LogInformation("Task {TaskId} created in group {GroupId} by user {UserId}",
                task.Id, groupId, userId);

            return result;
        }

        /// <summary>
        /// A group's tasks for a member, filterable by status, priority, assignee, overdue and a
        /// case-insensitive search over title and description. Paged through the shared
        /// PaginatedList contract, newest first.
        /// </summary>
        public async Task<Result<PaginatedList<TaskDto>>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter, Guid userId, int pageNumber = 1, int pageSize = 50)
        {
            var isMember = await _groupService.IsUserMemberAsync(groupId, userId);

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
                    var pattern = $"%{filter.SearchTerm}%";
                    query = query.Where(t =>
                    EF.Functions.ILike(t.Title, pattern) ||
                    (t.Description != null && EF.Functions.ILike(t.Description, pattern)));
                }
            }

            return await query.OrderByDescending(t => t.CreatedAt)
                .Select(TaskProjections.ToTaskDto)
                .ToPaginatedListAsync(pageNumber, pageSize);
        }

        /// <summary>
        /// One task through the shared projection. Doubles as the internal "re-fetch after
        /// mutation" helper because the DTO needs joined display names anyway.
        /// </summary>
        public async Task<Result<TaskDto>> GetTaskByIdAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Where(t => t.Id == taskId)
                .Select(TaskProjections.ToTaskDto)
                .FirstOrDefaultAsync();

            if (task == null)
                return Error.NotFound("Task not found");

            var isMember = await _groupService.IsUserMemberAsync(task.GroupId, userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to view this task");

            return task;
        }

        /// <summary>
        /// Edits title, description, priority and due date - for the creator or TeamLead and
        /// above. Null description means "leave it alone" while whitespace clears it, and
        /// ClearDueDate exists because null cannot mean both. Any change reschedules the
        /// due-soon reminder from scratch.
        /// </summary>
        public async Task<Result<TaskDto>> UpdateTaskAsync(Guid taskId, UpdateTaskDto updateTaskDto, Guid userId)
        {
            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var callerRole = await _groupService.GetUserRoleAsync(task.GroupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group");

            if (callerRole < GroupRole.TeamLead && task.CreatedBy != userId)
                return Error.Forbidden("Only the task creator or Team Leads and above can update this task");

            if (!string.IsNullOrWhiteSpace(updateTaskDto.Title))
                task.Title = updateTaskDto.Title;

            if (updateTaskDto.Description != null)
            {
                task.Description = string.IsNullOrWhiteSpace(updateTaskDto.Description)
                    ? null
                    : updateTaskDto.Description;
            }

            if (updateTaskDto.PriorityId.HasValue)
            {
                var priorityExists = await _context.TaskPriorities
                    .AnyAsync(p => p.Id == updateTaskDto.PriorityId.Value && p.IsActive);

                if (!priorityExists)
                    return Error.BadRequest("Invalid priority selected");

                task.PriorityId = updateTaskDto.PriorityId.Value;
            }

            if (updateTaskDto.ClearDueDate)
                task.DueDate = null;
            else if (updateTaskDto.DueDate.HasValue)
                task.DueDate = updateTaskDto.DueDate.Value; 

            task.UpdatedBy = userId;


            if (task.DueSoonJobId != null)
            {
                _backgroundJobService.CancelScheduledJob(task.DueSoonJobId);
            }

            task.DueSoonJobId = task.DueDate.HasValue && task.AssignedToId.HasValue
                ? await _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, task.AssignedToId.Value, task.DueDate.Value)
                : null;

            await _context.SaveChangesAsync();

            var result = await GetTaskByIdAsync(task.Id, userId);

            _logger.LogInformation("Task {TaskId} updated by user {UserId}", taskId, userId);

            return result;
        }

        /// <summary>
        /// Moves a task to another status for TeamLead and above, the assignee or the creator.
        /// Returns a result record carrying the old and new status names so the controller can
        /// notify without re-reading state. CompletedAt is set only on first completion and
        /// cleared when the task leaves Completed.
        /// </summary>
        public async Task<Result<TaskStatusChangeResult>> ChangeTaskStatusAsync(Guid taskId, ChangeTaskStatusDto statusDto, Guid userId)
        {
            var row = await _context.Tasks
                .Where(t => t.Id == taskId)
                .Select(t => new { Task = t, OldStatus = t.Status.DisplayName })
                .FirstOrDefaultAsync();

            if (row == null)
                return Error.NotFound("Task not found");

            var task = row.Task;

            var callerRole = await _groupService.GetUserRoleAsync(task.GroupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group");

            var canChangeStatus = callerRole >= GroupRole.TeamLead
                || task.AssignedToId == userId
                || task.CreatedBy == userId;

            if (!canChangeStatus)
                return Error.Forbidden("You don't have permission to change this task's status");

            var oldStatus = row.OldStatus;

            var newStatus = await _context.TaskStatuses
                .Where(s => s.Id == statusDto.NewStatusId && s.IsActive)
                .Select(s => s.DisplayName)
                .FirstOrDefaultAsync();

            if (newStatus == null)
                return Error.BadRequest("Invalid status selected");

            var oldStatusId = task.StatusId;

            if (oldStatusId == statusDto.NewStatusId)
                return Error.BadRequest("Task is already in that status");

            task.StatusId = statusDto.NewStatusId;          

            if (statusDto.NewStatusId == (int)TaskStatusItem.Completed)
                task.CompletedAt ??= DateTime.UtcNow;
            else if (oldStatusId == (int)TaskStatusItem.Completed)
                task.CompletedAt = null;
            task.UpdatedBy = userId;

            task.DisplayOrder = await NextDisplayOrderAsync(task.GroupId, statusDto.NewStatusId);

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
                NewStatus = newStatus
            };
        }

        /// <summary>
        /// Changes priority, TeamLead and above. Same result-record pattern as the status
        /// change: old and new display names travel back with the task.
        /// </summary>
        public async Task<Result<TaskPriorityChangeResult>> ChangeTaskPriorityAsync(Guid taskId, int newPriorityId, Guid userId)
        {
            var row = await _context.Tasks
                .Where(t => t.Id == taskId)
                .Select(t => new { Task = t, OldPriority = t.Priority.DisplayName })
                .FirstOrDefaultAsync();

            if (row == null)
                return Error.NotFound("Task not found");

            var task = row.Task;

            var callerRole = await _groupService.GetUserRoleAsync(task.GroupId, userId);


            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group");

            if (callerRole < GroupRole.TeamLead)
                return Error.Forbidden("Only Team Leads and above can change task priority");

            var oldPriority = row.OldPriority;

            var newPriority = await _context.TaskPriorities
                .Where(p => p.Id == newPriorityId && p.IsActive)
                .Select(p => p.DisplayName)
                .FirstOrDefaultAsync();

            if (newPriority == null)
                return Error.BadRequest("Invalid priority selected");

            task.PriorityId = newPriorityId;
            task.UpdatedBy = userId;
            

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
                NewPriority = newPriority
            };
        }

        /// <summary>
        /// Assigns the task to a group member, TeamLead and above, and reschedules the due-soon
        /// reminder for the new assignee.
        /// </summary>
        public async Task<Result> AssignTaskAsync(Guid taskId, AssignTaskDto assignDto, Guid userId)
        {
            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var callerRole = await _groupService.GetUserRoleAsync(task.GroupId, userId);


            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group");

            if (callerRole < GroupRole.TeamLead)
                return Error.Forbidden("Only Team Leads and above can assign tasks");

            var assigneeIsMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == assignDto.UserId);

            if (!assigneeIsMember)
                return Error.BadRequest("Cannot assign task to user who is not a group member");

            task.AssignedToId = assignDto.UserId;
            task.UpdatedBy = userId;

            if (task.DueSoonJobId != null)
            {
                _backgroundJobService.CancelScheduledJob(task.DueSoonJobId);
            }

            task.DueSoonJobId = task.DueDate.HasValue && task.AssignedToId.HasValue
             ? await _backgroundJobService.ScheduleTaskDueSoonNotification(task.Id, task.AssignedToId.Value, task.DueDate.Value)
             : null;

            await _context.SaveChangesAsync();



            _logger.LogInformation("Task {TaskId} assigned to user {AssignedUserId} by {UserId}",
                taskId, assignDto.UserId, userId);

            return Result.Success();
        }

        /// <summary>
        /// Clears the assignee - TeamLead and above, or the assignee taking themselves off.
        /// </summary>
        public async Task<Result> UnassignTaskAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var callerRole = await _groupService.GetUserRoleAsync(task.GroupId, userId);



            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group");

            var canUnassign = callerRole >= GroupRole.TeamLead
                || task.AssignedToId == userId;

            if (!canUnassign)
                return Error.Forbidden("Only Team Leads and above or the assigned user can unassign tasks");

            task.AssignedToId = null;
            task.UpdatedBy = userId;
            

            await _context.SaveChangesAsync();

            _logger.LogInformation("Task {TaskId} unassigned by user {UserId}", taskId, userId);

            return Result.Success();
        }

        /// <summary>
        /// Soft-deletes a task and cascades to its comments, attachments and notifications in
        /// one transaction, Manager and above. The bulk updates set UpdatedAt by hand because
        /// ExecuteUpdate bypasses the SaveChanges stamping override.
        /// </summary>
        public async Task<Result> DeleteTaskAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var callerRole = await _groupService.GetUserRoleAsync(task.GroupId, userId);


            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group");

            if (callerRole < GroupRole.Manager)
                return Error.Forbidden("Only Managers and Owners can delete tasks");

            var now = DateTime.UtcNow;

            await using var transaction = await _context.BeginTransactionAsync();


            await _context.Notifications
                .IgnoreQueryFilters()
                .Where(n => n.RelatedEntityId == taskId && !n.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsDeleted, true)
                    .SetProperty(n => n.DeletedAt, now)
                    .SetProperty(n => n.DeletedBy, userId)
                    .SetProperty(n => n.UpdatedAt, now));

            await _context.TaskComments
                .IgnoreQueryFilters()
                .Where(tc => tc.TaskId == taskId && !tc.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.IsDeleted, true)
                    .SetProperty(c => c.DeletedAt, now)
                    .SetProperty(c => c.DeletedBy, userId)
                    .SetProperty(c => c.UpdatedAt, now));

            await _context.TaskAttachments
                .IgnoreQueryFilters()
                .Where(ta => ta.TaskId == taskId && !ta.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.IsDeleted, true)
                    .SetProperty(a => a.DeletedAt, now)
                    .SetProperty(a => a.DeletedBy, userId)
                    .SetProperty(a => a.UpdatedAt, now));

            task.IsDeleted = true;
            task.DeletedAt = now;
            task.DeletedBy = userId;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Task {TaskId} deleted by user {UserId}", taskId, userId);

            return Result.Success();
        }

        /// <summary>
        /// The member ids of the task's group, used by SignalR to know who may hear about this
        /// task. Caller must be a member themselves.
        /// </summary>
        public async Task<Result<List<Guid>>> GetTaskGroupMembersAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var isMember = await _groupService.IsUserMemberAsync(task.GroupId, userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of this group");

            var memberIds = await _context.GroupMembers
                .Where(gm => gm.GroupId == task.GroupId)
                .Select(gm => gm.UserId)
                .ToListAsync();

            return memberIds;
        }

        /// <summary>
        /// Builds the whole board for a member: one projected query for the group's tasks,
        /// grouped in memory into columns. The status lookup is served from a process-wide
        /// cache because statuses are static seed data.
        /// </summary>
        public async Task<Result<KanbanBoardDto>> GetKanbanBoardAsync(Guid groupId, Guid userId)
        {
            var callerRole = await _groupService.GetUserRoleAsync(groupId, userId);


            if (callerRole == null)
                return Error.Forbidden("You are not a member of this group");

            var group = await _context.Groups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
                return Error.NotFound("Group not found");

            var statuses = await _cache.GetOrCreateAsync("task-statuses", async _ =>
                await _context.TaskStatuses
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.DisplayOrder)
                .ToListAsync());

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
                    PriorityName = t.Priority.DisplayName,
                    PriorityColor = t.Priority.Color,
                    AssignedToProfilePicturePath = t.AssignedTo != null ? t.AssignedTo.ProfilePicturePath : null,
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

        /// <summary>
        /// Handles a kanban drag. Same-column reorders are open to any member; a cross-column
        /// drag is a status change and answers to the ChangeTaskStatusAsync rule. Concurrent
        /// drags trip the xmin token, so the whole operation retries up to three times before
        /// giving up with a conflict. The per-drag renumbering is slated to be replaced by
        /// sparse ranks (frontend/07-kanban-move-ranks.md), which also removes this retry loop.
        /// </summary>
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
                        .FirstOrDefaultAsync(t => t.Id == taskId);

                    if (task == null)
                        return Error.NotFound("Task not found");

                    var callerRole = await _groupService.GetUserRoleAsync(task.GroupId, userId);

                    if (callerRole == null)
                        return Error.Forbidden("You don't have permission to move tasks");

                    var oldStatusId = task.StatusId;
                    var oldDisplayOrder = task.DisplayOrder;

                    // A cross column drag is a status change so it answers to the same rule as
                    // ChangeTaskStatusAsync. Reordering inside a column stays open to members.
                    if (oldStatusId != moveDto.NewStatusId)
                    {
                        var canChangeStatus = callerRole >= GroupRole.TeamLead
                            || task.AssignedToId == userId
                            || task.CreatedBy == userId;

                        if (!canChangeStatus)
                            return Error.Forbidden("You don't have permission to change this task's status");
                    }

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

        /// <summary>
        /// Shifts the tasks between the old and new position by one so the moved task can slot
        /// in without leaving duplicates or gaps.
        /// </summary>
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

        /// <summary>
        /// Renumbers the source column to close the gap and shifts the target column open at the
        /// insertion point.
        /// </summary>
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

        /// <summary>
        /// The next free slot at the bottom of a column. The nullable Max cast is what makes an
        /// empty column return -1 instead of throwing.
        /// </summary>
        private async Task<int> NextDisplayOrderAsync(Guid groupId, int statusId)
        {
            var maxOrder = await _context.Tasks
            .Where(t => t.GroupId == groupId && t.StatusId == statusId)
            .MaxAsync(t => (int?)t.DisplayOrder) ?? -1;

            return maxOrder + 1;
        }
    }
}