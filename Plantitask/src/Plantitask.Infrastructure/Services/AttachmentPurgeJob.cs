using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Deletes the bytes behind soft-deleted attachments.
    ///
    /// Deleting a tree or a task soft-deletes every attachment row in one ExecuteUpdateAsync and
    /// never touched storage, so those files stayed on disk forever. That was a leak until the
    /// storage quota started counting SUM(FileSize) over live rows, at which point it became a
    /// way around the cap entirely: upload to the limit, delete the tree, upload again, repeat.
    /// The quota freed, the disk did not.
    ///
    /// Rows are stamped rather than hard deleted, which is what makes this safe to run twice and
    /// lets a file that could not be deleted simply come back around on the next pass.
    /// </summary>
    public class AttachmentPurgeJob
    {
        // Bounded so one pass over a tree with thousands of attachments cannot hold a connection
        // open indefinitely. Whatever is left is picked up fifteen minutes later.
        private const int BatchSize = 500;

        private readonly IApplicationDbContext _context;
        private readonly IFileStorageService _fileStorage;
        private readonly ILogger<AttachmentPurgeJob> _logger;

        public AttachmentPurgeJob(
            IApplicationDbContext context,
            IFileStorageService fileStorage,
            ILogger<AttachmentPurgeJob> logger)
        {
            _context = context;
            _fileStorage = fileStorage;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 2)]
        public async Task PurgeDeletedAttachmentFilesAsync()
        {
            // Read set, then the per-file work that has no set form, then write set. Only two
            // columns are projected: nothing is tracked because nothing here is mutated through
            // the change tracker.
            //
            // IgnoreQueryFilters on purpose: every row this job exists for is soft deleted, so
            // the global filter would hide all of them and the job would find nothing forever.
            var pending = await _context.TaskAttachments
                .IgnoreQueryFilters()
                .Where(a => a.IsDeleted && a.FilePurgedAt == null)
                .OrderBy(a => a.DeletedAt)
                .Take(BatchSize)
                .Select(a => new { a.Id, a.FilePath })
                .ToListAsync();

            if (pending.Count == 0)
                return;

            var now = DateTime.UtcNow;
            var purgedIds = new List<Guid>(pending.Count);

            foreach (var attachment in pending)
            {
                try
                {
                    // Both storage backends treat an already-missing file as success, so a file
                    // deleted by some earlier path does not trap this row in a retry loop.
                    await _fileStorage.DeleteFileAsync(attachment.FilePath);
                    purgedIds.Add(attachment.Id);
                }
                catch (Exception ex)
                {
                    // Not collected, so FilePurgedAt stays null and the next pass retries this
                    // one. A single unreachable file must not cost the rest of the batch.
                    _logger.LogWarning(ex,
                        "Could not delete stored file for attachment {AttachmentId}, will retry",
                        attachment.Id);
                }
            }

            if (purgedIds.Count > 0)
            {
                // One statement whatever the batch size: Contains translates to = ANY(@ids).
                // ExecuteUpdate bypasses the SaveChangesAsync stamping override, so UpdatedAt
                // has to be set by hand here.
                await _context.TaskAttachments
                    .IgnoreQueryFilters()
                    .Where(a => purgedIds.Contains(a.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.FilePurgedAt, now)
                        .SetProperty(a => a.UpdatedAt, now));
            }

            _logger.LogInformation(
                "Purged {Purged} of {Found} deleted attachment files", purgedIds.Count, pending.Count);
        }
    }
}
