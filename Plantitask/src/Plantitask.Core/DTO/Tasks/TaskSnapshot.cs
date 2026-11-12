namespace Plantitask.Core.DTO.Tasks
{
    // The task as it was when the change happened: the only fields notifications read.
    public record TaskSnapshot
    {
        public required Guid Id { get; init; }
        public required string Title { get; init; }
        public required Guid? AssigneeId { get; init; }
        public required Guid CreatedById { get; init; }
    }
}
