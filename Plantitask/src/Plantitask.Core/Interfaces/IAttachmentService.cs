using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Attachments;

namespace Plantitask.Core.Interfaces
{
    public interface IAttachmentService
    {
        Task<Result<AttachmentDto>> UploadAttachmentAsync(Guid taskId, IFormFile file, Guid userId);
        Task<Result<List<AttachmentDto>>> GetTaskAttachmentsAsync(Guid taskId, Guid userId);
        Task<Result<AttachmentDto>> GetAttachmentByIdAsync(Guid attachmentId, Guid userId);
        Task<Result<(Stream FileStream, string FileName, string ContentType)>> DownloadAttachmentAsync(Guid attachmentId, Guid userId);
        Task<Result> DeleteAttachmentAsync(Guid attachmentId, Guid userId);
    }
}
