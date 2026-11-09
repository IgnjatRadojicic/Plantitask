namespace Plantitask.Core.Events
{
    // Every group event carries GroupId. A membership event also carries the member's UserId.

    [EventName("group.created.v1")]
    public record GroupCreated : IEvent
    {
        public required Guid GroupId { get; init; }
        public required string Name { get; init; }
    }

    [EventName("group.updated.v1")]
    public record GroupUpdated : IEvent
    {
        public required Guid GroupId { get; init; }
    }

    [EventName("group.member-joined.v1")]
    public record GroupMemberJoined : IEvent
    {
        public required Guid GroupId { get; init; }
        public required Guid UserId { get; init; }
    }

    [EventName("group.member-role-changed.v1")]
    public record GroupMemberRoleChanged : IEvent
    {
        public required Guid GroupId { get; init; }
        public required Guid UserId { get; init; }
        public required int OldRoleId { get; init; }
        public required int NewRoleId { get; init; }
    }

    [EventName("group.member-removed.v1")]
    public record GroupMemberRemoved : IEvent
    {
        public required Guid GroupId { get; init; }
        public required Guid UserId { get; init; }
    }

    [EventName("group.member-left.v1")]
    public record GroupMemberLeft : IEvent
    {
        public required Guid GroupId { get; init; }
        public required Guid UserId { get; init; }
    }
}
