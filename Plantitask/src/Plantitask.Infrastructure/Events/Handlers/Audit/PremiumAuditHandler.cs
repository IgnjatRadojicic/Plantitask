using Plantitask.Core.Constants;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events.Handlers.Audit
{
    public class PremiumAuditHandler : AuditHandler,
        IDurableEventHandler<PremiumActivated>,
        IDurableEventHandler<PremiumCancelled>
    {
        public PremiumAuditHandler(ApplicationDbContext db) : base(db)
        {
        }

        public Task HandleAsync(PremiumActivated e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.PremiumActivated,
                propertyName: "SubscriptionType",
                newValue: e.Kind == PremiumKind.Recurring ? "recurring" : "onetime");

        public Task HandleAsync(PremiumCancelled e, EventContext context, CancellationToken ct) =>
            Write(context, EntityTypes.User, e.UserId, AuditActions.PremiumCancelled,
                propertyName: "IsPremium",
                oldValue: "true",
                newValue: "false");
    }
}
