
using Plantitask.Core.Entities;
using System.Linq.Expressions;

namespace Plantitask.Core.Specifications
{
    public static class UserSpecifications
    {
        /// <summary>
        /// Also written out by hand in UserProjections.ToProfileDto. A projection cannot call
        /// User.HasActivePremium because that is a compiled Func and EF cannot translate it.
        /// Change this rule and change that one.
        /// </summary>
        public static Expression<Func<User, bool>> HasActivePremium =>
            u => u.IsPremium && (!u.PremiumExpiresAt.HasValue || u.PremiumExpiresAt > DateTime.UtcNow);
    }
}


