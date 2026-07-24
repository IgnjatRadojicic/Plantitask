using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Models;

namespace Plantitask.Core.Interfaces
{
    public interface IRedisService
    {

        Task SetRefreshTokenAsync(string tokenHash, RefreshTokenModel model, TimeSpan expiration);


        Task<RefreshTokenModel?> GetRefreshTokenAsync(string tokenHash);


        Task MarkRefreshTokenRevokedAsync(string tokenHash);

        Task RevokeAllUserTokensAsync(Guid userId);

        Task DeleteRefreshTokenAsync(string tokenHash);

        Task StoreVerificationCodeAsync(string email, string codeHash, TimeSpan expiration);
        Task<string?> GetVerificationCodeHashAsync(string email);
        Task MarkVerificationCodeUsedAsync(string email);
        Task<bool> IsEmailVerifiedAsync(string email);
        Task<DateTime?> GetVerificationCodeCreatedAtAsync(string email);


    }
}
