namespace Plantitask.Core.Events
{
    // Account events belong to no group. They are the auth paths, so each one carries the IP and
    // user agent. The ones raised before a token exists also name their user, because the
    // envelope has no actor then.

    public enum LoginMethod
    {
        Password = 1,
        Google = 2,
    }

    [EventName("account.registered.v1")]
    public record UserRegistered : IEvent
    {
        public required Guid UserId { get; init; }
        public required string UserName { get; init; }
        public required string Email { get; init; }
        public required string DisplayName { get; init; }
        public required string? IpAddress { get; init; }
        public required string? UserAgent { get; init; }
    }

    [EventName("account.logged-in.v1")]
    public record UserLoggedIn : IEvent
    {
        public required Guid UserId { get; init; }
        public required string UserName { get; init; }
        public required string Email { get; init; }
        public required LoginMethod Method { get; init; }
        public required string? IpAddress { get; init; }
        public required string? UserAgent { get; init; }
    }

    [EventName("account.logged-out.v1")]
    public record UserLoggedOut : IEvent
    {
        public required Guid UserId { get; init; }
        public required string? IpAddress { get; init; }
        public required string? UserAgent { get; init; }
    }

    [EventName("account.password-reset.v1")]
    public record PasswordReset : IEvent
    {
        public required Guid UserId { get; init; }
        public required string UserName { get; init; }
        public required string Email { get; init; }
        public required string? IpAddress { get; init; }
        public required string? UserAgent { get; init; }
    }

    [EventName("account.password-changed.v1")]
    public record PasswordChanged : IEvent
    {
        public required Guid UserId { get; init; }
        public required string? IpAddress { get; init; }
        public required string? UserAgent { get; init; }
    }

    [EventName("account.username-changed.v1")]
    public record UsernameChanged : IEvent
    {
        public required Guid UserId { get; init; }
        public required string OldUserName { get; init; }
        public required string NewUserName { get; init; }
        public required string? IpAddress { get; init; }
        public required string? UserAgent { get; init; }
    }
}
