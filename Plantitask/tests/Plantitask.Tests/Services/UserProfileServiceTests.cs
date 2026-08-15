using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class UserProfileServiceTests : DbTestBase
    {
        private const string StoredKey = "avatars/8f14e45f-stored.png";

        private static readonly byte[] PngBytes =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

        private readonly Mock<IFileStorageService> _storage = new();

        public UserProfileServiceTests(PostgresFixture fixture) : base(fixture)
        {
            _storage
                .Setup(s => s.UploadFileAsync(
                    It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(StoredKey);
        }

        private UserProfileService NewSut(IApplicationDbContext context) => new(
            context, _storage.Object, NullLogger<UserProfileService>.Instance);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private static MemoryStream Png() => new(PngBytes);

        private async Task<User> ReadUserAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.Users.SingleAsync(u => u.Id == id);
        }

        private async Task SetPictureAsync(Guid userId, string? path)
        {
            await using var db = NewContext();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.ProfilePicturePath = path;
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task GetProfileAsync_ReturnsTheCallersOwnProfile()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetProfileAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("member", result.Value!.UserName);
            Assert.Equal("member@example.com", result.Value.Email);
            Assert.Equal(5, result.Value.MaxGroups);
            Assert.False(result.Value.IsPremium);
        }

        [Fact]
        public async Task GetProfileAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetProfileAsync(Guid.NewGuid());

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        /// <summary>
        /// The projection computes IsPremium rather than reading the stored flag, so a premium
        /// row whose expiry has passed reports itself as free even before the nightly job runs.
        /// </summary>
        [Fact]
        public async Task GetProfileAsync_ReportsAnExpiredPremiumAsNoLongerPremium()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                var user = await db.Users.SingleAsync(u => u.Id == MemberId);
                user.IsPremium = true;
                user.SubscriptionType = "onetime";
                user.PremiumExpiresAt = DateTime.UtcNow.AddDays(-1);
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetProfileAsync(MemberId);

            Assert.False(result.Value!.IsPremium);
        }

        [Fact]
        public async Task GetProfileAsync_ReportsALiveRecurringSubscriptionAsPremium()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                var user = await db.Users.SingleAsync(u => u.Id == MemberId);
                user.IsPremium = true;
                user.SubscriptionType = "subscription";
                user.PremiumExpiresAt = null;
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetProfileAsync(MemberId);

            Assert.True(result.Value!.IsPremium);
        }

        [Fact]
        public async Task UpdateProfileAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateProfileAsync(
                Guid.NewGuid(), new UpdateUserProfileDto { UserName = "whoever" });

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task UpdateProfileAsync_ChangesTheNames()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateProfileAsync(MemberId, new UpdateUserProfileDto
            {
                UserName = "renamed",
                FirstName = "Ada",
                LastName = "Lovelace"
            });

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("renamed", result.Value!.UserName);

            var stored = await ReadUserAsync(MemberId);
            Assert.Equal("renamed", stored.UserName);
            Assert.Equal("Ada", stored.FirstName);
            Assert.Equal("Lovelace", stored.LastName);
        }

        /// <summary>
        /// The name is trimmed before the availability check so the value that gets looked up is
        /// the value that gets stored. Checking one string and saving a different one would let a
        /// taken name through.
        /// </summary>
        [Fact]
        public async Task UpdateProfileAsync_TrimsTheUsernameBeforeCheckingAndStoringIt()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateProfileAsync(
                MemberId, new UpdateUserProfileDto { UserName = "  spaced  " });

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("spaced", (await ReadUserAsync(MemberId)).UserName);
        }

        [Fact]
        public async Task UpdateProfileAsync_TrimsTheGivenNames()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).UpdateProfileAsync(MemberId, new UpdateUserProfileDto
            {
                FirstName = "  Ada  ",
                LastName = "  Lovelace  "
            });

            var stored = await ReadUserAsync(MemberId);
            Assert.Equal("Ada", stored.FirstName);
            Assert.Equal("Lovelace", stored.LastName);
        }

        [Fact]
        public async Task UpdateProfileAsync_RejectsAUsernameSomebodyElseAlreadyHas()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateProfileAsync(
                MemberId, new UpdateUserProfileDto { UserName = "lead" });

            Assert.True(result.IsFailure);
            Assert.Equal("Conflict", result.Error!.Code);
            Assert.Equal("member", (await ReadUserAsync(MemberId)).UserName);
        }

        /// <summary>
        /// Submitting the name you already have is not a clash with yourself. The availability
        /// check excludes the caller, and the rename check short circuits before it anyway.
        /// </summary>
        [Fact]
        public async Task UpdateProfileAsync_AcceptsTheUsernameTheCallerAlreadyHas()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateProfileAsync(
                MemberId, new UpdateUserProfileDto { UserName = "member", FirstName = "Ada" });

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Ada", (await ReadUserAsync(MemberId)).FirstName);
        }

        /// <summary>
        /// Usernames are case sensitive by decision, so member and Member are two different
        /// handles and changing between them is a real rename rather than a no op.
        /// </summary>
        [Fact]
        public async Task UpdateProfileAsync_TreatsAChangeOfCaseAsARealRename()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateProfileAsync(
                MemberId, new UpdateUserProfileDto { UserName = "Member" });

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Member", (await ReadUserAsync(MemberId)).UserName);
        }

        [Fact]
        public async Task UpdateProfileAsync_LeavesTheUsernameAloneWhenNoneIsSupplied()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).UpdateProfileAsync(MemberId, new UpdateUserProfileDto { FirstName = "Ada" });

            var stored = await ReadUserAsync(MemberId);
            Assert.Equal("member", stored.UserName);
            Assert.Equal("Ada", stored.FirstName);
        }

        /// <summary>
        /// A null name means leave it alone while an empty string means clear it, which is the
        /// same three way distinction the task description uses.
        /// </summary>
        [Fact]
        public async Task UpdateProfileAsync_ClearsAGivenNameWhenSentAnEmptyString()
        {
            await SeedAsync();

            await using (var first = NewContext())
                await NewSut(first).UpdateProfileAsync(MemberId, new UpdateUserProfileDto { FirstName = "Ada" });

            await using (var second = NewContext())
                await NewSut(second).UpdateProfileAsync(MemberId, new UpdateUserProfileDto { FirstName = "" });

            Assert.Equal(string.Empty, (await ReadUserAsync(MemberId)).FirstName);
        }

        [Fact]
        public async Task UploadProfilePictureAsync_StoresTheFileAndRecordsTheKey()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UploadProfilePictureAsync(MemberId, Png(), "avatar.png");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(StoredKey, result.Value);

            Assert.Equal(StoredKey, (await ReadUserAsync(MemberId)).ProfilePicturePath);
        }

        [Fact]
        public async Task UploadProfilePictureAsync_DerivesTheContentTypeAndFoldersItUnderAvatars()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).UploadProfilePictureAsync(MemberId, Png(), "avatar.png");

            _storage.Verify(s => s.UploadFileAsync(
                It.IsAny<Stream>(), "avatar.png", "image/png", "avatars"), Times.Once);
        }

        [Fact]
        public async Task UploadProfilePictureAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UploadProfilePictureAsync(Guid.NewGuid(), Png(), "avatar.png");

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);

            _storage.Verify(s => s.UploadFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        /// <summary>
        /// Profile pictures are images only, unlike task attachments where the allowlist is
        /// wider. A pdf is a legitimate attachment and never a legitimate avatar.
        /// </summary>
        [Fact]
        public async Task UploadProfilePictureAsync_RejectsAFileTypeThatIsNotAnImage()
        {
            await SeedAsync();

            var pdf = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x00, 0x00, 0x00, 0x00]);

            await using var act = NewContext();
            var result = await NewSut(act).UploadProfilePictureAsync(MemberId, pdf, "document.pdf");

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            _storage.Verify(s => s.UploadFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task UploadProfilePictureAsync_RejectsBytesThatDoNotMatchTheExtension()
        {
            await SeedAsync();

            var executable = new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

            await using var act = NewContext();
            var result = await NewSut(act).UploadProfilePictureAsync(MemberId, executable, "avatar.png");

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.Null((await ReadUserAsync(MemberId)).ProfilePicturePath);
        }

        [Fact]
        public async Task UploadProfilePictureAsync_DeletesThePictureItReplaced()
        {
            await SeedAsync();
            await SetPictureAsync(MemberId, "avatars/previous.png");

            await using var act = NewContext();
            await NewSut(act).UploadProfilePictureAsync(MemberId, Png(), "avatar.png");

            _storage.Verify(s => s.DeleteFileAsync("avatars/previous.png"), Times.Once);
            Assert.Equal(StoredKey, (await ReadUserAsync(MemberId)).ProfilePicturePath);
        }

        /// <summary>
        /// A Google SSO avatar lives on Google's CDN so the column holds their absolute URL
        /// rather than one of our storage keys. Handing that to the storage layer would at best
        /// fail and at worst resolve to something of ours.
        /// </summary>
        [Theory]
        [InlineData("https://lh3.googleusercontent.com/a/photo.jpg")]
        [InlineData("http://example.com/avatar.png")]
        public async Task UploadProfilePictureAsync_NeverTriesToDeleteAnExternalAvatarUrl(string externalUrl)
        {
            await SeedAsync();
            await SetPictureAsync(MemberId, externalUrl);

            await using var act = NewContext();
            await NewSut(act).UploadProfilePictureAsync(MemberId, Png(), "avatar.png");

            _storage.Verify(s => s.DeleteFileAsync(It.IsAny<string>()), Times.Never);
            Assert.Equal(StoredKey, (await ReadUserAsync(MemberId)).ProfilePicturePath);
        }

        [Fact]
        public async Task UploadProfilePictureAsync_DeletesNothingWhenThereWasNoPictureBefore()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).UploadProfilePictureAsync(MemberId, Png(), "avatar.png");

            _storage.Verify(s => s.DeleteFileAsync(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// The new key commits before the old file is removed, so a failed cleanup leaves a
        /// stray file rather than a profile pointing at something that is no longer there.
        /// </summary>
        [Fact]
        public async Task UploadProfilePictureAsync_StillSucceedsWhenTheOldFileCannotBeDeleted()
        {
            await SeedAsync();
            await SetPictureAsync(MemberId, "avatars/previous.png");

            _storage
                .Setup(s => s.DeleteFileAsync(It.IsAny<string>()))
                .ThrowsAsync(new IOException("the disk said no"));

            await using var act = NewContext();
            var result = await NewSut(act).UploadProfilePictureAsync(MemberId, Png(), "avatar.png");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(StoredKey, (await ReadUserAsync(MemberId)).ProfilePicturePath);
        }

        [Fact]
        public async Task RemoveProfilePictureAsync_ClearsTheColumnAndDeletesTheFile()
        {
            await SeedAsync();
            await SetPictureAsync(MemberId, "avatars/current.png");

            await using var act = NewContext();
            var result = await NewSut(act).RemoveProfilePictureAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Null((await ReadUserAsync(MemberId)).ProfilePicturePath);

            _storage.Verify(s => s.DeleteFileAsync("avatars/current.png"), Times.Once);
        }

        [Fact]
        public async Task RemoveProfilePictureAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).RemoveProfilePictureAsync(Guid.NewGuid());

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task RemoveProfilePictureAsync_IsHarmlessWhenThereIsNoPicture()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).RemoveProfilePictureAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            _storage.Verify(s => s.DeleteFileAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RemoveProfilePictureAsync_ClearsAnExternalAvatarWithoutDeletingAnything()
        {
            await SeedAsync();
            await SetPictureAsync(MemberId, "https://lh3.googleusercontent.com/a/photo.jpg");

            await using var act = NewContext();
            var result = await NewSut(act).RemoveProfilePictureAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Null((await ReadUserAsync(MemberId)).ProfilePicturePath);
            _storage.Verify(s => s.DeleteFileAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RemoveProfilePictureAsync_StillSucceedsWhenTheFileCannotBeDeleted()
        {
            await SeedAsync();
            await SetPictureAsync(MemberId, "avatars/current.png");

            _storage
                .Setup(s => s.DeleteFileAsync(It.IsAny<string>()))
                .ThrowsAsync(new IOException("the disk said no"));

            await using var act = NewContext();
            var result = await NewSut(act).RemoveProfilePictureAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Null((await ReadUserAsync(MemberId)).ProfilePicturePath);
        }
    }
}
