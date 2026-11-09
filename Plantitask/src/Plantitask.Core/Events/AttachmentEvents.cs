namespace Plantitask.Core.Events
{
    // Every attachment event carries AttachmentId, TaskId and GroupId.

    [EventName("attachment.uploaded.v1")]
    public record AttachmentUploaded : IEvent
    {
        public required Guid AttachmentId { get; init; }
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string FileName { get; init; }
    }

    // FileName travels because the attachment row is soft deleted by the time the drainer runs.
    [EventName("attachment.deleted.v1")]
    public record AttachmentDeleted : IEvent
    {
        public required Guid AttachmentId { get; init; }
        public required Guid TaskId { get; init; }
        public required Guid GroupId { get; init; }
        public required string FileName { get; init; }
    }
}
