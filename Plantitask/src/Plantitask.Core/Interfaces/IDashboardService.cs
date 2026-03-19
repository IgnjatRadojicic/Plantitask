using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Dashboard;

namespace Plantitask.Core.Interfaces
{
    public interface IDashboardService
    {
        Task<Result<PersonalDashboardDto>> GetPersonalDashboardAsync(Guid userId);
        Task<Result<List<FieldTreeDto>>> GetFieldDataAsync(Guid userId);
        Task<Result<GroupStatisticsDto>> GetGroupStatisticsAsync(Guid groupId, Guid userId);
        Task<Result<FieldTreeDto>> GetGroupTreeProgressAsync(Guid groupId);
    }
}
