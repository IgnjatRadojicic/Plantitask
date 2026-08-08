using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class PasswordHasher : IPasswordHasher
    {

        private const int WorkFactor = 12;

        private static readonly string _dummyHash =
            BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), WorkFactor);

        public string DummyHash => _dummyHash;

        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        public bool NeedsRehash(string hashedPassword)
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(hashedPassword, WorkFactor);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrWhiteSpace(hashedPassword))
                return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }

    }
}
