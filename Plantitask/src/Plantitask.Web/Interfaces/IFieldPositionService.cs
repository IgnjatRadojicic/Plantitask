using Plantitask.Web.Models;
using Plantitask.Web.Services;

namespace Plantitask.Web.Interfaces
{
    public interface IFieldPositionService
    {
        Task<Dictionary<string, TreePosition>> GetPositionsAsync(Guid userId);
        Task SaveAllPositionsAsync(Guid userId, Dictionary<string, TreePosition> positions);
        Task SavePositionAsync(Guid userId, string groupId, double x, double y);
    }
}