using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services.Storage;

/// <summary>
/// Azure Blob twin of the local storage service, for real deployments. Same contract, same
/// server-generated names; the container is created private so nothing is ever reachable
/// without going through the API.
/// </summary>
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

    /// <summary>
    /// Uploads under a fresh Guid name with the server-derived content type. The IfNoneMatch
    /// condition is the blob equivalent of FileMode.CreateNew - a name collision fails instead
    /// of overwriting.
    /// </summary>
    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, string folder)
    {
        // Size/extension/content validation is the caller's job (FileUploadRules) so failures
        // surface as Result errors, not exceptions.
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        var storedName = $"{folder}/{Guid.NewGuid()}{extension}";

        var blobClient = _containerClient.GetBlobClient(storedName);

        await blobClient.UploadAsync(fileStream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        });

        _logger.LogInformation("Uploaded blob '{BlobName}' ({ContentType})", storedName, contentType);

        return storedName;
    }
    

    /// <summary>
    /// Streams a blob without buffering it in memory. Missing blobs throw for the same reason
    /// the local service throws - today a row always implies a file.
    /// </summary>
    public async Task<Stream> DownloadFileAsync(string storagePath)
    {
        var blobClient = _containerClient.GetBlobClient(storagePath);

        var exists = await blobClient.ExistsAsync();
        if (!exists.Value)
            throw new FileNotFoundException($"Blob '{storagePath}' not found.");

        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }

    /// <summary>Deletes the blob and its snapshots, logging a warning when it was already gone.</summary>
    public async Task DeleteFileAsync(string storagePath)
    {
        var blobClient = _containerClient.GetBlobClient(storagePath);
        var deleted = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);

        if (deleted.Value)
            _logger.LogInformation("Deleted blob '{BlobName}'", storagePath);
        else
            _logger.LogWarning("Blob '{BlobName}' not found for deletion", storagePath);
    }

    /// <summary>Whether the stored key still has a blob behind it.</summary>
    public async Task<bool> FileExistsAsync(string storagePath)
    {
        var blobClient = _containerClient.GetBlobClient(storagePath);
        var response = await blobClient.ExistsAsync();
        return response.Value;
    }

    /// <summary>
    /// Public URL for public-by-design content, preferring the configured CDN base when set.
    /// Group-scoped files never use this - they stream through the authorized endpoint.
    /// </summary>
    public string GetFileUrl(string storagePath)
    {
        if (!string.IsNullOrWhiteSpace(_blobSettings.BaseUrl))
            return $"{_blobSettings.BaseUrl.TrimEnd('/')}/{storagePath}";

        // GetBlobClient percent encodes the blob name and keeps any query string on the
        // container Uri where it belongs. Appending by hand gets both of those wrong.
        var blobClient = _containerClient.GetBlobClient(storagePath);
        return blobClient.Uri.ToString();
    }
}