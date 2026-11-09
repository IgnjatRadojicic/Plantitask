namespace Plantitask.Core.Events
{
    // Premium events belong to no group. A webhook raises them with no actor in the envelope, so
    // the user is always in the payload.

    public enum PremiumKind
    {
        Recurring = 1,
        OneTime = 2,
    }

    [EventName("premium.activated.v1")]
    public record PremiumActivated : IEvent
    {
        public required Guid UserId { get; init; }
        public required PremiumKind Kind { get; init; }
    }

    [EventName("premium.cancelled.v1")]
    public record PremiumCancelled : IEvent
    {
        public required Guid UserId { get; init; }
    }
}
