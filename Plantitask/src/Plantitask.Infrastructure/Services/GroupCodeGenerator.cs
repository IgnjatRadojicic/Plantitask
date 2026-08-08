using System.Security.Cryptography;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class GroupCodeGenerator : IGroupCodeGenerator
    {
        private const string ValidChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 8;

        public string Generate()
        {
            return RandomNumberGenerator.GetString(ValidChars, CodeLength);
        }

        public bool IsValid(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != CodeLength)
                return false;
            return code.All(c => ValidChars.Contains(c));
        }
    }
}