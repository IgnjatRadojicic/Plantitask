using Plantitask.Core.DTO.Auth;

namespace Plantitask.Web.Interfaces
{
    public interface ISessionService
    {
        /// <summary>New access token, or null when the session ended</summary>
        event Action<string?>? OnTokensChanged;
        Task<string?> GetAccessTokenAsync();
        Task<string?> GetRefreshTokenAsync();
        Task SetTokensAsync(AuthResponseDto auth);
        Task ClearAsync();
        Task<string?> TryRefreshAsync();
    }
}
