using Plantitask.Core.Constants;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events.Handlers.Audit
{
    public class AccountAuditHandler : AuditHandler,
        IDurableEventHandler<UserRegistered>,
        IDurableEventHandler<UserLoggedIn>,
        IDurableEventHandler<UserLoggedOut>,
        IDurableEventHandler<PasswordReset>,
        IDurableEventHandler<PasswordChanged>,
        IDurableEventHandler<UsernameChanged>
    {
        public AccountAuditHandler(ApplicationDbContext db) : base(db)
        {
        }

        public Task HandleAsync(UserRegistered e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.Registered,
                person: new EventActor(e.UserId, e.UserName, e.Email),
                ipAddress: e.IpAddress,
                userAgent: e.UserAgent);

        public Task HandleAsync(UserLoggedIn e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.Login,
                propertyName: "Method",
                newValue: e.Method == LoginMethod.Google ? "google" : "password",
                person: new EventActor(e.UserId, e.UserName, e.Email),
                ipAddress: e.IpAddress,
                userAgent: e.UserAgent);

        public Task HandleAsync(UserLoggedOut e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.Logout,
                ipAddress: e.IpAddress,
                userAgent: e.UserAgent);

        public Task HandleAsync(PasswordReset e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.PasswordReset,
                person: new EventActor(e.UserId, e.UserName, e.Email),
                ipAddress: e.IpAddress,
                userAgent: e.UserAgent);

        public Task HandleAsync(PasswordChanged e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.PasswordChanged,
                ipAddress: e.IpAddress,
                userAgent: e.UserAgent);

        public Task HandleAsync(UsernameChanged e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.UsernameChanged,
                propertyName: "UserName",
                oldValue: e.OldUserName,
                newValue: e.NewUserName,
                ipAddress: e.IpAddress,
                userAgent: e.UserAgent);
    }
}
