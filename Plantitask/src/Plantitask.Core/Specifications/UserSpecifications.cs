
using Plantitask.Core.Entities;
using System.Linq.Expressions;

namespace Plantitask.Core.Specifications
{
    public static class UserSpecifications
    {
        public static Expression<Func<User, bool>> HasActivePremium =>
            u => u.IsPremium && (!u.PremiumExpiresAt.HasValue || u.PremiumExpiresAt > DateTime.UtcNow);
    }
}


