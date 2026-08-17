using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;

namespace Plantitask.Core.Entities
{
    public class User : SelfManagedEntity
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? ProfilePicturePath { get; set; }

        public bool IsEmailConfirmed { get; set; } = false;
        public DateTime? LastLoginAt { get; set; }

        // Premium is not a column. MaxGroups, IsPremium, PremiumExpiresAt, PremiumStartedAt,
        // SubscriptionType, PayPalSubscriptionId and PayPalOrderId all moved to UserPlanGrant on
        // 2026-08-16, because a stored entitlement needs a correct writer at every payment
        // transition and one of them was always going to be wrong. Ask IEntitlementService.
        // There is deliberately no PlanGrants collection here: grants are queried through the
        // DbSet so the filtering happens in SQL rather than by loading every grant ever held.

        public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
        public ICollection<Group> OwnedGroups { get; set; } = new List<Group>();
        public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
        public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
        public ICollection<TaskAttachment> UploadedAttachments { get; set; } = new List<TaskAttachment>();


    }
}
