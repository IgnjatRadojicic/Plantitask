using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces
{
    public interface IAttachmentService
    {
        Task<ServiceResult<AttachmentDto>> UploadAsync(Guid taskId, Stream fileStream, string fileName, string contentType);
        Task<ServiceResult<List<AttachmentDto>>> GetTaskAttachmentsAsync(Guid taskId);
        Task<ServiceResult<AttachmentDto>> GetByIdAsync(Guid taskId, Guid attachmentId);
        Task<ServiceResult<bool>> DeleteAsync(Guid taskId, Guid attachmentId);
    }
}
