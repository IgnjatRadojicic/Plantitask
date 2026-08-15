using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.Constants;
using Plantitask.Core.DTO.Groups;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class GroupServiceTests : DbTestBase
    {
        private const string GeneratedCode = "NEWCODE1";
        private const string DevTeamCode = "DEV12345";

        private readonly Mock<IGroupCodeGenerator> _codes = new();
        private readonly Mock<IPasswordHasher> _hasher = new();

        public GroupServiceTests(PostgresFixture fixture) : base(fixture)
        {
            _codes.Setup(c => c.Generate()).Returns(GeneratedCode);
            _codes.Setup(c => c.IsValid(It.IsAny<string>())).Returns(true);

            // A reversible stand in for BCrypt. The real hasher costs a third of a second a call
            // and nothing here is testing how a password is hashed, only which one was checked.
            _hasher.Setup(h => h.HashPassword(It.IsAny<string>()))
                .Returns<string>(plain => $"hashed:{plain}");
            _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((plain, hash) => hash == $"hashed:{plain}");
        }

        private GroupService NewSut(IApplicationDbContext context) => new(
            context, _codes.Object, _hasher.Object, NullLogger<GroupService>.Instance);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private async Task SetRoleAsync(Guid userId, GroupRole role)
        {
            await using var db = NewContext();
            await db.SetRoleAsync(userId, role);
        }

        private async Task SetGroupPasswordAsync(string? hash)
        {
            await using var db = NewContext();
            var group = await db.Groups.SingleAsync(g => g.Id == GroupId);
            group.PasswordHash = hash;
            await db.SaveChangesAsync();
        }

        private async Task SetMaxGroupsAsync(Guid userId, int maxGroups)
        {
            await using var db = NewContext();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.MaxGroups = maxGroups;
            await db.SaveChangesAsync();
        }

        private async Task<GroupMember?> ReadMembershipAsync(Guid userId, bool includeDeleted = false)
        {
            await using var db = NewContext();
            var query = includeDeleted ? db.GroupMembers.IgnoreQueryFilters() : db.GroupMembers;
            return await query.FirstOrDefaultAsync(gm => gm.GroupId == GroupId && gm.UserId == userId);
        }

        [Fact]
        public async Task IsUserMemberAsync_AnswersForMembersAndOutsiders()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.True(await sut.IsUserMemberAsync(GroupId, MemberId));
            Assert.False(await sut.IsUserMemberAsync(GroupId, OutsiderId));
            Assert.False(await sut.IsUserMemberAsync(GroupId, OtherLeadId));
        }

        /// <summary>
        /// A removed membership is soft deleted, so the global filter is what turns a removal
        /// into a non membership. Every authorization check in the codebase leans on this.
        /// </summary>
        [Fact]
        public async Task IsUserMemberAsync_TreatsARemovedMembershipAsNotAMember()
        {
            await SeedAsync();

            await using (var del = NewContext())
                await NewSut(del).LeaveGroupAsync(GroupId, MemberId);

            await using var act = NewContext();
            Assert.False(await NewSut(act).IsUserMemberAsync(GroupId, MemberId));
        }

        [Fact]
        public async Task GetUserRoleAsync_ReturnsTheRankAndNullForANonMember()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.Equal(GroupRole.TeamLead, await sut.GetUserRoleAsync(GroupId, LeadId));
            Assert.Equal(GroupRole.Member, await sut.GetUserRoleAsync(GroupId, MemberId));
            Assert.Null(await sut.GetUserRoleAsync(GroupId, OutsiderId));
        }

        /// <summary>
        /// The caller becomes Owner in the same save that creates the group. A group with no
        /// owner membership would be unmanageable by anybody.
        /// </summary>
        [Fact]
        public async Task CreateGroupAsync_MakesTheCreatorItsOwner()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CreateGroupAsync(
                new CreateGroupDto { Name = "New Team" }, OutsiderId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(GroupRole.Owner, result.Value!.UserRole);
            Assert.Equal(GeneratedCode, result.Value.GroupCode);
            Assert.Equal(1, result.Value.MemberCount);
            Assert.False(result.Value.IsPasswordProtected);

            await using var assert = NewContext();
            var created = await assert.Groups.SingleAsync(g => g.GroupCode == GeneratedCode);
            Assert.Equal(OutsiderId, created.OwnerId);

            var membership = await assert.GroupMembers.SingleAsync(gm => gm.GroupId == created.Id);
            Assert.Equal((int)GroupRole.Owner, membership.RoleId);
        }

        [Fact]
        public async Task CreateGroupAsync_HashesAnOptionalPasswordRatherThanStoringIt()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CreateGroupAsync(
                new CreateGroupDto { Name = "Locked Team", Password = "supersecret" }, OutsiderId);

            Assert.True(result.Value!.IsPasswordProtected);

            // What is provable here is that the value went through the hasher rather than being
            // assigned raw. That the hash is irreversible is PasswordHasher's own test.
            _hasher.Verify(h => h.HashPassword("supersecret"), Times.Once);

            await using var assert = NewContext();
            var created = await assert.Groups.SingleAsync(g => g.GroupCode == GeneratedCode);
            Assert.Equal("hashed:supersecret", created.PasswordHash);
            Assert.NotEqual("supersecret", created.PasswordHash);
        }

        [Fact]
        public async Task CreateGroupAsync_RetriesUntilItFindsAnUnusedCode()
        {
            await SeedAsync();

            _codes.SetupSequence(c => c.Generate())
                .Returns(DevTeamCode)
                .Returns(GeneratedCode);

            await using var act = NewContext();
            var result = await NewSut(act).CreateGroupAsync(
                new CreateGroupDto { Name = "New Team" }, OutsiderId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(GeneratedCode, result.Value!.GroupCode);
        }

        /// <summary>
        /// Five collisions against a thirty two to the eight space is not bad luck, it means the
        /// generator is broken, so it gives up loudly instead of looping forever.
        /// </summary>
        [Fact]
        public async Task CreateGroupAsync_GivesUpRatherThanLoopingWhenEveryCodeCollides()
        {
            await SeedAsync();

            _codes.Setup(c => c.Generate()).Returns(DevTeamCode);

            await using var act = NewContext();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => NewSut(act).CreateGroupAsync(new CreateGroupDto { Name = "New Team" }, OutsiderId));
        }

        [Fact]
        public async Task CreateGroupAsync_RefusesOnceTheUsersPlanLimitIsReached()
        {
            await SeedAsync();
            await SetMaxGroupsAsync(MemberId, 1);

            await using var act = NewContext();
            var result = await NewSut(act).CreateGroupAsync(
                new CreateGroupDto { Name = "One Too Many" }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
            Assert.Contains("limit of 1", result.Error.Message);
        }

        [Fact]
        public async Task CreateGroupAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CreateGroupAsync(
                new CreateGroupDto { Name = "Ghost Team" }, Guid.NewGuid());

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task JoinGroupAsync_AddsTheCallerAsAPlainMember()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, OutsiderId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(GroupRole.Member, result.Value!.UserRole);
            Assert.Equal(3, result.Value.MemberCount);

            Assert.Equal((int)GroupRole.Member, (await ReadMembershipAsync(OutsiderId))!.RoleId);
        }

        [Fact]
        public async Task JoinGroupAsync_UppercasesAndTrimsTheCodeBeforeLookingItUp()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = "  dev12345  " }, OutsiderId);

            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        [Fact]
        public async Task JoinGroupAsync_RejectsACodeTheGeneratorSaysIsMalformed()
        {
            await SeedAsync();

            _codes.Setup(c => c.IsValid(It.IsAny<string>())).Returns(false);

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = "nonsense" }, OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task JoinGroupAsync_ReturnsNotFoundForAWellFormedCodeThatMatchesNoGroup()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = "ZZZZZZZZ" }, OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task JoinGroupAsync_RefusesAnInactiveGroup()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                var group = await db.Groups.SingleAsync(g => g.Id == GroupId);
                group.IsActive = false;
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task JoinGroupAsync_RejectsSomebodyWhoIsAlreadyAMember()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Conflict", result.Error!.Code);
        }

        [Fact]
        public async Task JoinGroupAsync_RequiresThePasswordWhenTheGroupHasOne()
        {
            await SeedAsync();
            await SetGroupPasswordAsync("hashed:letmein");

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
            Assert.Null(await ReadMembershipAsync(OutsiderId, includeDeleted: true));
        }

        [Fact]
        public async Task JoinGroupAsync_RejectsTheWrongPassword()
        {
            await SeedAsync();
            await SetGroupPasswordAsync("hashed:letmein");

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode, Password = "guess" }, OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
            Assert.Null(await ReadMembershipAsync(OutsiderId, includeDeleted: true));
        }

        [Fact]
        public async Task JoinGroupAsync_AcceptsTheCorrectPassword()
        {
            await SeedAsync();
            await SetGroupPasswordAsync("hashed:letmein");

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode, Password = "letmein" }, OutsiderId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(result.Value!.IsPasswordProtected);
        }

        /// <summary>
        /// The bug that shipped once. A previously removed member has a soft deleted row, and if
        /// the restore branch runs before the password check then leaving and rejoining is a way
        /// past the password entirely. The check has to come first.
        /// </summary>
        [Fact]
        public async Task JoinGroupAsync_MakesAReturningMemberPassThePasswordToo()
        {
            await SeedAsync();

            await using (var leave = NewContext())
                await NewSut(leave).LeaveGroupAsync(GroupId, MemberId);

            await SetGroupPasswordAsync("hashed:letmeinnnnnnnnnnnnnnnnnnnn");

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            var membership = await ReadMembershipAsync(MemberId, includeDeleted: true);
            Assert.True(membership!.IsDeleted);
        }

        [Fact]
        public async Task JoinGroupAsync_RestoresARemovedMembershipInsteadOfAddingASecondRow()
        {
            await SeedAsync();

            await using (var leave = NewContext())
                await NewSut(leave).LeaveGroupAsync(GroupId, MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            var rows = await assert.GroupMembers
                .IgnoreQueryFilters()
                .Where(gm => gm.GroupId == GroupId && gm.UserId == MemberId)
                .ToListAsync();

            var restored = Assert.Single(rows);
            Assert.False(restored.IsDeleted);
            Assert.Null(restored.DeletedAt);
            Assert.Null(restored.DeletedBy);
        }

        /// <summary>
        /// Somebody who was a Manager and got removed comes back as a plain Member. Restoring the
        /// old rank would hand back moderation powers to anyone who was ever demoted by removal.
        /// </summary>
        [Fact]
        public async Task JoinGroupAsync_BringsARejoinerBackAsAPlainMemberWhateverTheyWereBefore()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);

            await using (var leave = NewContext())
                await NewSut(leave).LeaveGroupAsync(GroupId, MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(GroupRole.Member, result.Value!.UserRole);
            Assert.Equal((int)GroupRole.Member, (await ReadMembershipAsync(MemberId))!.RoleId);
        }

        [Fact]
        public async Task JoinGroupAsync_RefusesOnceTheUsersPlanLimitIsReached()
        {
            await SeedAsync();
            await SetMaxGroupsAsync(OtherLeadId, 1);

            await using var act = NewContext();
            var result = await NewSut(act).JoinGroupAsync(
                new JoinGroupDto { GroupCode = DevTeamCode }, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
            Assert.Null(await ReadMembershipAsync(OtherLeadId, includeDeleted: true));
        }

        [Fact]
        public async Task GetUserGroupsAsync_ReturnsOnlyTheGroupsTheCallerBelongsTo()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetUserGroupsAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            var group = Assert.Single(result.Value!);
            Assert.Equal(GroupId, group.Id);
            Assert.Equal("Dev Team", group.Name);
            Assert.Equal(GroupRole.Member, group.UserRole);
            Assert.Equal(2, group.MemberCount);
        }

        [Fact]
        public async Task GetUserGroupsAsync_IsEmptyForSomebodyInNoGroups()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetUserGroupsAsync(OutsiderId);

            Assert.Empty(result.Value!);
        }

        [Fact]
        public async Task GetGroupDetailsAsync_ReturnsTheHeaderAndTheMemberList()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupDetailsAsync(GroupId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Dev Team", result.Value!.Name);
            Assert.Equal(LeadId, result.Value.OwnerId);
            Assert.Equal("lead", result.Value.OwnerName);
            Assert.Equal(2, result.Value.Members.Count);
            Assert.Contains(result.Value.Members, m => m.UserId == MemberId && m.Role == GroupRole.Member);
        }

        [Fact]
        public async Task GetGroupDetailsAsync_RefusesANonMember()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupDetailsAsync(GroupId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetGroupDetailsAsync_ReturnsNotFoundForAGroupThatDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupDetailsAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, false)]
        [InlineData(GroupRole.Manager, true)]
        [InlineData(GroupRole.Owner, true)]
        public async Task UpdateGroupAsync_RequiresManagerOrAbove(GroupRole role, bool shouldSucceed)
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, role);

            await using var act = NewContext();
            var result = await NewSut(act).UpdateGroupAsync(
                GroupId, new UpdateGroupDto { Name = "Renamed" }, MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);

            await using var assert = NewContext();
            var name = (await assert.Groups.SingleAsync(g => g.Id == GroupId)).Name;
            Assert.Equal(shouldSucceed ? "Renamed" : "Dev Team", name);
        }

        [Fact]
        public async Task UpdateGroupAsync_RefusesANonMember()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateGroupAsync(
                GroupId, new UpdateGroupDto { Name = "Hijacked" }, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        /// <summary>
        /// The password field has three meanings. Null keeps whatever is there, an empty string
        /// removes protection entirely, and anything else becomes the new hash.
        /// </summary>
        [Fact]
        public async Task UpdateGroupAsync_LeavesThePasswordAloneWhenNoneIsSupplied()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);
            await SetGroupPasswordAsync("hashed:original");

            await using var act = NewContext();
            await NewSut(act).UpdateGroupAsync(GroupId, new UpdateGroupDto { Name = "Renamed" }, MemberId);

            await using var assert = NewContext();
            Assert.Equal("hashed:original", (await assert.Groups.SingleAsync(g => g.Id == GroupId)).PasswordHash);
        }

        [Fact]
        public async Task UpdateGroupAsync_RemovesProtectionWhenSentAnEmptyPassword()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);
            await SetGroupPasswordAsync("hashed:original");

            await using var act = NewContext();
            var result = await NewSut(act).UpdateGroupAsync(
                GroupId, new UpdateGroupDto { Password = "" }, MemberId);

            Assert.False(result.Value!.IsPasswordProtected);

            await using var assert = NewContext();
            Assert.Null((await assert.Groups.SingleAsync(g => g.Id == GroupId)).PasswordHash);
        }

        [Fact]
        public async Task UpdateGroupAsync_ReplacesTheHashWhenSentANewPassword()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);
            await SetGroupPasswordAsync("hashed:original");

            await using var act = NewContext();
            await NewSut(act).UpdateGroupAsync(
                GroupId, new UpdateGroupDto { Password = "brandnew" }, MemberId);

            await using var assert = NewContext();
            Assert.Equal("hashed:brandnew", (await assert.Groups.SingleAsync(g => g.Id == GroupId)).PasswordHash);
        }

        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, false)]
        [InlineData(GroupRole.Manager, true)]
        public async Task ChangeUserRoleAsync_RequiresManagerOrAbove(GroupRole callerRole, bool shouldSucceed)
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, callerRole);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, LeadId, new ChangeRoleDto { NewRole = GroupRole.Member }, MemberId);

            // The lead outranks a Manager caller at TeamLead so success here also needs the
            // caller above the target, which the Manager case satisfies.
            Assert.Equal(shouldSucceed, result.IsSuccess);
        }

        [Fact]
        public async Task ChangeUserRoleAsync_RefusesToLetYouChangeYourOwnRole()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, MemberId, new ChangeRoleDto { NewRole = GroupRole.Owner }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        /// <summary>
        /// Ownership never moves through a role change. Without this a Manager could promote
        /// somebody to Owner and take the group over sideways.
        /// </summary>
        [Fact]
        public async Task ChangeUserRoleAsync_NeverHandsOutOwnership()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, LeadId, new ChangeRoleDto { NewRole = GroupRole.Owner }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.Equal((int)GroupRole.TeamLead, (await ReadMembershipAsync(LeadId))!.RoleId);
        }

        [Fact]
        public async Task ChangeUserRoleAsync_RejectsARoleOutsideTheEnum()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, LeadId, new ChangeRoleDto { NewRole = (GroupRole)999 }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        /// <summary>
        /// You can only manage people strictly below you, so a Manager cannot touch another
        /// Manager and certainly not the Owner.
        /// </summary>
        [Fact]
        public async Task ChangeUserRoleAsync_RefusesATargetAtOrAboveTheCallersOwnRank()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);
            await SetRoleAsync(LeadId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, LeadId, new ChangeRoleDto { NewRole = GroupRole.Member }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        /// <summary>
        /// The escalation hole. A Manager granting Manager would let anyone with the power to
        /// manage roles clone their own rank indefinitely, so the new role has to be strictly
        /// below the caller's too.
        /// </summary>
        [Fact]
        public async Task ChangeUserRoleAsync_RefusesToGrantARoleAtOrAboveTheCallersOwn()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, LeadId, new ChangeRoleDto { NewRole = GroupRole.Manager }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
            Assert.Equal((int)GroupRole.TeamLead, (await ReadMembershipAsync(LeadId))!.RoleId);
        }

        [Fact]
        public async Task ChangeUserRoleAsync_PromotesSomebodyBelowTheCaller()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, MemberId, new ChangeRoleDto { NewRole = GroupRole.TeamLead }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(GroupRole.TeamLead, result.Value!.Role);
            Assert.Equal("member", result.Value.UserName);
            Assert.Equal((int)GroupRole.TeamLead, (await ReadMembershipAsync(MemberId))!.RoleId);
        }

        [Fact]
        public async Task ChangeUserRoleAsync_ReturnsNotFoundForSomebodyNotInTheGroup()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeUserRoleAsync(
                GroupId, OutsiderId, new ChangeRoleDto { NewRole = GroupRole.TeamLead }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task TransferOwnershipAsync_MovesTheRoleAndTheGroupsOwnerColumnTogether()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).TransferOwnershipAsync(GroupId, MemberId, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            Assert.Equal((int)GroupRole.Owner, (await ReadMembershipAsync(MemberId))!.RoleId);
            Assert.Equal((int)GroupRole.Manager, (await ReadMembershipAsync(LeadId))!.RoleId);

            await using var assert = NewContext();
            Assert.Equal(MemberId, (await assert.Groups.SingleAsync(g => g.Id == GroupId)).OwnerId);
        }

        [Fact]
        public async Task TransferOwnershipAsync_IsOwnerOnly()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).TransferOwnershipAsync(GroupId, LeadId, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task TransferOwnershipAsync_RefusesTransferringToYourself()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).TransferOwnershipAsync(GroupId, LeadId, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task TransferOwnershipAsync_RefusesSomebodyWhoIsNotAMember()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).TransferOwnershipAsync(GroupId, OutsiderId, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, false)]
        [InlineData(GroupRole.Manager, true)]
        public async Task RemoveUserFromGroupAsync_RequiresManagerOrAbove(GroupRole callerRole, bool shouldSucceed)
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, callerRole);
            await SetRoleAsync(LeadId, GroupRole.Member);

            await using var act = NewContext();
            var result = await NewSut(act).RemoveUserFromGroupAsync(GroupId, LeadId, MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);
            Assert.Equal(shouldSucceed, await ReadMembershipAsync(LeadId) is null);
        }

        [Fact]
        public async Task RemoveUserFromGroupAsync_SendsYouToLeaveWhenYouTargetYourself()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).RemoveUserFromGroupAsync(GroupId, MemberId, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.NotNull(await ReadMembershipAsync(MemberId));
        }

        [Fact]
        public async Task RemoveUserFromGroupAsync_NeverRemovesTheOwner()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);
            await SetRoleAsync(MemberId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).RemoveUserFromGroupAsync(GroupId, LeadId, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.NotNull(await ReadMembershipAsync(LeadId));
        }

        [Fact]
        public async Task RemoveUserFromGroupAsync_RefusesATargetAtOrAboveTheCallersOwnRank()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);
            await SetRoleAsync(LeadId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).RemoveUserFromGroupAsync(GroupId, LeadId, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task RemoveUserFromGroupAsync_SoftDeletesSoTheMembershipCanBeRestored()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);
            await SetRoleAsync(LeadId, GroupRole.Member);

            await using var act = NewContext();
            await NewSut(act).RemoveUserFromGroupAsync(GroupId, LeadId, MemberId);

            var removed = await ReadMembershipAsync(LeadId, includeDeleted: true);
            Assert.True(removed!.IsDeleted);
            Assert.Equal(MemberId, removed.DeletedBy);
            Assert.NotNull(removed.DeletedAt);
        }

        [Fact]
        public async Task LeaveGroupAsync_SoftDeletesTheCallersOwnMembership()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).LeaveGroupAsync(GroupId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Null(await ReadMembershipAsync(MemberId));

            var removed = await ReadMembershipAsync(MemberId, includeDeleted: true);
            Assert.True(removed!.IsDeleted);
            Assert.Equal(MemberId, removed.DeletedBy);
        }

        /// <summary>
        /// The owner leaving would strand the group with nobody able to manage it, so they have
        /// to transfer ownership or delete it instead.
        /// </summary>
        [Fact]
        public async Task LeaveGroupAsync_RefusesTheOwner()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).LeaveGroupAsync(GroupId, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.NotNull(await ReadMembershipAsync(LeadId));
        }

        [Fact]
        public async Task LeaveGroupAsync_ReturnsNotFoundForSomebodyWhoIsNotAMember()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).LeaveGroupAsync(GroupId, OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task DeleteGroupAsync_IsOwnerOnly()
        {
            await SeedAsync();
            await SetRoleAsync(MemberId, GroupRole.Manager);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteGroupAsync(GroupId, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        /// <summary>
        /// The cascade only soft deletes the owner's own membership, so the group has to be empty
        /// first or the other members would be left pointing at a deleted group.
        /// </summary>
        [Fact]
        public async Task DeleteGroupAsync_RefusesWhileAnybodyElseIsStillAMember()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteGroupAsync(GroupId, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task DeleteGroupAsync_SoftDeletesTheGroupTheOwnersMembershipAndEverythingUnderIt()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId));
                await db.SaveChangesAsync();

                db.TaskComments.Add(new TaskComment { TaskId = TaskId, Content = "hi", CreatedBy = LeadId });
                db.TaskAttachments.Add(new TaskAttachment
                {
                    TaskId = TaskId,
                    FileName = "spec.pdf",
                    FilePath = "attachments/spec.pdf",
                    ContentType = "application/pdf",
                    FileSize = 10,
                    CreatedBy = LeadId
                });
                db.Notifications.Add(new Notification
                {
                    UserId = LeadId,
                    Type = NotificationType.TaskAssigned,
                    Title = "Assigned",
                    Message = "message",
                    RelatedEntityId = TaskId,
                    RelatedEntityType = "Task"
                });
                await db.SaveChangesAsync();
            }

            await using (var leave = NewContext())
                await NewSut(leave).LeaveGroupAsync(GroupId, MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteGroupAsync(GroupId, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();

            Assert.Empty(await assert.Groups.Where(g => g.Id == GroupId).ToListAsync());
            Assert.Empty(await assert.Tasks.ToListAsync());
            Assert.Empty(await assert.TaskComments.ToListAsync());
            Assert.Empty(await assert.TaskAttachments.ToListAsync());
            Assert.Empty(await assert.Notifications.ToListAsync());

            var group = await assert.Groups.IgnoreQueryFilters().SingleAsync(g => g.Id == GroupId);
            Assert.True(group.IsDeleted);
            Assert.Equal(LeadId, group.DeletedBy);

            var comment = await assert.TaskComments.IgnoreQueryFilters().SingleAsync();
            Assert.True(comment.IsDeleted);
            Assert.NotNull(comment.UpdatedAt);

            var ownerMembership = await assert.GroupMembers
                .IgnoreQueryFilters()
                .SingleAsync(gm => gm.GroupId == GroupId && gm.UserId == LeadId);
            Assert.True(ownerMembership.IsDeleted);
        }

        /// <summary>
        /// The cascade is scoped to this group. A second group's tasks and comments have to
        /// survive, which is the only thing separating a delete from a data loss incident.
        /// </summary>
        [Fact]
        public async Task DeleteGroupAsync_LeavesAnotherGroupsDataCompletelyAlone()
        {
            await SeedAsync();
            await SetRoleAsync(LeadId, GroupRole.Owner);

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId, title: "Ours"));
                db.Tasks.Add(TestData.Task(OtherGroupId, OtherLeadId, title: "Theirs"));
                await db.SaveChangesAsync();
            }

            await using (var leave = NewContext())
                await NewSut(leave).LeaveGroupAsync(GroupId, MemberId);

            await using var act = NewContext();
            await NewSut(act).DeleteGroupAsync(GroupId, LeadId);

            await using var assert = NewContext();
            var survivor = Assert.Single(await assert.Tasks.ToListAsync());
            Assert.Equal("Theirs", survivor.Title);
            Assert.Single(await assert.Groups.Where(g => g.Id == OtherGroupId).ToListAsync());
        }
    }
}
