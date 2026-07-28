namespace Plantitask.Web.Helpers;

/// <summary>
/// Turns a stored file path into an address the browser can load.
///
/// The API stores and returns storage keys like "a1b2c3.png" and never absolute URLs,
/// because the host part is configuration and configuration does not belong in the
/// database. Gluing the two together is the client's job and this is the only place
/// that does it.
///
/// Configured once from Program.cs. A static rather than an injected service is deliberate:
/// the value is read at startup and never changes, and injecting it would mean an @inject
/// line in every component that renders an avatar.
/// </summary>
public static class FileUrls
{
    private static string _baseUrl = string.Empty;

    public static void Configure(string baseUrl) => _baseUrl = baseUrl.TrimEnd('/');

    /// <param name="path">
    /// A storage key, or null when the user has no picture. Google SSO avatars are the one
    /// exception: those rows hold Google's own absolute URL, so they pass through untouched.
    /// </param>
    public static string? ToUrl(string? path) =>
        string.IsNullOrEmpty(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{_baseUrl}/{path}";
}
