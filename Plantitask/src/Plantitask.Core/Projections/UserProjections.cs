using System;
using System.Linq.Expressions;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Entities;

namespace Plantitask.Core.Projections
{
    public static class UserProjections
    {
        /// <summary>
        /// Reads a profile without materialising the User entity, so PasswordHash and the
        /// PayPal columns never enter memory on a plain profile read.
        ///
        /// IsPremium repeats the rule in UserSpecifications.HasActivePremium rather than
        /// calling User.HasActivePremium. That property is a compiled Func, which EF cannot
        /// translate - referencing it here throws at query time. Change one and change both.
        /// </summary>
        public static Expression<Func<User, UserProfileDto>> ToProfileDto => u => new UserProfileDto
        {
            Id = u.Id,
            UserName = u.UserName,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            ProfilePicturePath = u.ProfilePicturePath,
            LastLoginAt = u.LastLoginAt,
            CreatedAt = u.CreatedAt,
            IsPremium = u.IsPremium && (!u.PremiumExpiresAt.HasValue || u.PremiumExpiresAt > DateTime.UtcNow),
            SubscriptionType = u.SubscriptionType,
            PremiumExpiresAt = u.PremiumExpiresAt,
            PremiumStartedAt = u.PremiumStartedAt,
            MaxGroups = u.MaxGroups
        };
    }
}
