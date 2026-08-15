using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Plantitask.Core.Configuration;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class AttachmentServiceTests : DbTestBase
    {
        private const string StoredKey = "attachments/8f14e45f-stored.png";

        private static readonly byte[] PngBytes =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

        private readonly Mock<IFileStorageService> _storage = new();

        public AttachmentServiceTests(PostgresFixture fixture) : base(fixture)
        {
            _storage
                .Setup(s => s.UploadFileAsync(
                    It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(StoredKey);
        }

        private AttachmentService NewSut(IApplicationDbContext context) => new(
            context,
            _storage.Object,
            Options.Create(new FileStorageSettings
            {
                MaxFileSizeInMB = 5,
                AllowedExtensions = [".png", ".jpg", ".pdf"]
            }),
            new GroupService(
                context,
                Mock.Of<IGroupCodeGenerator>(),
                Mock.Of<IPasswordHasher>(),
                NullLogger<GroupService>.Instance),
            NullLogger<AttachmentService>.Instance);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
            db.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId));
            await db.SaveChangesAsync();
        }

        private static MemoryStream Png() => new(PngBytes);

        private async Task<Guid> SeedAttachmentAsync(
            Guid uploader,
            string fileName = "seeded.png",
            Guid? taskId = null,
            DateTime? createdAt = null)
        {
            await using var db = NewContext();

            var attachment = new TaskAttachment
            {
                TaskId = taskId ?? TaskId,
                FileName = fileName,
                FilePath = StoredKey,
                ContentType = "image/png",
                FileSize = PngBytes.Length,
                CreatedBy = uploader
            };

            db.TaskAttachments.Add(attachment);
            await db.SaveChangesAsync();

            if (createdAt.HasValue)
                await db.BackdateAsync<TaskAttachment>(attachment.Id, createdAt.Value);

            return attachment.Id;
        }

        private async Task PromoteMemberAsync(GroupRole role)
        {
            await using var db = NewContext();
            await db.SetRoleAsync(MemberId, role);
        }

        [Fact]
        public async Task UploadAttachmentAsync_WhenTheTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UploadAttachmentAsync(
                Guid.NewGuid(), Png(), "photo.png", LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        /// <summary>
        /// Membership is checked before any storage call happens, so an outsider never causes a
        /// write to the file system or a bill from a blob account.
        /// </summary>
        [Fact]
        public async Task UploadAttachmentAsync_WhenCallerLeadsAnotherGroup_ReturnsForbiddenAndStoresNothing()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UploadAttachmentAsync(
                TaskId, Png(), "photo.png", OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            _storage.Verify(s => s.UploadFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskAttachments.CountAsync());
        }

        [Fact]
        public async Task UploadAttachmentAsync_WhenTheExtensionIsNotAllowed_ReturnsBadRequestAndStoresNothing()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UploadAttachmentAsync(
                TaskId, Png(), "payload.exe", LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            _storage.Verify(s => s.UploadFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskAttachments.CountAsync());
        }

        /// <summary>
        /// The bytes have to agree with the extension. An allowed name over the wrong content is
        /// the case an allowlist alone cannot see, and it must fail before anything is stored.
        /// </summary>
        [Fact]
        public async Task UploadAttachmentAsync_WhenTheBytesDoNotMatchTheExtension_ReturnsBadRequest()
        {
            await SeedAsync();

            var executableBytes = new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

            await using var act = NewContext();
            var result = await NewSut(act).UploadAttachmentAsync(
                TaskId, executableBytes, "photo.png", LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            _storage.Verify(s => s.UploadFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task UploadAttachmentAsync_AsAPlainMember_StoresTheFileAndRecordsTheRow()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UploadAttachmentAsync(
                TaskId, Png(), "holiday photo.png", MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var dto = result.Value!;
            Assert.Equal("holiday photo.png", dto.FileName);
            Assert.Equal("image/png", dto.ContentType);
            Assert.Equal(PngBytes.Length, dto.FileSize);
            Assert.Equal("member", dto.UploadedByUserName);
            Assert.Equal($"/api/tasks/{TaskId}/attachments/{dto.Id}/download", dto.DownloadUrl);

            await using var assert = NewContext();
            var stored = await assert.TaskAttachments.SingleAsync();
            Assert.Equal(TaskId, stored.TaskId);
            Assert.Equal(MemberId, stored.CreatedBy);
            Assert.Equal(StoredKey, stored.FilePath);
            Assert.NotEqual(default, stored.CreatedAt);
        }

        /// <summary>
        /// The content type stored and later served is derived from the validated extension
        /// rather than taken from the client, so a caller cannot make us serve their file as
        /// something the browser will treat differently.
        /// </summary>
        [Fact]
        public async Task UploadAttachmentAsync_DerivesTheContentTypeFromTheValidatedExtension()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).UploadAttachmentAsync(TaskId, Png(), "photo.png", LeadId);

            _storage.Verify(s => s.UploadFileAsync(
                It.IsAny<Stream>(), "photo.png", "image/png", "attachments"), Times.Once);

            await using var assert = NewContext();
            Assert.Equal("image/png", (await assert.TaskAttachments.SingleAsync()).ContentType);
        }

        [Fact]
        public async Task GetTaskAttachmentsAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedAttachmentAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskAttachmentsAsync(TaskId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetTaskAttachmentsAsync_WhenTheTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskAttachmentsAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task GetTaskAttachmentsAsync_ReturnsOnlyThatTasksLiveAttachments()
        {
            await SeedAsync();
            await SeedAttachmentAsync(LeadId, fileName: "wanted.png");

            var otherTaskId = Guid.NewGuid();
            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, id: otherTaskId, title: "Another"));
                await db.SaveChangesAsync();
            }

            await SeedAttachmentAsync(LeadId, fileName: "other-task.png", taskId: otherTaskId);

            var doomedId = await SeedAttachmentAsync(LeadId, fileName: "deleted.png");
            await using (var del = NewContext())
            {
                await NewSut(del).DeleteAttachmentAsync(doomedId, LeadId);
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskAttachmentsAsync(TaskId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("wanted.png", Assert.Single(result.Value!).FileName);
        }

        [Fact]
        public async Task GetTaskAttachmentsAsync_ReturnsNewestFirst()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            await SeedAttachmentAsync(LeadId, fileName: "oldest.png", createdAt: now.AddHours(-3));
            await SeedAttachmentAsync(LeadId, fileName: "newest.png", createdAt: now);
            await SeedAttachmentAsync(LeadId, fileName: "middle.png", createdAt: now.AddHours(-1));

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskAttachmentsAsync(TaskId, LeadId);

            Assert.Equal(
                new[] { "newest.png", "middle.png", "oldest.png" },
                result.Value!.Select(a => a.FileName));
        }

        [Fact]
        public async Task GetAttachmentByIdAsync_WhenItDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetAttachmentByIdAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task GetAttachmentByIdAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).GetAttachmentByIdAsync(attachmentId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetAttachmentByIdAsync_ReturnsTheMetadataWithTheAuthorizedDownloadUrl()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(MemberId, fileName: "spec.png");

            await using var act = NewContext();
            var result = await NewSut(act).GetAttachmentByIdAsync(attachmentId, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("spec.png", result.Value!.FileName);
            Assert.Equal("member", result.Value.UploadedByUserName);
            Assert.Equal($"/api/tasks/{TaskId}/attachments/{attachmentId}/download", result.Value.DownloadUrl);
        }

        [Fact]
        public async Task DownloadAttachmentAsync_WhenItDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).DownloadAttachmentAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        /// <summary>
        /// The membership check has to happen before the bytes are fetched. A denial that still
        /// reads the file has already done the expensive and the risky part of the work.
        /// </summary>
        [Fact]
        public async Task DownloadAttachmentAsync_WhenCallerLeadsAnotherGroup_ReturnsForbiddenAndNeverReadsTheFile()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).DownloadAttachmentAsync(attachmentId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            _storage.Verify(s => s.DownloadFileAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DownloadAttachmentAsync_HandsBackTheStreamWithTheOriginalNameAndStoredType()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(LeadId, fileName: "holiday photo.png");

            _storage
                .Setup(s => s.DownloadFileAsync(StoredKey))
                .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("the bytes")));

            await using var act = NewContext();
            var result = await NewSut(act).DownloadAttachmentAsync(attachmentId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var (stream, fileName, contentType) = result.Value;
            Assert.Equal("holiday photo.png", fileName);
            Assert.Equal("image/png", contentType);

            using var reader = new StreamReader(stream);
            Assert.Equal("the bytes", await reader.ReadToEndAsync());
        }

        [Fact]
        public async Task DeleteAttachmentAsync_WhenItDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).DeleteAttachmentAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        /// <summary>
        /// Membership comes before the uploader check. Without that ordering someone who
        /// uploaded a file and then left the group could still reach back in and delete it.
        /// </summary>
        [Fact]
        public async Task DeleteAttachmentAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(OtherLeadId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteAttachmentAsync(attachmentId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal(1, await assert.TaskAttachments.CountAsync());
        }

        [Fact]
        public async Task DeleteAttachmentAsync_TheUploaderCanDeleteTheirOwnAtAnyRank()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteAttachmentAsync(attachmentId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskAttachments.CountAsync());
        }

        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, false)]
        [InlineData(GroupRole.Manager, true)]
        [InlineData(GroupRole.Owner, true)]
        public async Task DeleteAttachmentAsync_SomeoneElsesNeedsManagerOrAbove(GroupRole role, bool shouldSucceed)
        {
            await SeedAsync();
            await PromoteMemberAsync(role);
            var attachmentId = await SeedAttachmentAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteAttachmentAsync(attachmentId, MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);

            await using var assert = NewContext();
            Assert.Equal(shouldSucceed ? 0 : 1, await assert.TaskAttachments.CountAsync());
        }

        [Fact]
        public async Task DeleteAttachmentAsync_SoftDeletesTheRowAndRemovesTheStoredFile()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteAttachmentAsync(attachmentId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            _storage.Verify(s => s.DeleteFileAsync(StoredKey), Times.Once);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskAttachments.CountAsync());

            var stored = await assert.TaskAttachments.IgnoreQueryFilters().SingleAsync();
            Assert.True(stored.IsDeleted);
            Assert.NotNull(stored.DeletedAt);
            Assert.Equal(MemberId, stored.DeletedBy);
            Assert.Equal(StoredKey, stored.FilePath);
        }

        /// <summary>
        /// The row is the source of truth and it commits first, so a storage failure afterwards
        /// is logged and swallowed. Letting it surface would report a failure for a delete that
        /// already happened, and the retry would then hit a row that is no longer there.
        /// </summary>
        [Fact]
        public async Task DeleteAttachmentAsync_StillSucceedsWhenTheStoredFileCannotBeRemoved()
        {
            await SeedAsync();
            var attachmentId = await SeedAttachmentAsync(MemberId);

            _storage
                .Setup(s => s.DeleteFileAsync(It.IsAny<string>()))
                .ThrowsAsync(new IOException("the disk said no"));

            await using var act = NewContext();
            var result = await NewSut(act).DeleteAttachmentAsync(attachmentId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskAttachments.CountAsync());
            Assert.True((await assert.TaskAttachments.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        }
    }
}
