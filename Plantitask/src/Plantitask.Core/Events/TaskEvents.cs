namespace Plantitask.Core.Events
{
    // Every task event carries TaskId and GroupId. A value a handler needs that the change itself
    // overwrites, like the previous status, travels in the event because the row no longer has it
    // by the time the drainer runs.

    [EventName("task.created.v1")]
    public record TaskCreated : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Title { get; init; }
        public required int StatusId { get; init; }
        public required Guid? AssigneeId { get; init; }
    }

    [EventName("task.updated.v1")]
    public record TaskUpdated : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Title { get; init; }
        public required Guid? AssigneeId { get; init; }
    }

    [EventName("task.status-changed.v1")]
    public record TaskStatusChanged : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Title { get; init; }
        public required Guid? AssigneeId { get; init; }
        public required Guid CreatedById { get; init; }
        public required int OldStatusId { get; init; }
        public required string OldStatusName { get; init; }
        public required int NewStatusId { get; init; }
        public required string NewStatusName { get; init; }
    }

    [EventName("task.priority-changed.v1")]
    public record TaskPriorityChanged : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Title { get; init; }
        public required Guid? AssigneeId { get; init; }
        public required int OldPriorityId { get; init; }
        public required string OldPriorityName { get; init; }
        public required int NewPriorityId { get; init; }
        public required string NewPriorityName { get; init; }
    }

    // Raised only when the assignee actually changes, so assigning the same person again sends nothing.
    [EventName("task.assigned.v1")]
    public record TaskAssigned : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Title { get; init; }
        public required Guid AssigneeId { get; init; }
        public required string AssigneeName { get; init; }
        public required string GroupName { get; init; }
    }

    [EventName("task.unassigned.v1")]
    public record TaskUnassigned : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Title { get; init; }
        public required Guid PreviousAssigneeId { get; init; }
        public required string PreviousAssigneeName { get; init; }
    }

    [EventName("task.deleted.v1")]
    public record TaskDeleted : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Title { get; init; }
        public required int StatusId { get; init; }
    }

    [EventName("task.moved.v1")]
    public record TaskMoved : IEvent
    {
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required int OldStatusId { get; init; }
        public required string OldStatusName { get; init; }
        public required int NewStatusId { get; init; }
        public required string NewStatusName { get; init; }
        public required int NewDisplayOrder { get; init; }
    }
}
