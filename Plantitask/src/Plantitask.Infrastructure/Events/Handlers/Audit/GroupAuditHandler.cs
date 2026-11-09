using Plantitask.Contracts.Enums;
using Plantitask.Core.Constants;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events.Handlers.Audit
{
    public class GroupAuditHandler : AuditHandler,
        IDurableEventHandler<GroupCreated>,
        IDurableEventHandler<GroupUpdated>,
        IDurableEventHandler<GroupMemberJoined>,
        IDurableEventHandler<GroupMemberRoleChanged>,
        IDurableEventHandler<GroupMemberRemoved>,
        IDurableEventHandler<GroupMemberLeft>
    {
        public GroupAuditHandler(ApplicationDbContext db) : base(db)
        {
        }

        public Task HandleAsync(GroupCreated e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.Group, e.GroupId, AuditActions.Created, e.GroupId);

        public Task HandleAsync(GroupUpdated e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.Group, e.GroupId, AuditActions.Updated, e.GroupId);

        public Task HandleAsync(GroupMemberJoined e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.GroupMember, e.UserId, AuditActions.JoinedGroup, e.GroupId);

        public Task HandleAsync(GroupMemberRoleChanged e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.GroupMember, e.UserId, AuditActions.RoleChanged, e.GroupId,
                propertyName: "Role",
                oldValue: ((GroupRole)e.OldRoleId).ToString(),
                newValue: ((GroupRole)e.NewRoleId).ToString());

        public Task HandleAsync(GroupMemberRemoved e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.GroupMember, e.UserId, AuditActions.RemovedFromGroup, e.GroupId);

        public Task HandleAsync(GroupMemberLeft e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.GroupMember, e.UserId, AuditActions.LeftGroup, e.GroupId);
    }
}
