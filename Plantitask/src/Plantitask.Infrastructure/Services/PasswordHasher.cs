using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class PasswordHasher : IPasswordHasher
    {

        private const int WorkFactor = 12;
        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public bool NeedsRehash(string hashedPassword)
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(hashedPassword, WorkFactor);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
    }
}
