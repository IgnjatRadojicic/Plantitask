namespace Plantitask.Web.Models;

public class ProfilePictureResponse
{
    // Storage key the upload endpoint just wrote. Run it through FileUrls.ToUrl to display it.
    public string Path { get; set; } = string.Empty;
}
