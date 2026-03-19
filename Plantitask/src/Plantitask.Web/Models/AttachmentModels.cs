namespace Plantitask.Web.Models;


// ===== Atachment Dtos =====
public class AttachmentDto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public string UploadedByUserName { get; set; } = string.Empty;

    public string FileSizeDisplay => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1048576 => $"{FileSize / 1024.0:F1} KB",
        < 1073741824 => $"{FileSize / 1048576.0:F1} MB",
        _ => $"{FileSize / 1073741824.0:F2} GB"
    };
}