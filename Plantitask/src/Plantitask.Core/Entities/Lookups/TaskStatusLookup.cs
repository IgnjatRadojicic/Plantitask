using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Entities.Lookups
{
    public class TaskStatusLookup : BaseLookupEntity
    {

        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}
