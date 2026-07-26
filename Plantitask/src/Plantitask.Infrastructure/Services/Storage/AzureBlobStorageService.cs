using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services.Storage;

public class AzureBlobStorageService : IFileStorageService
{
    private readonly AzureBlobStorageSettings _blobSettings;
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly BlobContainerClient _containerClient;

    public AzureBlobStorageService(
        IOptions<FileStorageSettings> settings,
        ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        _blobSettings = settings.Value.AzureBlobStorage;

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
        // Size/extension/content validation is the caller's job (FileUploadRules) so failures
        // surface as Result errors, not exceptions.
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid()}{extension}";

        var blobClient = _containerClient.GetBlobClient(storedName);

        await blobClient.UploadAsync(fileStream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        });

        _logger.LogInformation("Uploaded blob '{BlobName}' ({ContentType})", storedName, contentType);

        return storedName;
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