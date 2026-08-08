using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Interfaces;
namespace Plantitask.Infrastructure.Services.Storage
{
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

        // When adding a retention job that hard-deletes
        // blobs while leaving rows, or move to storage where objects legitimately expire,
        // missing-file becomes an expected state and Result<Stream> with Error.NotFound becomes right.
        // Re-run the "can a well-behaved system reach this?" test then, not the cost question.

        public Task<Stream> DownloadFileAsync(string storagePath)
        {
            var fullPath = ResolveStoredPath(storagePath);

            if (!File.Exists(fullPath))

                throw new FileNotFoundException("File not found", storagePath);

            return Task.FromResult<Stream>(new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true));
        }


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


        private string ResolveStoredPath(string storagePath)
        {
            var basePath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(_settings.LocalStorage.BasePath));
            var fullPath = Path.GetFullPath(Path.Combine(basePath, storagePath));

            if (!fullPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Resolved path escaped the storage root");

            return fullPath;
        }

        public Task<bool> FileExistsAsync(string storagePath) =>
            Task.FromResult(File.Exists(ResolveStoredPath(storagePath)));

        public string GetFileUrl(string storagePath)
        {
            return $"{_settings.LocalStorage.BaseUrl}/{storagePath}";
        }
    }
}

