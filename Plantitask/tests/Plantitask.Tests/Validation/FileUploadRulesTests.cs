using Plantitask.Core.Validation;

namespace Plantitask.Tests.Validation
{
    public class FileUploadRulesTests
    {
        private static readonly string[] Allowed = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".zip"];

        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];
        private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31];
        private static readonly byte[] Gif87 = "GIF87a"u8.ToArray();
        private static readonly byte[] Gif89 = "GIF89a"u8.ToArray();
        private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
        private static readonly byte[] ZipEmpty = [0x50, 0x4B, 0x05, 0x06];
        private static readonly byte[] ZipSpanned = [0x50, 0x4B, 0x07, 0x08];

        /// <summary>A stream whose first bytes are the given signature, padded to a realistic size.</summary>
        private static MemoryStream FileOf(byte[] signature, int totalBytes = 64)
        {
            var buffer = new byte[Math.Max(totalBytes, signature.Length)];
            signature.CopyTo(buffer, 0);
            return new MemoryStream(buffer);
        }

        private static MemoryStream Webp(string riff = "RIFF", string format = "WEBP")
        {
            var buffer = new byte[64];
            System.Text.Encoding.ASCII.GetBytes(riff).CopyTo(buffer, 0);
            System.Text.Encoding.ASCII.GetBytes(format).CopyTo(buffer, 8);
            return new MemoryStream(buffer);
        }

        private static Task<Plantitask.Core.Common.Result<string>> Validate(
            Stream content, string fileName, int maxMb = 10)
            => FileUploadRules.ValidateAsync(content, fileName, maxMb, Allowed);

        [Fact]
        public async Task ValidateAsync_AcceptsAFileWhoseBytesMatchItsExtension()
        {
            var result = await Validate(FileOf(Png), "holiday.png");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(".png", result.Value);
        }

        [Fact]
        public async Task ValidateAsync_NormalisesTheExtensionToLowercase()
        {
            var result = await Validate(FileOf(Pdf), "REPORT.PDF");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(".pdf", result.Value);
        }

        [Fact]
        public async Task ValidateAsync_RejectsAnEmptyFile()
        {
            var result = await Validate(new MemoryStream(), "empty.png");

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.Contains("empty", result.Error.Message);
        }

        [Fact]
        public async Task ValidateAsync_RejectsAFileOverTheConfiguredLimit()
        {
            var twoMegabytes = FileOf(Png, totalBytes: 2 * 1024 * 1024);

            var result = await Validate(twoMegabytes, "big.png", maxMb: 1);

            Assert.True(result.IsFailure);
            Assert.Contains("1MB", result.Error!.Message);
        }

        [Theory]
        [InlineData("script.exe")]
        [InlineData("archive.tar")]
        [InlineData("noextension")]
        public async Task ValidateAsync_RejectsAnExtensionOutsideTheAllowlist(string fileName)
        {
            var result = await Validate(FileOf(Png), fileName);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        /// <summary>
        /// The defence that an extension allowlist alone does not give you. Renaming an
        /// executable to photo.png gets past the name check and has to be caught by its bytes.
        /// </summary>
        [Fact]
        public async Task ValidateAsync_RejectsBytesThatDoNotMatchTheClaimedExtension()
        {
            var windowsExecutable = FileOf([0x4D, 0x5A, 0x90, 0x00]);

            var result = await Validate(windowsExecutable, "photo.png");

            Assert.True(result.IsFailure);
            Assert.Contains("do not match", result.Error!.Message);
        }

        [Fact]
        public async Task ValidateAsync_RejectsAValidFileTypeRenamedToAnotherValidType()
        {
            var result = await Validate(FileOf(Png), "actually-a-png.pdf");

            Assert.True(result.IsFailure);
            Assert.Contains("do not match", result.Error!.Message);
        }

        [Theory]
        [InlineData("photo.jpg")]
        [InlineData("photo.jpeg")]
        public async Task ValidateAsync_AcceptsBothJpegSpellings(string fileName)
        {
            var result = await Validate(FileOf(Jpeg), fileName);

            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        [Fact]
        public async Task ValidateAsync_AcceptsEitherGifGeneration()
        {
            Assert.True((await Validate(FileOf(Gif87), "old.gif")).IsSuccess);
            Assert.True((await Validate(FileOf(Gif89), "new.gif")).IsSuccess);
        }

        /// <summary>
        /// Zip has three container headers depending on whether the archive is normal, empty or
        /// spanned. Recognising only the first would reject legitimate uploads.
        /// </summary>
        [Fact]
        public async Task ValidateAsync_AcceptsAllThreeZipContainerHeaders()
        {
            Assert.True((await Validate(FileOf(Zip), "normal.zip")).IsSuccess);
            Assert.True((await Validate(FileOf(ZipEmpty), "empty.zip")).IsSuccess);
            Assert.True((await Validate(FileOf(ZipSpanned), "spanned.zip")).IsSuccess);
        }

        /// <summary>
        /// WebP is the one type that is not a prefix match. The RIFF container tag sits at byte
        /// zero and the format tag at byte eight, so a RIFF file that is actually a WAV must be
        /// rejected even though its first four bytes look right.
        /// </summary>
        [Fact]
        public async Task ValidateAsync_ChecksTheWebpFormatTagAndNotJustTheRiffContainer()
        {
            Assert.True((await Validate(Webp(), "photo.webp")).IsSuccess);

            var riffButNotWebp = await Validate(Webp(format: "WAVE"), "photo.webp");

            Assert.True(riffButNotWebp.IsFailure);
            Assert.Contains("do not match", riffButNotWebp.Error!.Message);
        }

        [Fact]
        public async Task ValidateAsync_RejectsAFileTooShortToCarryItsSignature()
        {
            var threeBytes = new MemoryStream([0x89, 0x50, 0x4E]);

            var result = await Validate(threeBytes, "truncated.png");

            Assert.True(result.IsFailure);
        }

        /// <summary>
        /// An extension can be allowed by configuration and still have no signature we know how
        /// to check. The unknown case rejects rather than assuming the file is fine, which is
        /// what CanVerify exists to catch at startup.
        /// </summary>
        [Fact]
        public async Task ValidateAsync_RejectsAnAllowedExtensionItCannotVerify()
        {
            var result = await FileUploadRules.ValidateAsync(
                FileOf(Png), "notes.txt", 10, [".txt"]);

            Assert.True(result.IsFailure);
            Assert.Contains("do not match", result.Error!.Message);
        }

        /// <summary>
        /// The caller uploads the same stream straight after validating it, so validation has to
        /// hand it back positioned at byte zero. Leave it where the header read finished and
        /// every stored file loses its first twelve bytes.
        /// </summary>
        [Fact]
        public async Task ValidateAsync_LeavesTheStreamReadyToUpload()
        {
            var content = FileOf(Png);

            await Validate(content, "holiday.png");

            Assert.Equal(0, content.Position);
        }

        [Fact]
        public async Task ValidateAsync_RefusesANonSeekableStreamOutright()
        {
            await using var unseekable = new NonSeekableStream(FileOf(Png));

            await Assert.ThrowsAsync<ArgumentException>(() => Validate(unseekable, "holiday.png"));
        }

        [Theory]
        [InlineData(".png", "image/png")]
        [InlineData(".jpg", "image/jpeg")]
        [InlineData(".jpeg", "image/jpeg")]
        [InlineData(".gif", "image/gif")]
        [InlineData(".webp", "image/webp")]
        [InlineData(".pdf", "application/pdf")]
        [InlineData(".zip", "application/zip")]
        public void ContentTypeFor_MapsEveryVerifiableExtension(string extension, string expected)
        {
            Assert.Equal(expected, FileUploadRules.ContentTypeFor(extension));
        }

        /// <summary>
        /// The served content type is derived here rather than taken from the client, so an
        /// extension nobody mapped has to fall back to something inert rather than to whatever
        /// the uploader claimed.
        /// </summary>
        [Fact]
        public void ContentTypeFor_FallsBackToOctetStreamForAnythingUnmapped()
        {
            Assert.Equal("application/octet-stream", FileUploadRules.ContentTypeFor(".svg"));
        }

        [Theory]
        [InlineData(".png", true)]
        [InlineData(".webp", true)]
        [InlineData(".zip", true)]
        [InlineData(".txt", false)]
        [InlineData(".svg", false)]
        [InlineData(".exe", false)]
        public void CanVerify_AnswersWhetherBothTablesKnowTheExtension(string extension, bool expected)
        {
            Assert.Equal(expected, FileUploadRules.CanVerify(extension));
        }

        private sealed class NonSeekableStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableStream(Stream inner) => _inner = inner;

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override void Flush() => _inner.Flush();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
