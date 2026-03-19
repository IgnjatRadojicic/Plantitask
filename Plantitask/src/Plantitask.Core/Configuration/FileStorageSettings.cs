using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Configuration
{
    public class FileStorageSettings
    {
        public string Provider { get; set; } = "Local";
        public LocalStorageSettings LocalStorage { get; set; } = new();
        public AzureBlobStorageSettings AzureBlobStorage { get; set; } = new();
        public int MaxFileSizeInMB { get; set; } = 10;
        public List<string> AllowedExtensions { get; set; } = new();
    }

    public class LocalStorageSettings
    {
        public string BasePath { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
    }

    public class AzureBlobStorageSettings
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
    }
}
