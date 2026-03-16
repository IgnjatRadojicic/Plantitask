using TaskManagement.Web.Models;
using TaskManagement.Web.Services;

namespace TaskManagement.Web.Interfaces
{
    public interface IFieldPositionService
    {
        Task<Dictionary<string, TreePosition>> GetPositionsAsync(Guid userId);
        Task SaveAllPositionsAsync(Guid userId, Dictionary<string, TreePosition> positions);
        Task SavePositionAsync(Guid userId, string groupId, double x, double y);
    }
}