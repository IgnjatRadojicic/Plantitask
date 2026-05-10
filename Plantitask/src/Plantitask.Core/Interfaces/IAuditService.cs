using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Audit;
using Plantitask.Core.Entities;

namespace Plantitask.Core.Interfaces
{
    public interface IAuditService
    {
        Task LogAsync(CreateAuditLogRequest request);

        Task<Result<List<AuditLogDto>>> GetEntityHistoryAsync(string entityType, Guid entityId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50);
        Task<Result<List<AuditLogDto>>> GetGroupHistoryAsync(Guid groupId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50);
        Task<Result<List<AuditLogDto>>> GetUserHistoryAsync(Guid userId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50);


    }
}
