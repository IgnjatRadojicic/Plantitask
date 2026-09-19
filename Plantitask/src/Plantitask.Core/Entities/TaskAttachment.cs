using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;

namespace Plantitask.Core.Entities
{
    public class TaskAttachment : BaseEntity
    {
        public Guid TaskId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize  { get; set; }
        public string ContentType { get; set; } = string.Empty;

        /// <summary>
        /// When the bytes behind this row were actually removed from storage. Null on a live
        /// attachment, and null on a soft-deleted one whose file is still on disk.
        ///
        /// Soft-deleting the row and deleting the file are two separate events, and the gap is
        /// what AttachmentPurgeJob closes. This column is how the job knows what is left to do
        /// and why running it twice is harmless.
        /// </summary>
        public DateTime? FilePurgedAt { get; set; }

        public virtual TaskItem Task { get; set; } = null!;
        public User Uploader { get; set; } = null!;

    }
}
