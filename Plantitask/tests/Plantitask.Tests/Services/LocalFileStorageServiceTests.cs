using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Infrastructure.Services.Storage;

namespace Plantitask.Tests.Services
{
    /// <summary>
    /// Runs against a real directory under the OS temp path. The filesystem is the thing under
    /// test here, so mocking it away would leave nothing worth asserting.
    /// </summary>
    public class LocalFileStorageServiceTests : IDisposable
    {
        private readonly string _basePath;
        private readonly LocalFileStorageService _sut;

        public LocalFileStorageServiceTests()
        {
            _basePath = Path.Combine(Path.GetTempPath(), "plantitask-storage-tests", Guid.NewGuid().ToString());

            _sut = new LocalFileStorageService(
                Options.Create(new FileStorageSettings
                {
                    LocalStorage = new LocalStorageSettings
                    {
                        BasePath = _basePath,
                        BaseUrl = "https://files.example.com"
                    }
                }),
                NullLogger<LocalFileStorageService>.Instance);
        }

        public void Dispose()
        {
            if (Directory.Exists(_basePath))
                Directory.Delete(_basePath, recursive: true);
        }

        private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

        [Fact]
        public void Constructor_CreatesTheBaseDirectoryWhenItDoesNotExist()
        {
            Assert.True(Directory.Exists(_basePath));
        }

        [Fact]
        public async Task UploadFileAsync_WritesTheBytesAndReturnsTheStoredKey()
        {
            var storedKey = await _sut.UploadFileAsync(
                Bytes("hello from the other sidee"), "report.pdf", "application/pdf", "attachments");

            Assert.StartsWith("attachments/", storedKey);
            Assert.EndsWith(".pdf", storedKey);

            var onDisk = Path.Combine(_basePath, storedKey.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(onDisk));
            Assert.Equal("hello from the other sidee", await File.ReadAllTextAsync(onDisk));
        }

        /// <summary>
        /// Only the extension survives from whatever the client called the file. The stored name
        /// is a fresh Guid, so a hostile or merely awkward filename never reaches the disk.
        /// </summary>
        [Theory]
        [InlineData("holiday photo.png")]
        [InlineData("../../../etc/passwd.png")]
        [InlineData("..\\..\\windows\\system32\\evil.png")]
        [InlineData("con.png")]
        [InlineData("a;rm -rf /.png")]
        public async Task UploadFileAsync_KeepsNoneOfTheClientFilenameExceptTheExtension(string clientName)
        {
            var storedKey = await _sut.UploadFileAsync(
                Bytes("payload"), clientName, "image/png", "attachments");

            var storedFileName = Path.GetFileNameWithoutExtension(storedKey);

            Assert.True(Guid.TryParse(storedFileName, out _), storedKey);
            Assert.Equal("attachments/" + storedFileName + ".png", storedKey);
        }

        [Fact]
        public async Task UploadFileAsync_LowercasesTheExtension()
        {
            var storedKey = await _sut.UploadFileAsync(
                Bytes("payload"), "SCAN.PDF", "application/pdf", "attachments");

            Assert.EndsWith(".pdf", storedKey);
        }

        [Fact]
        public async Task UploadFileAsync_GivesTwoUploadsOfTheSameNameDifferentKeys()
        {
            var first = await _sut.UploadFileAsync(Bytes("one"), "same.png", "image/png", "attachments");
            var second = await _sut.UploadFileAsync(Bytes("two"), "same.png", "image/png", "attachments");

            Assert.NotEqual(first, second);
            Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(_basePath, first.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(_basePath, second.Replace('/', Path.DirectorySeparatorChar))));
        }

        [Fact]
        public async Task DownloadFileAsync_ReturnsWhatWasUploaded()
        {
            var storedKey = await _sut.UploadFileAsync(
                Bytes("round trip"), "notes.pdf", "application/pdf", "attachments");

            await using var stream = await _sut.DownloadFileAsync(storedKey);
            using var reader = new StreamReader(stream);

            Assert.Equal("round trip", await reader.ReadToEndAsync());
        }

        [Fact]
        public async Task DownloadFileAsync_ThrowsWhenTheFileIsGone()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => _sut.DownloadFileAsync("attachments/does-not-exist.pdf"));
        }

        [Fact]
        public async Task FileExistsAsync_AnswersForBothCases()
        {
            var storedKey = await _sut.UploadFileAsync(
                Bytes("here"), "here.png", "image/png", "attachments");

            Assert.True(await _sut.FileExistsAsync(storedKey));
            Assert.False(await _sut.FileExistsAsync("attachments/missing.png"));
        }

        [Fact]
        public async Task DeleteFileAsync_RemovesTheFile()
        {
            var storedKey = await _sut.UploadFileAsync(
                Bytes("temporary"), "temp.png", "image/png", "attachments");

            await _sut.DeleteFileAsync(storedKey);

            Assert.False(await _sut.FileExistsAsync(storedKey));
        }

        /// <summary>
        /// Deleting something already gone is success rather than an error. AttachmentService
        /// deletes the row first and the file afterwards, so a retry must not fail on the second
        /// attempt.
        /// </summary>
        [Fact]
        public async Task DeleteFileAsync_TreatsAnAlreadyMissingFileAsDone()
        {
            var thrown = await Record.ExceptionAsync(
                () => _sut.DeleteFileAsync("attachments/never-existed.png"));

            Assert.Null(thrown);
        }

        [Fact]
        public void GetFileUrl_ComposesTheConfiguredBaseUrl()
        {
            Assert.Equal(
                "https://files.example.com/avatars/abc.png",
                _sut.GetFileUrl("avatars/abc.png"));
        }

        /// <summary>
        /// The containment proof. Stored keys are server generated today, so this is defence in
        /// depth for whatever ends up in the database, and it has to hold on every path that
        /// touches the disk rather than only on download.
        /// </summary>
        [Theory]
        [InlineData("../escaped.txt")]
        [InlineData("..\\escaped.txt")]
        [InlineData("attachments/../../escaped.txt")]
        [InlineData("attachments/../../../../../../etc/passwd")]
        public async Task EveryPathThatTouchesTheDiskRefusesToLeaveTheStorageRoot(string escape)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DownloadFileAsync(escape));
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteFileAsync(escape));
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.FileExistsAsync(escape));
        }

        /// <summary>
        /// An absolute key ignores the base path entirely when combined, so it has to be caught
        /// by the same check rather than by anything about dot segments.
        /// </summary>
        [Fact]
        public async Task AnAbsolutePathIsRefusedTheSameWay()
        {
            var absolute = Path.Combine(Path.GetTempPath(), "outside-the-root.txt");

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DownloadFileAsync(absolute));
        }

        /// <summary>
        /// The separator in the StartsWith check is what makes this fail. A sibling directory
        /// whose name merely begins with the storage root shares a string prefix with it, so a
        /// prefix comparison alone would let it through.
        /// </summary>
        [Fact]
        public async Task ASiblingDirectorySharingTheRootsNameIsStillOutside()
        {
            var siblingLeak = "../" + Path.GetFileName(_basePath) + "-evil/file.txt";

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DownloadFileAsync(siblingLeak));
        }

        /// <summary>
        /// The folder argument is caller supplied rather than server generated, so it is the one
        /// part of an upload key that could carry a traversal
        /// </summary>
        [Fact]
        public async Task UploadFileAsync_RefusesAFolderThatEscapesTheRoot()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.UploadFileAsync(Bytes("payload"), "x.png", "image/png", "../../outside"));
        }
    }
}
