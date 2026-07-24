using System;
using System.Security.Cryptography;
using System.Text;
namespace Plantitask.Infrastructure.Security
{
    public static class TokenHasher
    {
        public static string Sha256(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
