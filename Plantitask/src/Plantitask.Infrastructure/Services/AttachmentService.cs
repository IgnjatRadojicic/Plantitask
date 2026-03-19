using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Common;
using Plantitask.Core.Configuration;
using Plantitask.Core.Constants;
using Plantitask.Core.DTO.Attachments;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class AttachmentService : IAttachmentService
    {
        private readonly IApplicationDbContext _context;
        private readonly IFileStorageService _fileStorage;
        private readonly FileStorageSettings _settings;
        private readonly ILogger<AttachmentService> _logger;

        public AttachmentService(
            IApplicationDbContext context,
            IFileStorageService fileStorage,
            IOptions<FileStorageSettings> settings,
            ILogger<AttachmentService> logger)
        {
            _context = context;
            _fileStorage = fileStorage;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<Result<AttachmentDto>> UploadAttachmentAsync(Guid taskId, IFormFile file, Guid userId)
        {
            _logger.LogInformation("User {UserId} uploading attachment to task {TaskId}", userId, taskId);

            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to upload attachments");

            var validationError = ValidateFile(file);
            if (validationError != null)
                return validationError;

            string storagePath;
            using (var stream = file.OpenReadStream())
            {
                storagePath = await _fileStorage.UploadFileAsync(stream, file.FileName, file.ContentType);
            }

            var attachment = new TaskAttachment
            {
                TaskId = taskId,
                FileName = file.FileName,
                FilePath = storagePath,
                FileSize = file.Length,
                ContentType = file.ContentType,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.TaskAttachments.Add(attachment);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Attachment {AttachmentId} uploaded to task {TaskId}", attachment.Id, taskId);

            return await GetAttachmentByIdAsync(attachment.Id, userId);
        }

        public async Task<Result<List<AttachmentDto>>> GetTaskAttachmentsAsync(Guid taskId, Guid userId)
        {
            var task = await _context.Tasks
                .Include(t => t.Group)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return Error.NotFound("Task not found");

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to view attachments");

            var attachments = await _context.TaskAttachments
                .Where(a => a.TaskId == taskId)
                .Include(a => a.Uploader)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new AttachmentDto
                {
                    Id = a.Id,
                    TaskId = a.TaskId,
                    FileName = a.FileName,
                    FileSize = a.FileSize,
                    ContentType = a.ContentType,
                    DownloadUrl = _fileStorage.GetFileUrl(a.FilePath),
                    UploadedAt = a.CreatedAt,
                    UploadedByUserName = a.Uploader.UserName
                })
                .ToListAsync();

            return attachments;
        }

        public async Task<Result<AttachmentDto>> GetAttachmentByIdAsync(Guid attachmentId, Guid userId)
        {
            var attachment = await _context.TaskAttachments
                .Include(a => a.Task)
                .ThenInclude(t => t.Group)
                .Include(a => a.Uploader)
                .FirstOrDefaultAsync(a => a.Id == attachmentId);

            if (attachment == null)
                return Error.NotFound("Attachment not found");

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == attachment.Task.GroupId && gm.UserId == userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to view this attachment");

            return new AttachmentDto
            {
                Id = attachment.Id,
                TaskId = attachment.TaskId,
                FileName = attachment.FileName,
                FileSize = attachment.FileSize,
                ContentType = attachment.ContentType,
                DownloadUrl = _fileStorage.GetFileUrl(attachment.FilePath),
                UploadedAt = attachment.CreatedAt,
                UploadedByUserName = attachment.Uploader.UserName
            };
        }

        public async Task<Result<(Stream FileStream, string FileName, string ContentType)>> DownloadAttachmentAsync(Guid attachmentId, Guid userId)
        {
            var attachment = await _context.TaskAttachments
                .Include(a => a.Task)
                .ThenInclude(t => t.Group)
                .FirstOrDefaultAsync(a => a.Id == attachmentId);

            if (attachment == null)
                return Error.NotFound("Attachment not found");

            var isMember = await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == attachment.Task.GroupId && gm.UserId == userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to download this attachment");

            var fileStream = await _fileStorage.DownloadFileAsync(attachment.FilePath);

            return (fileStream, attachment.FileName, attachment.ContentType);
        }

        public async Task<Result> DeleteAttachmentAsync(Guid attachmentId, Guid userId)
        {
            var attachment = await _context.TaskAttachments
                .Include(a => a.Task)
                .ThenInclude(t => t.Group)
                .FirstOrDefaultAsync(a => a.Id == attachmentId);

            if (attachment == null)
                return Error.NotFound("Attachment not found");

            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == attachment.Task.GroupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You must be a member of the group");

            var canDelete = membership.Role.PermissionLevel >= PermissionLevels.Manager || attachment.CreatedBy == userId;

            if (!canDelete)
                return Error.Forbidden("Only Managers, Owners, or the uploader can delete attachments");

            await _fileStorage.DeleteFileAsync(attachment.FilePath);

            attachment.IsDeleted = true;
            attachment.DeletedAt = DateTime.UtcNow;
            attachment.DeletedBy = userId;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Attachment {AttachmentId} deleted by user {UserId}", attachmentId, userId);

            return Result.Success();
        }

        private Error? ValidateFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Error.BadRequest("File is empty");

            var maxSizeBytes = _settings.MaxFileSizeInMB * 1024 * 1024;
            if (file.Length > maxSizeBytes)
                return Error.BadRequest($"File size exceeds maximum allowed size of {_settings.MaxFileSizeInMB}MB");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_settings.AllowedExtensions.Contains(extension))
                return Error.BadRequest($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", _settings.AllowedExtensions)}");

            return null;
        }
    }
}