using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// BCrypt hashing for the low-entropy secrets (passwords and verification codes). The work
    /// factor is pinned rather than left to the library default so a library upgrade cannot
    /// silently change our cost. High-entropy tokens use SHA-256 elsewhere - BCrypt's cost buys
    /// nothing against unguessable input and its salt would break lookups.
    /// </summary>
    public class PasswordHasher : IPasswordHasher
    {

        private const int WorkFactor = 12;

        private static readonly string _dummyHash =
            BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), WorkFactor);

        /// <summary>
        /// A real hash of a throwaway value. Login verifies against this when the email is
        /// unknown so both paths cost the same time and the difference cannot be measured.
        /// </summary>
        public string DummyHash => _dummyHash;

        /// <summary>Hashes with the pinned work factor; the salt is generated per call.</summary>
        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        /// <summary>
        /// True when the stored hash was made with an older work factor - login uses this to
        /// upgrade hashes opportunistically while it still has the plaintext in hand.
        /// </summary>
        public bool NeedsRehash(string hashedPassword)
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(hashedPassword, WorkFactor);
        }

        /// <summary>
        /// Constant-answer verification: empty or malformed stored hashes come back false
        /// instead of throwing, so callers never need a try/catch on a login path.
        /// </summary>
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
