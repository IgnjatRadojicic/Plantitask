using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.Constants;
using Plantitask.Core.DTO.Dashboard;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Domain;
using Plantitask.Core.Projections;

namespace Plantitask.Infrastructure.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IApplicationDbContext _context;
        private readonly IGroupService _groupService;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(
            IApplicationDbContext context,
            IGroupService groupService,
            ILogger<DashboardService> logger)
        {
            _context = context;
            _groupService = groupService;
            _logger = logger;
        }

        public async Task<Result<PersonalDashboardDto>> GetPersonalDashboardAsync(Guid userId)
        {
            var now = DateTime.UtcNow;
            var todayEnd = now.Date.AddDays(1);
            var weekEnd = now.Date.AddDays(7);
            var sevenDaysAgo = now.AddDays(-7);

            var userGroupIds = await _context.GroupMembers
                .Where(gm => gm.UserId == userId)
                .Select(gm => gm.GroupId)
                .ToListAsync();

            const int TrendWindowDays = 30;
            var trendStart = now.Date.AddDays(-(TrendWindowDays - 1));

            // One bounded slice instead of every task ever assigned: the open tasks that can
            // land in a due bucket, plus the completions the trend window can reach
            var relevantTasks = await _context.Tasks
                .Where(t => t.AssignedToId == userId
                    && ((t.StatusId != (int)TaskStatusItem.Completed
                            && t.DueDate.HasValue
                            && t.DueDate.Value < weekEnd)
                        || (t.StatusId == (int)TaskStatusItem.Completed
                            && t.CompletedAt.HasValue
                            && t.CompletedAt.Value >= trendStart)))
                .Select(TaskProjections.ToTaskSummary)
                .ToListAsync();

            var openWithDueDate = relevantTasks
                .Where(t => t.CompletedAt == null)
                .OrderBy(t => t.DueDate)
                .ToList();

            var overdueTasks = openWithDueDate
                .Where(t => t.DueDate!.Value < now)
                .ToList();

            var dueToday = openWithDueDate
                .Where(t => t.DueDate!.Value >= now && t.DueDate.Value < todayEnd)
                .ToList();

            var dueThisWeek = openWithDueDate
                .Where(t => t.DueDate!.Value >= todayEnd)
                .ToList();

            var completedInTrendWindow = relevantTasks
                .Where(t => t.CompletedAt != null)
                .OrderByDescending(t => t.CompletedAt)
                .ToList();

            var recentlyCompleted = completedInTrendWindow
                .Where(t => t.CompletedAt!.Value >= sevenDaysAgo)
                .ToList();

            var counts = await _context.Tasks
                .Where(t => t.AssignedToId == userId)
                .GroupBy(t => 1)
                .Select(g => new
                {
                    Open = g.Count(t => t.StatusId != (int)TaskStatusItem.Completed),
                    Completed = g.Count(t => t.StatusId == (int)TaskStatusItem.Completed)
                })
                .FirstOrDefaultAsync();

            var recentActivity = userGroupIds.Count == 0
                ? new List<ActivityDto>()
                : await _context.AuditLogs
                .Where(a => a.GroupId.HasValue && userGroupIds.Contains(a.GroupId.Value))
                .OrderByDescending(a => a.CreatedAt)
                .Take(15)
                .Select(a => new ActivityDto
                {
                    UserName = a.UserName,
                    Action = a.Action,
                    EntityType = a.EntityType,
                    Timestamp = a.CreatedAt
                })
                .ToListAsync();


            var completedByDate = completedInTrendWindow
                .GroupBy(t => t.CompletedAt!.Value.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            var completionTrend = Enumerable.Range(0, TrendWindowDays)
                .Select(i => trendStart.AddDays(i))
                .Select(date => new TrendPointDto
                {
                    Date = date,
                    CompletedCount = completedByDate.GetValueOrDefault(date, 0)
                })
                .ToList();

            return new PersonalDashboardDto
            {
                OverdueTasks = overdueTasks,
                DueToday = dueToday,
                DueThisWeek = dueThisWeek,
                RecentlyCompleted = recentlyCompleted,
                RecentActivity = recentActivity,
                CompletionTrend = completionTrend,
                TotalAssignedTasks = counts?.Open ?? 0,
                TotalCompletedTasks = counts?.Completed ?? 0,
                GroupCount = userGroupIds.Count
            };
        }

        public async Task<Result<List<FieldTreeDto>>> GetFieldDataAsync(Guid userId)
        {
            var userGroups = await _context.GroupMembers
                .Where(gm => gm.UserId == userId)
                .Select(gm => gm.Group)
                .ToListAsync();

            var userGroupIds = userGroups.Select(g => g.Id).ToList();

            var taskStats = await _context.Tasks
                .Where(t => userGroupIds.Contains(t.GroupId))
                .GroupBy(t => t.GroupId)
                .Select(g => new
                {
                    GroupId = g.Key,
                    TotalTasks = g.Count(),
                    CompletedTasks = g.Count(t => t.StatusId == (int)TaskStatusItem.Completed)
                })
                .ToDictionaryAsync(x => x.GroupId);

            var memberCounts = await _context.GroupMembers
                .Where(gm => userGroupIds.Contains(gm.GroupId))
                .GroupBy(gm => gm.GroupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.GroupId);

            var result = userGroups.Select(group =>
            {
                var stats = taskStats.GetValueOrDefault(group.Id);
                var totalTasks = stats?.TotalTasks ?? 0;
                var completedTasks = stats?.CompletedTasks ?? 0;
                var completionPercentage = totalTasks > 0
                    ? Math.Round((double)completedTasks / totalTasks * 100, 1)
                    : 0;

                return new FieldTreeDto
                {
                    GroupId = group.Id,
                    GroupName = group.Name,
                    CompletionPercentage = completionPercentage,
                    CurrentTreeStage = TreeProgressCalculator.CalculateStage(completionPercentage),
                    MemberCount = memberCounts.GetValueOrDefault(group.Id)?.Count ?? 0,
                    TotalTasks = totalTasks,
                    CompletedTasks = completedTasks
                };
            }).ToList();

            return result;
        }

        public async Task<Result<GroupStatisticsDto>> GetGroupStatisticsAsync(Guid groupId, Guid userId)
        {
            if (!await _groupService.IsUserMemberAsync(groupId, userId))
                return Error.Forbidden("You must be a member of this group");

            var groupName = await _context.Groups
                .Where(g => g.Id == groupId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync();

            if (groupName == null)
                return Error.NotFound("Group not found");

            var now = DateTime.UtcNow;

            // Every output of this method is an aggregate, so nothing here is ever returned to
            // the client as a row. Project the columns the aggregates read rather than
            // materialising and tracking whole tasks with three joined entities
            var tasks = await _context.Tasks
                .Where(t => t.GroupId == groupId)
                .Select(t => new
                {
                    t.StatusId,
                    StatusName = t.Status.DisplayName,
                    StatusColor = t.Status.Color,
                    PriorityName = t.Priority.DisplayName,
                    PriorityColor = t.Priority.Color,
                    t.AssignedToId,
                    AssigneeName = t.AssignedTo != null ? t.AssignedTo.UserName : null,
                    t.DueDate,
                    t.CompletedAt,
                    t.CreatedAt
                })
                .ToListAsync();

            var totalTasks = tasks.Count;
            var completedTasks = tasks.Count(t => t.StatusId == (int)TaskStatusItem.Completed);
            var inProgressTasks = tasks.Count(t => t.StatusId == (int)TaskStatusItem.InProgress);
            var notStartedTasks = tasks.Count(t => t.StatusId == (int)TaskStatusItem.NotStarted);
            var underReviewTasks = tasks.Count(t => t.StatusId == (int)TaskStatusItem.UnderReview);
            var overdueTasks = tasks.Count(t => t.DueDate.HasValue
                && t.DueDate.Value < now
                && t.StatusId != (int)TaskStatusItem.Completed);

            var completionPercentage = TreeProgressCalculator.CalculateCompletion(totalTasks, completedTasks);


            var completedWithDates = tasks
                .Where(t => t.CompletedAt.HasValue && t.CreatedAt != default)
                .ToList();

            double? averageCompletionDays = completedWithDates.Count > 0
                ? Math.Round(completedWithDates.Average(t => (t.CompletedAt!.Value - t.CreatedAt).TotalDays), 1)
                : null;

            var memberCount = await _context.GroupMembers
                .CountAsync(gm => gm.GroupId == groupId);

            var tasksByStatus = tasks
                .GroupBy(t => new { t.StatusName, Color = t.StatusColor })
                .Select(g => new StatusCountDto
                {
                    StatusName = g.Key.StatusName,
                    Color = g.Key.Color,
                    Count = g.Count()
                })
                .ToList();

            var tasksByPriority = tasks
                .GroupBy(t => new { t.PriorityName, Color = t.PriorityColor })
                .Select(g => new PriorityCountDto
                {
                    PriorityName = g.Key.PriorityName,
                    Color = g.Key.Color,
                    Count = g.Count()
                })
                .ToList();

            var memberWorkload = tasks
                .Where(t => t.AssignedToId.HasValue)
                .GroupBy(t => new { t.AssignedToId, UserName = t.AssigneeName ?? "Unassigned" })
                .Select(g => new MemberWorkloadDto
                {
                    UserName = g.Key.UserName,
                    AssignedCount = g.Count(t => t.StatusId != (int)TaskStatusItem.Completed),
                    CompletedCount = g.Count(t => t.StatusId == (int)TaskStatusItem.Completed),
                    OverdueCount = g.Count(t => t.DueDate.HasValue
                        && t.DueDate.Value < now
                        && t.StatusId != (int)TaskStatusItem.Completed)
                })
                .OrderByDescending(m => m.AssignedCount)
                .ToList();

            const int trendWindowDays = 30;
            var trendStart = now.Date.AddDays(-(trendWindowDays - 1));

            var completedByDate = tasks
                .Where(t => t.CompletedAt.HasValue
                    && t.CompletedAt.Value >= trendStart)
                .GroupBy(t => t.CompletedAt!.Value.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            var completionTrend = Enumerable.Range(0, trendWindowDays)
                .Select(i => trendStart.AddDays(i))
                .Select(date => new TrendPointDto
                {
                    Date = date,
                    CompletedCount = completedByDate.GetValueOrDefault(date, 0)
                })
                .ToList();

            return new GroupStatisticsDto
            {
                GroupId = groupId,
                GroupName = groupName,
                CompletionPercentage = completionPercentage,
                CurrentTreeStage = TreeProgressCalculator.CalculateStage(completionPercentage),
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                InProgressTasks = inProgressTasks,
                NotStartedTasks = notStartedTasks,
                UnderReviewTasks = underReviewTasks,
                OverdueTasks = overdueTasks,
                MemberCount = memberCount,
                AverageCompletionDays = averageCompletionDays,
                TasksByStatus = tasksByStatus,
                TasksByPriority = tasksByPriority,
                MemberWorkload = memberWorkload,
                CompletionTrend = completionTrend
            };
        }


        /// <summary>
        /// INTERNAL: no authorization. Only callable from TreeProgressBroadcaster, which emits
        /// into the membership-gated "group_{id}" room. Never expose via a controller without
        /// adding a membership check.
        /// </summary>
        public async Task<Result<FieldTreeDto>> GetGroupTreeProgressAsync(Guid groupId)
        {
            var groupName = await _context.Groups
                .Where(g => g.Id == groupId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync();

            if (groupName is null)
                return Error.NotFound("Group not found");

            var stats = await _context.Tasks
                .Where(t => t.GroupId == groupId)
                .GroupBy(t => t.GroupId)
                .Select(g => new
                {
                    Total = g.Count(),
                    Completed = g.Count(t => t.StatusId == (int)TaskStatusItem.Completed)
                })
                .FirstOrDefaultAsync();

            var memberCount = await _context.GroupMembers
                 .CountAsync(gm => gm.GroupId == groupId);

            var total = stats?.Total ?? 0;
            var completed = stats?.Completed ?? 0;
            var pct = TreeProgressCalculator.CalculateCompletion(total, completed);
            var stage = TreeProgressCalculator.CalculateStage(pct);

            return new FieldTreeDto
            {
                GroupId = groupId,
                GroupName = groupName,
                CompletionPercentage = pct,
                CurrentTreeStage = stage,
                MemberCount = memberCount,
                TotalTasks = total,
                CompletedTasks = completed
            };
        }

    }
}