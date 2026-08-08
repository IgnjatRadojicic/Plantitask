using System.Security.Cryptography;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Eight-character join codes from a CSPRNG. The alphabet drops 0, O, 1 and I so a code
    /// read out loud or off a screenshot cannot be mistyped.
    /// </summary>
    public class GroupCodeGenerator : IGroupCodeGenerator
    {
        private const string ValidChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 8;

        /// <summary>A fresh random code; uniqueness against the database is the caller's job.</summary>
        public string Generate()
        {
            return RandomNumberGenerator.GetString(ValidChars, CodeLength);
        }

        /// <summary>
        /// Cheap shape check before the database is asked - wrong length or characters outside
        /// the alphabet cannot be a real code, so they never cost a query.
        /// </summary>
        public bool IsValid(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != CodeLength)
                return false;
            return code.All(c => ValidChars.Contains(c));
        }
    }
}