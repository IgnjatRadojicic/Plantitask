using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Common;
using Plantitask.Core.Configuration;
using Plantitask.Core.Enums;
using Plantitask.Core.DTO.Attachments;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Validation;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Task attachments: upload, listing, download and deletion. Every operation resolves the
    /// owning group first, because attachments are group scoped and the membership check is
    /// what keeps one tenant out of another's files.
    /// </summary>
    public class AttachmentService : IAttachmentService
    {
        private readonly IApplicationDbContext _context;
        private readonly IFileStorageService _fileStorage;
        private readonly FileStorageSettings _settings;
        private readonly ILogger<AttachmentService> _logger;
        private readonly IGroupService _groupService;
        private readonly IEntitlementService _entitlements;
        public AttachmentService(
            IApplicationDbContext context,
            IFileStorageService fileStorage,
            IOptions<FileStorageSettings> settings,
            IGroupService groupService,
            IEntitlementService entitlements,
            ILogger<AttachmentService> logger)
        {
            _context = context;
            _fileStorage = fileStorage;
            _groupService = groupService;
            _entitlements = entitlements;
            _settings = settings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Validates and stores a file against a task, then records it in the database.
        /// Membership is checked before any storage I/O happens. The content type comes from the
        /// validated extension, never from the client, and the storage layer picks the stored
        /// name so the original filename is metadata only.
        /// </summary>
        public async Task<Result<AttachmentDto>> UploadAttachmentAsync(Guid taskId, Stream content, string fileName, Guid userId)
        {
            _logger.LogInformation("User {UserId} uploading attachment to task {TaskId}", userId, taskId);

            var groupId = await _context.Tasks
                .Where(t => t.Id == taskId)
                .Select(t => (Guid?)t.GroupId)
                .FirstOrDefaultAsync();

            if (groupId == null)
                return Error.NotFound("Task not found");

            var isMember = await _groupService.IsUserMemberAsync(groupId.Value, userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to upload attachments");

            var validation = await FileUploadRules.ValidateAsync(
                content, fileName, _settings.MaxFileSizeInMB, _settings.AllowedExtensions);
            if (validation.IsFailure)
                return validation.Error!;

            var quotaError = await CheckStorageQuotaAsync(userId, content.Length);
            if (quotaError != null)
                return quotaError;

            var contentType = FileUploadRules.ContentTypeFor(validation.Value!);
            var storagePath = await _fileStorage.UploadFileAsync(content, fileName, contentType, "attachments");

            var attachment = new TaskAttachment
            {
                TaskId = taskId,
                FileName = fileName,
                FilePath = storagePath,
                FileSize = content.Length,
                ContentType = contentType,
                CreatedBy = userId,
            };

            _context.TaskAttachments.Add(attachment);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Attachment {AttachmentId} uploaded to task {TaskId}", attachment.Id, taskId);

            var user = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync();

            return new AttachmentDto
            {
                Id = attachment.Id,
                TaskId = attachment.TaskId,
                FileName = attachment.FileName,
                FileSize = attachment.FileSize,
                ContentType = attachment.ContentType,
                DownloadUrl = BuildDownloadUrl(attachment.TaskId, attachment.Id),
                UploadedAt = attachment.CreatedAt,
                UploadedByUserName = user!
            };
        }

        /// <summary>
        /// The authorized endpoint, not the storage URL. Attachments are group scoped so the
        /// bytes must go through membership checks rather than an anonymous static path.
        /// </summary>
        private static string BuildDownloadUrl(Guid taskId, Guid attachmentId) =>
            $"/api/tasks/{taskId}/attachments/{attachmentId}/download";

        /// <summary>
        /// Lists a task's attachments for a group member, newest first. Download links point at
        /// the authorized endpoint built by <see cref="BuildDownloadUrl"/>.
        /// </summary>
        public async Task<Result<List<AttachmentDto>>> GetTaskAttachmentsAsync(Guid taskId, Guid userId)
        {
            var groupId = await _context.Tasks
                .Where(t => t.Id == taskId)
                .Select(t => (Guid?)t.GroupId)
                .FirstOrDefaultAsync();

            if (groupId == null)
                return Error.NotFound("Task not found");

            var isMember = await _groupService.IsUserMemberAsync(groupId.Value, userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to view attachments");

            var results = await _context.TaskAttachments
                .Where(a => a.TaskId == taskId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.TaskId,
                    a.FileName,
                    a.FileSize,
                    a.ContentType,
                    a.FilePath,
                    a.CreatedAt,
                    UploaderName = a.Uploader.UserName
                })
                .ToListAsync();

            var attachments = results.Select(a => new AttachmentDto
            {
                Id = a.Id,
                TaskId = a.TaskId,
                FileName = a.FileName,
                FileSize = a.FileSize,
                ContentType = a.ContentType,
                DownloadUrl = BuildDownloadUrl(a.TaskId, a.Id),
                UploadedAt = a.CreatedAt,
                UploadedByUserName = a.UploaderName
            }).ToList();

            return attachments;
        }

        /// <summary>
        /// Returns one attachment's metadata after confirming the caller belongs to the group
        /// that owns it.
        /// </summary>
        public async Task<Result<AttachmentDto>> GetAttachmentByIdAsync(Guid attachmentId, Guid userId)
        {
            var attachment = await _context.TaskAttachments
                .Where(a => a.Id == attachmentId)
                .Select(a => new
                {
                    a.Id,
                    a.TaskId,
                    a.FileName,
                    a.FileSize,
                    a.ContentType,
                    a.FilePath,
                    a.CreatedAt,
                    GroupId = a.Task.GroupId,
                    UploaderName = a.Uploader.UserName
                })
                .FirstOrDefaultAsync();

            if (attachment == null)
                return Error.NotFound("Attachment not found");

            var isMember = await _groupService.IsUserMemberAsync(attachment.GroupId, userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to view this attachment");

            return new AttachmentDto
            {
                Id = attachment.Id,
                TaskId = attachment.TaskId,
                FileName = attachment.FileName,
                FileSize = attachment.FileSize,
                ContentType = attachment.ContentType,
                DownloadUrl = BuildDownloadUrl(attachment.TaskId, attachment.Id),
                UploadedAt = attachment.CreatedAt,
                UploadedByUserName = attachment.UploaderName
            };
        }

        /// <summary>
        /// Opens the stored file for a group member. Hands back the stream together with the
        /// original filename and the server-derived content type so the controller can serve it
        /// as a download.
        /// </summary>
        public async Task<Result<(Stream FileStream, string FileName, string ContentType)>> DownloadAttachmentAsync(Guid attachmentId, Guid userId)
        {
            var attachment = await _context.TaskAttachments
                .Where(a => a.Id == attachmentId)
                .Select(a => new
                {
                    GroupId = a.Task.GroupId,
                    a.FilePath,
                    a.FileName,
                    a.ContentType

                })
                .FirstOrDefaultAsync();

            if (attachment == null)
                return Error.NotFound("Attachment not found");

            var isMember = await _groupService.IsUserMemberAsync(attachment.GroupId, userId);

            if (!isMember)
                return Error.Forbidden("You must be a member of the group to download this attachment");

            var fileStream = await _fileStorage.DownloadFileAsync(attachment.FilePath);

            return (fileStream, attachment.FileName, attachment.ContentType);
        }

        /// <summary>
        /// Soft-deletes an attachment. The caller must be a member of the owning group, and then
        /// either the uploader or a Manager and above. The database row commits first; deleting
        /// the physical file is best effort because the row is the source of truth.
        /// </summary>
        public async Task<Result> DeleteAttachmentAsync(Guid attachmentId, Guid userId)
        {
            var row = await _context.TaskAttachments
                .Where(a => a.Id == attachmentId)
                .Select(a => new { Attachment = a, a.Task.GroupId })
                .FirstOrDefaultAsync();

            if (row == null)
                return Error.NotFound("Attachment not found");

            var attachment = row.Attachment;

            var callerRole = await _groupService.GetUserRoleAsync(row.GroupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You must be a member of this group");

            var canDelete = attachment.CreatedBy == userId || callerRole >= GroupRole.Manager;

            if (!canDelete)
                return Error.Forbidden("Only Managers, Owners, or the uploader can delete attachments");

            attachment.IsDeleted = true;
            attachment.DeletedAt = DateTime.UtcNow;
            attachment.DeletedBy = userId;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Attachment {AttachmentId} deleted by user {UserId}", attachmentId, userId);

            try
            {
                await _fileStorage.DeleteFileAsync(attachment.FilePath);

                // Stamped only once the bytes are really gone. Leaving it null on failure is
                // what hands the row to AttachmentPurgeJob instead of orphaning the file, which
                // is what this catch used to do silently.
                attachment.FilePurgedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to delete physical file for attachment {AttachmentId}, purge job will retry",
                    attachmentId);
            }

            return Result.Success();
        }

        /// <summary>
        /// The per-user storage cap. Runs after validation so the size is known but before any
        /// storage I/O, because a file rejected after upload is a file we are paying to keep.
        ///
        /// Quota is per uploader, not per tree: your files count against you wherever you put
        /// them, so the message makes sense to whoever hits it and no tree owner can be filled
        /// up by their members.
        /// </summary>
        private async Task<Error?> CheckStorageQuotaAsync(Guid userId, long incomingBytes)
        {
            var entitlements = await _entitlements.GetEntitlementsAsync(userId);
            if (entitlements.IsFailure)
                return entitlements.Error!;

            var limit = entitlements.Value!.MaxStorageBytes;
            var used = await _entitlements.GetStorageUsedBytesAsync(userId);

            if (used + incomingBytes <= limit)
                return null;

            const long mb = 1024 * 1024;

            return Error.Forbidden(
                $"This file would put you over your {limit / mb} MB storage limit. " +
                $"You have used {used / mb} MB. Delete something or upgrade to Premium for more.");
        }
    }
}