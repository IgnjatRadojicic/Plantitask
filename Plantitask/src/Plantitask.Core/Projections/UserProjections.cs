using System;
using System.Linq.Expressions;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Entities;

namespace Plantitask.Core.Projections
{
    public static class UserProjections
    {
        /// <summary>
        /// Reads a profile without materialising the User entity, so PasswordHash never enters
        /// memory on a plain profile read.
        ///
        /// Carries identity only. The premium fields on UserProfileDto come from the active
        /// grant and are stamped by UserProfileService after this runs, because resolving a
        /// grant means ordering by precedence and falling back to the free plan, and an
        /// expression tree cannot call the one method that knows those rules.
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
            CreatedAt = u.CreatedAt
        };
    }
}
