namespace Plantitask.Web.Models;

public record FileDownload(byte[] Content, string FileName, string ContentType);
