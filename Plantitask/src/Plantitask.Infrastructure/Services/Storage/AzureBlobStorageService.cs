using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Configuration;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services.Storage
{
        public class AzureBlobStorageService : IFileStorageService
        {
            private readonly FileStorageSettings _settings;
            private readonly ILogger<AzureBlobStorageService> _logger;

            public AzureBlobStorageService(
                IOptions<FileStorageSettings> settings,
                ILogger<AzureBlobStorageService> logger)
            {
                _settings = settings.Value;
                _logger = logger;
            }

            public Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
            {
                // TODO: Implement Azure Blob Storage upload
                // Create BlobServiceClient with connection string
                // Get container reference
                // Upload blob
                // Return blob URL or name

                throw new NotImplementedException("Azure Blob Storage not yet implemented. Use Local storage for now.");
            }

            public Task<Stream> DownloadFileAsync(string storagePath)
            {
                throw new NotImplementedException("Azure Blob Storage not yet implemented. Use Local storage for now.");
            }

            public Task DeleteFileAsync(string storagePath)
            {
                throw new NotImplementedException("Azure Blob Storage not yet implemented. Use Local storage for now.");
            }

            public Task<bool> FileExistsAsync(string storagePath)
            {
                throw new NotImplementedException("Azure Blob Storage not yet implemented. Use Local storage for now.");
            }

            public string GetFileUrl(string storagePath)
            {
                throw new NotImplementedException("Azure Blob Storage not yet implemented. Use Local storage for now.");
            }
        }
    }
