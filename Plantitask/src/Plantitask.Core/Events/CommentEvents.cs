namespace Plantitask.Core.Events
{
    // Every comment event carries CommentId, TaskId and GroupId.

    [EventName("comment.added.v1")]
    public record CommentAdded : IEvent
    {
        public required Guid CommentId { get; init; }
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Content { get; init; }
        public required string TaskTitle { get; init; }
        public required Guid? TaskAssigneeId { get; init; }
        public required Guid TaskCreatedById { get; init; }
    }

    [EventName("comment.edited.v1")]
    public record CommentEdited : IEvent
    {
        public required Guid CommentId { get; init; }
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string Content { get; init; }
    }

    [EventName("comment.deleted.v1")]
    public record CommentDeleted : IEvent
    {
        public required Guid CommentId { get; init; }
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
    }
}
