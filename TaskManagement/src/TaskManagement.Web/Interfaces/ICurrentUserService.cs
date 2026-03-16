using TaskManagement.Web.Models;

namespace TaskManagement.Web.Interfaces
{
    public interface ICurrentUserService
    {
        Task<UserInfo?> GetCurrentUserAsync();
    }
}
