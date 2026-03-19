using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces
{
    public interface ICurrentUserService
    {
        Task<UserInfo?> GetCurrentUserAsync();
    }
}
