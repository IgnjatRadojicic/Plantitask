using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Interfaces;
namespace Plantitask.Infrastructure.Services.Storage
{
    /// <summary>
    /// Disk-backed file storage for development and single-server deployments. Stored names
    /// are always server-generated, and every path that reaches the filesystem goes through
    /// <see cref="ResolveStoredPath"/>, which proves the result stayed inside the storage root.
    /// </summary>
    public class LocalFileStorageService : IFileStorageService
    {
        private readonly FileStorageSettings _settings;
        private readonly ILogger<LocalFileStorageService> _logger;

        public LocalFileStorageService(
            IOptions<FileStorageSettings> settings,
            ILogger<LocalFileStorageService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            if (!Directory.Exists(_settings.LocalStorage.BasePath))
            {
                Directory.CreateDirectory(_settings.LocalStorage.BasePath);
                _logger.LogInformation("Created base directory: {Path}", _settings.LocalStorage.BasePath);
            }
        }

        /// <summary>
        /// Writes the stream under a fresh Guid name - only the validated extension survives
        /// from the client's filename. CreateNew means a name collision throws instead of
        /// silently overwriting someone else's file. Content validation is the caller's job.
        /// </summary>
        public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, string folder)
        {
            var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
            var storedName = $"{folder}/{Guid.NewGuid()}{extension}";

            var fullPath = ResolveStoredPath(storedName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
            {
                await fileStream.CopyToAsync(output);
            }

            return storedName;
        }

        /// <summary>
        /// Opens a stored file for async streaming. A missing file throws rather than returning
        /// a Result because today a DB row always implies a file - it is a fault, not an
        /// expected state. If a retention job ever hard-deletes blobs while leaving rows, or
        /// storage starts expiring objects legitimately, missing-file becomes expected and
        /// Result with Error.NotFound becomes right. Re-run the "can a well-behaved system
        /// reach this?" test then, not the cost question.
        /// </summary>
        public Task<Stream> DownloadFileAsync(string storagePath)
        {
            var fullPath = ResolveStoredPath(storagePath);

            if (!File.Exists(fullPath))

                throw new FileNotFoundException("File not found", storagePath);

            return Task.FromResult<Stream>(new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true));
        }


        /// <summary>
        /// Deletes a stored file, treating already-gone as done. Failures propagate so the
        /// caller decides whether the delete was best-effort or load-bearing.
        /// </summary>
        public Task DeleteFileAsync(string storagePath)
        {
            try
            {
                var fullPath = ResolveStoredPath(storagePath);

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation("File deleted from local storage: {Path}", fullPath);
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file from local storage: {Path}", storagePath);
                throw;
            }
        }


        /// <summary>
        /// The containment proof: resolves the stored key against the base path and refuses any
        /// result that escapes the storage root. This runs even though stored names are
        /// server-generated - defense in depth for whatever the database ends up holding.
        /// </summary>
        private string ResolveStoredPath(string storagePath)
        {
            var basePath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(_settings.LocalStorage.BasePath));
            var fullPath = Path.GetFullPath(Path.Combine(basePath, storagePath));

            if (!fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Resolved path escaped the storage root");

            return fullPath;
        }

        /// <summary>Whether the stored key still has a file behind it.</summary>
        public Task<bool> FileExistsAsync(string storagePath) =>
            Task.FromResult(File.Exists(ResolveStoredPath(storagePath)));

        /// <summary>
        /// Public URL for content that is allowed to be public (avatars). Group-scoped files
        /// never use this - they go through the membership-checked download endpoint.
        /// </summary>
        public string GetFileUrl(string storagePath)
        {
            return $"{_settings.LocalStorage.BaseUrl}/{storagePath}";
        }
    }
}

