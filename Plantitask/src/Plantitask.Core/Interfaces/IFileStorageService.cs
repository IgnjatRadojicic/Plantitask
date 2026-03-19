using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Interfaces
{
    public interface IFileStorageService
    {
        Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);

        Task<Stream> DownloadFileAsync(string storagePath);
        Task<bool> FileExistsAsync(string storagePath);
        public Task DeleteFileAsync(string storagePath);
        string GetFileUrl(string storagePath);
    }
}
