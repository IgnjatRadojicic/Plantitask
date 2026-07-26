using Plantitask.Core.Common;

namespace Plantitask.Core.Validation;

public static class FileUploadRules
{
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    // Longest signature we need to read: WebP checks bytes 8..11.
    private const int HeaderSize = 12;

    private static readonly Dictionary<string, byte[][]> Signatures = new()
    {
        [".png"] = [[0x89, 0x50, 0x4E, 0x47]],
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".gif"] = ["GIF87a"u8.ToArray(), "GIF89a"u8.ToArray()],
        [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],
        // .webp is not a prefix match handled in MatchesSignatureAsync.
        // Zip containers: normal, empty archive, spanned archive. Note this same family of
        // headers covers docx/xlsx/pptx/jar/apk/epub, so ".zip" is the weakest check here —
        // it proves "some zip container", not "a zip and not an Office file".
        // Zip bombs are not a server risk while nothing here decompresses uploads. If that
        // ever changes (preview, thumbnailing, text extraction, AV scan), the archive must be
        // read through a cap on DECOMPRESSED bytes, never the sizes declared in its header.
        [".zip"] = [
            [0x50, 0x4B, 0x03, 0x04],
            [0x50, 0x4B, 0x05, 0x06],
            [0x50, 0x4B, 0x07, 0x08],
        ],
    };

    // Layer 2: the type we serve on download. Never the client's header.
    // Keep aligned with Signatures — CanVerify enforces that both tables know the extension.
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
    };

    /// <summary>Config validation: is this extension one we can actually verify?</summary>
    public static bool CanVerify(string extension) =>
        (Signatures.ContainsKey(extension) || extension == ".webp")
        && ContentTypes.ContainsKey(extension);

    /// <summary>The content type to store and serve, derived from the validated extension.</summary>
    public static string ContentTypeFor(string extension) =>
        ContentTypes.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";

    /// <returns>The validated, lowercased extension — callers use it to derive ContentType.</returns>
    public static async Task<Result<string>> ValidateAsync(
        Stream content, string fileName, int maxSizeInMb, IReadOnlyCollection<string> allowedExtensions)
    {
        if (!content.CanSeek)
            throw new ArgumentException("Upload validation needs a seekable stream", nameof(content));

        if (content.Length <= 0)
            return Error.BadRequest("File is empty");

        var maxBytes = (long)maxSizeInMb * 1024 * 1024;
        if (content.Length > maxBytes)
            return Error.BadRequest($"File size exceeds maximum allowed size of {maxSizeInMb}MB");

        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        if (extension.Length == 0 || !allowedExtensions.Contains(extension))
            return Error.BadRequest(
                $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");

        if (!await MatchesSignatureAsync(content, extension))
            return Error.BadRequest($"File contents do not match a '{extension}' file");

        return extension;
    }

    private static async Task<bool> MatchesSignatureAsync(Stream content, string extension)
    {
        var header = new byte[HeaderSize];
        var read = await ReadHeaderAsync(content, header);

        if (extension == ".webp")
            return read >= 12
                && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && header.AsSpan(8, 4).SequenceEqual("WEBP"u8);

        if (!Signatures.TryGetValue(extension, out var candidates))
            return false;                       // unknown type = rejected, not "assumed fine"

        return candidates.Any(sig =>
            read >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig));
    }

    private static async Task<int> ReadHeaderAsync(Stream content, byte[] header)
    {
        content.Position = 0;
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        content.Position = 0;                   // caller still gets a stream starting at byte 0
        return read;
    }
}