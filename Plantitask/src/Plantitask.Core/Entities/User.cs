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
        public string? ProfilePictureUrl { get; set; }

        public bool IsEmailConfirmed { get; set; } = false;
        public DateTime? LastLoginAt { get; set; }
        
        public int MaxGroups { get; set; } = 5;

        public bool IsPremium { get; set; } = false; 
        public DateTime? PremiumExpiresAt { get; set; }
        public string? PayPalSubscriptionId { get; set; }
        public string? PayPalOrderId { get; set; }
        public string? SubscriptionType { get; set; }
        public DateTime? PremiumStartedAt { get; set; }



        public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
        public ICollection<Group> OwnedGroups { get; set; } = new List<Group>();
        public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
        public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
        public ICollection<TaskAttachment> UploadedAttachments { get; set; } = new List<TaskAttachment>();

        public bool HasActivePremium =>
            IsPremium && (!PremiumExpiresAt.HasValue || PremiumExpiresAt.Value > DateTime.UtcNow);

    }
}