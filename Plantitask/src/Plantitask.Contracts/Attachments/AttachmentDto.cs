using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Attachments
{
    public class AttachmentDto
    {
        public Guid Id { get; set; }
        public Guid TaskId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public string UploadedByUserName { get; set; } = string.Empty;
    }
}
