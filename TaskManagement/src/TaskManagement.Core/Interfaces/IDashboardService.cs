using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManagement.Core.Common;
using TaskManagement.Core.DTO.Dashboard;

namespace TaskManagement.Core.Interfaces
{
    public interface IDashboardService
    {
        Task<Result<PersonalDashboardDto>> GetPersonalDashboardAsync(Guid userId);
        Task<Result<List<FieldTreeDto>>> GetFieldDataAsync(Guid userId);
        Task<Result<GroupStatisticsDto>> GetGroupStatisticsAsync(Guid groupId, Guid userId);
    }
}
