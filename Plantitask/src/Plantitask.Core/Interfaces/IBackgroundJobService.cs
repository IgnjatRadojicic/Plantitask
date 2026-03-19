using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Interfaces
{
    public interface IBackgroundJobService
    {

        Task<string> ScheduleTaskDueSoonNotification(Guid taskId, Guid userId, DateTime dueDate);

        void CancelScheduledJob(string jobId);

        void SetupRecurringJobs();

        void TriggerOverdueCheck();
    }
}
