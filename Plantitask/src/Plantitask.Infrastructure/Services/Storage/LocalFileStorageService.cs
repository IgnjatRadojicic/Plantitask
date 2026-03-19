using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Interfaces;
using System.IO;
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

        public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
        { try
            {
                var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
                var fullPath = Path.Combine(_settings.LocalStorage.BasePath, uniqueFileName);

                using (var fileStreamOutput = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                {
                    await fileStream.CopyToAsync(fileStreamOutput);
                }
                _logger.LogInformation("File uploaded to local storage: {Path}", fullPath);

                return uniqueFileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file to local storage");
                throw;
            }
        }
        public async Task<Stream> DownloadFileAsync(string storagePath)
        {
            try
            {
                var fullPath = Path.Combine(_settings.LocalStorage.BasePath, storagePath);

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("File not found", storagePath);
                }

                var memoryStream = new MemoryStream();
                using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                {
                    await fileStream.CopyToAsync(memoryStream);
                }
                memoryStream.Position = 0;

                return memoryStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file from local storage: {Path}", storagePath);
                throw;
            }
        }

        public Task DeleteFileAsync(string storagePath)
        {
            try
            {
                var fullPath = Path.Combine(_settings.LocalStorage.BasePath, storagePath);

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

        public Task<bool> FileExistsAsync(string storagePath)
        {
            var fullPath = Path.Combine(_settings.LocalStorage.BasePath, storagePath);
            return Task.FromResult(File.Exists(fullPath));
        }

        public string GetFileUrl(string storagePath)
        {
            return $"{_settings.LocalStorage.BaseUrl}/{storagePath}";
        }
    }
}

