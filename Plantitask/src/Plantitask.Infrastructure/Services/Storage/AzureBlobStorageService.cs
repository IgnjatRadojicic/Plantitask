using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Plantitask.Core.Configuration;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services.Storage;

public class AzureBlobStorageService : IFileStorageService
{
    private readonly AzureBlobStorageSettings _blobSettings;
    private readonly int _maxFileSizeBytes;
    private readonly List<string> _allowedExtensions;
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly BlobContainerClient _containerClient;

    public AzureBlobStorageService(
        IOptions<FileStorageSettings> settings,
        ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        _blobSettings = settings.Value.AzureBlobStorage;
        _maxFileSizeBytes = settings.Value.MaxFileSizeInMB * 1024 * 1024;
        _allowedExtensions = settings.Value.AllowedExtensions;

        if (string.IsNullOrWhiteSpace(_blobSettings.ConnectionString))
            throw new InvalidOperationException(
                "Azure Blob Storage connection string is not configured. " +
                "Set FileStorage:AzureBlobStorage:ConnectionString in app settings.");

        var containerName = !string.IsNullOrWhiteSpace(_blobSettings.ContainerName)
            ? _blobSettings.ContainerName
            : "uploads";

        var blobServiceClient = new BlobServiceClient(_blobSettings.ConnectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        _containerClient.CreateIfNotExists(PublicAccessType.None);

        _logger.LogInformation(
            "Azure Blob Storage initialized with container '{Container}'", containerName);
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        if (fileStream.Length > _maxFileSizeBytes)
            throw new InvalidOperationException(
                $"File exceeds maximum allowed size of {_maxFileSizeBytes / (1024 * 1024)}MB.");

        if (_allowedExtensions.Count > 0)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(ext))
                throw new InvalidOperationException(
                    $"File extension '{ext}' is not allowed.");
        }

        var blobClient = _containerClient.GetBlobClient(fileName);

        await blobClient.UploadAsync(fileStream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        });

        _logger.LogInformation("Uploaded blob '{BlobName}' ({ContentType})", fileName, contentType);

        return fileName;
    }

    public async Task<Stream> DownloadFileAsync(string storagePath)
    {
        var blobClient = _containerClient.GetBlobClient(storagePath);

        var exists = await blobClient.ExistsAsync();
        if (!exists.Value)
            throw new FileNotFoundException($"Blob '{storagePath}' not found.");

        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }

    public async Task DeleteFileAsync(string storagePath)
    {
        var blobClient = _containerClient.GetBlobClient(storagePath);
        var deleted = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);

        if (deleted.Value)
            _logger.LogInformation("Deleted blob '{BlobName}'", storagePath);
        else
            _logger.LogWarning("Blob '{BlobName}' not found for deletion", storagePath);
    }

    public async Task<bool> FileExistsAsync(string storagePath)
    {
        var blobClient = _containerClient.GetBlobClient(storagePath);
        var response = await blobClient.ExistsAsync();
        return response.Value;
    }

    public string GetFileUrl(string storagePath)
    {
        if (!string.IsNullOrWhiteSpace(_blobSettings.BaseUrl))
            return $"{_blobSettings.BaseUrl.TrimEnd('/')}/{storagePath}";

        var blobClient = _containerClient.GetBlobClient(storagePath);
        return blobClient.Uri.ToString();
    }
}