using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class GroupCodeGenerator : IGroupCodeGenerator
    {
        private const string ValidChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 8;

        public string Generate(string groupName)
        {
            var input = $"{groupName}{DateTime.UtcNow.Ticks}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var code = new StringBuilder(CodeLength);
            for (int i = 0; i < CodeLength; i++)
            {
                code.Append(ValidChars[hash[i] % ValidChars.Length]);
            }
            return code.ToString();
        }

        public bool IsValid(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != CodeLength)
                return false;
            return code.All(c => ValidChars.Contains(c));
        }
    }
}