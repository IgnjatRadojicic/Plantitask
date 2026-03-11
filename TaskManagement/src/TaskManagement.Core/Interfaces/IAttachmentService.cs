using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManagement.Core.Common;
using TaskManagement.Core.DTO.Attachments;

namespace TaskManagement.Core.Interfaces
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
