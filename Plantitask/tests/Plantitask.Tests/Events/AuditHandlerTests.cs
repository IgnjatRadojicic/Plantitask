using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Constants;
using Plantitask.Core.Entities;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;
using Plantitask.Infrastructure.Events;
using Plantitask.Infrastructure.Events.Handlers.Audit;
using Plantitask.Tests.Helpers;

namespace Plantitask.Tests.Events
{
    /// <summary>
    /// Sends every event through the real drainer against real Postgres, so one case proves the
    /// record survives serialization, finds its handler, and its row passes the table's constraints.
    /// </summary>
    public class AuditHandlerTests : DbTestBase
    {
        private const string Ip = "203.0.113.7";
        private const string Agent = "test-agent";

        private static readonly DateTime OccurredAt = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        private static readonly EventActor Lead = new(TestIds.LeadId, "lead", "lead@example.com");

        private static readonly Guid TaskId = TestIds.TaskId;
        private static readonly Guid Group = TestIds.GroupId;
        private static readonly Guid Member = TestIds.MemberId;
        private static readonly Guid User = TestIds.LeadId;
        private static readonly Guid Comment = new("cccccccc-0000-0000-0000-000000000001");
        private static readonly Guid Attachment = new("bbbbbbbb-0000-0000-0000-000000000001");

        public AuditHandlerTests(PostgresFixture fixture) : base(fixture)
        {
        }

        public record ExpectedRow(
            string EntityType,
            Guid EntityId,
            string Action,
            Guid? GroupId = null,
            string? Property = null,
            string? Old = null,
            string? New = null,
            string? Ip = null,
            string? Agent = null);

        public static TheoryData<IEvent, ExpectedRow> SignedInEvents => new()
        {
            {
                new TaskCreated { TaskId = TaskId, GroupId = Group, Title = "T", StatusId = 1, AssigneeId = null, GroupName = "G" },
                new(EntityTypes.TaskItem, TaskId, AuditActions.Created, Group)
            },
            {
                new TaskUpdated { TaskId = TaskId, GroupId = Group, Title = "T", AssigneeId = null },
                new(EntityTypes.TaskItem, TaskId, AuditActions.Updated, Group)
            },
            {
                new TaskStatusChanged
                {
                    TaskId = TaskId, GroupId = Group, Title = "T", AssigneeId = null, CreatedById = User,
                    OldStatusId = 1, OldStatusName = "Not Started", NewStatusId = 2, NewStatusName = "In Progress"
                },
                new(EntityTypes.TaskItem, TaskId, AuditActions.StatusChanged, Group, "StatusId", "Not Started", "In Progress")
            },
            {
                new TaskPriorityChanged
                {
                    TaskId = TaskId, GroupId = Group, Title = "T", AssigneeId = null,
                    OldPriorityId = 2, OldPriorityName = "Medium", NewPriorityId = 3, NewPriorityName = "High"
                },
                new(EntityTypes.TaskItem, TaskId, AuditActions.PriorityChanged, Group, "Priority", "Medium", "High")
            },
            {
                new TaskAssigned
                {
                    TaskId = TaskId, GroupId = Group, Title = "T", AssigneeId = Member,
                    AssigneeName = "member", GroupName = "Dev Team"
                },
                new(EntityTypes.TaskItem, TaskId, AuditActions.Assigned, Group, "AssignedTo", null, "member")
            },
            {
                new TaskUnassigned
                {
                    TaskId = TaskId, GroupId = Group, Title = "T", PreviousAssigneeId = Member,
                    PreviousAssigneeName = "member"
                },
                new(EntityTypes.TaskItem, TaskId, AuditActions.Unassigned, Group, "AssignedTo", "member")
            },
            {
                new TaskDeleted { TaskId = TaskId, GroupId = Group, Title = "T", StatusId = 1 },
                new(EntityTypes.TaskItem, TaskId, AuditActions.Deleted, Group)
            },
            {
                new TaskMoved
                {
                    TaskId = TaskId, GroupId = Group,
                    OldStatusId = 2, OldStatusName = "In Progress", NewStatusId = 3, NewStatusName = "Under Review",
                    NewDisplayOrder = 0
                },
                new(EntityTypes.TaskItem, TaskId, AuditActions.Moved, Group, "StatusId", "In Progress", "Under Review")
            },

            {
                new CommentAdded
                {
                    CommentId = Comment, TaskId = TaskId, GroupId = Group, Content = "hello", TaskTitle = "T",
                    TaskAssigneeId = null, TaskCreatedById = User
                },
                new(EntityTypes.TaskComment, Comment, AuditActions.Created, Group, "Content", null, "hello")
            },
            {
                new CommentEdited { CommentId = Comment, TaskId = TaskId, GroupId = Group, Content = "edited" },
                new(EntityTypes.TaskComment, Comment, AuditActions.Updated, Group, "Content", null, "edited")
            },
            {
                new CommentDeleted { CommentId = Comment, TaskId = TaskId, GroupId = Group },
                new(EntityTypes.TaskComment, Comment, AuditActions.Deleted, Group)
            },

            {
                new AttachmentUploaded { AttachmentId = Attachment, TaskId = TaskId, GroupId = Group, FileName = "a.png" },
                new(EntityTypes.TaskAttachment, Attachment, AuditActions.Uploaded, Group, "FileName", null, "a.png")
            },
            {
                new AttachmentDeleted { AttachmentId = Attachment, TaskId = TaskId, GroupId = Group, FileName = "a.png" },
                new(EntityTypes.TaskAttachment, Attachment, AuditActions.Deleted, Group, "FileName", "a.png")
            },

            {
                new GroupCreated { GroupId = Group, Name = "Dev Team" },
                new(EntityTypes.Group, Group, AuditActions.Created, Group)
            },
            {
                new GroupUpdated { GroupId = Group },
                new(EntityTypes.Group, Group, AuditActions.Updated, Group)
            },
            {
                new GroupMemberJoined { GroupId = Group, UserId = Member },
                new(EntityTypes.GroupMember, Member, AuditActions.JoinedGroup, Group)
            },
            {
                new GroupMemberRoleChanged { GroupId = Group, UserId = Member, OldRoleId = 25, NewRoleId = 50 },
                new(EntityTypes.GroupMember, Member, AuditActions.RoleChanged, Group, "Role", "Member", "TeamLead")
            },
            {
                new GroupMemberRemoved { GroupId = Group, UserId = Member },
                new(EntityTypes.GroupMember, Member, AuditActions.RemovedFromGroup, Group)
            },
            {
                new GroupMemberLeft { GroupId = Group, UserId = Member },
                new(EntityTypes.GroupMember, Member, AuditActions.LeftGroup, Group)
            },

            {
                new UserLoggedOut { UserId = User, IpAddress = Ip, UserAgent = Agent },
                new(EntityTypes.User, User, AuditActions.Logout, Ip: Ip, Agent: Agent)
            },
            {
                new PasswordChanged { UserId = User, IpAddress = Ip, UserAgent = Agent },
                new(EntityTypes.User, User, AuditActions.PasswordChanged, Ip: Ip, Agent: Agent)
            },
            {
                new UsernameChanged { UserId = User, OldUserName = "old", NewUserName = "new", IpAddress = Ip, UserAgent = Agent },
                new(EntityTypes.User, User, AuditActions.UsernameChanged, null, "UserName", "old", "new", Ip, Agent)
            },

            {
                new PremiumActivated { UserId = User, Kind = PremiumKind.Recurring },
                new(EntityTypes.User, User, AuditActions.PremiumActivated, null, "SubscriptionType", null, "recurring")
            },
            {
                new PremiumActivated { UserId = User, Kind = PremiumKind.OneTime },
                new(EntityTypes.User, User, AuditActions.PremiumActivated, null, "SubscriptionType", null, "onetime")
            },
            {
                new PremiumCancelled { UserId = User },
                new(EntityTypes.User, User, AuditActions.PremiumCancelled, null, "IsPremium", "true", "false")
            },
        };

        public static TheoryData<IEvent, ExpectedRow> EventsBeforeAToken => new()
        {
            {
                new UserRegistered { UserId = User, UserName = "lead", Email = "lead@example.com", IpAddress = Ip, UserAgent = Agent },
                new(EntityTypes.User, User, AuditActions.Registered, Ip: Ip, Agent: Agent)
            },
            {
                new UserLoggedIn
                {
                    UserId = User, UserName = "lead", Email = "lead@example.com", Method = LoginMethod.Password,
                    IpAddress = Ip, UserAgent = Agent
                },
                new(EntityTypes.User, User, AuditActions.Login, null, "Method", null, "password", Ip, Agent)
            },
            {
                new UserLoggedIn
                {
                    UserId = User, UserName = "lead", Email = "lead@example.com", Method = LoginMethod.Google,
                    IpAddress = Ip, UserAgent = Agent
                },
                new(EntityTypes.User, User, AuditActions.Login, null, "Method", null, "google", Ip, Agent)
            },
            {
                new PasswordReset { UserId = User, UserName = "lead", Email = "lead@example.com", IpAddress = Ip, UserAgent = Agent },
                new(EntityTypes.User, User, AuditActions.PasswordReset, Ip: Ip, Agent: Agent)
            },
        };

        private ServiceProvider Services()
        {
            var services = new ServiceCollection()
                .AddScoped(_ => new ApplicationDbContext(ContextOptions))
                .AddSingleton(new EventTypeRegistry(typeof(IEvent).Assembly))
                .AddSingleton(Options.Create(new OutboxSettings { MaxMessagesPerRun = 100 }))
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddScoped<OutboxDrainJob>()
                .AddEventHandler<TaskAuditHandler>()
                .AddEventHandler<CommentAuditHandler>()
                .AddEventHandler<AttachmentAuditHandler>()
                .AddEventHandler<GroupAuditHandler>()
                .AddEventHandler<AccountAuditHandler>()
                .AddEventHandler<PremiumAuditHandler>();

            return services.BuildServiceProvider(validateScopes: true);
        }

        private async Task<AuditLog> DrainAsync(IEvent e, EventActor? actor, EventSource source = EventSource.Request)
        {
            await using (var arrange = NewContext())
            {
                await arrange.SeedWorldAsync();
                arrange.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = EventNames.For(e.GetType()),
                    Payload = EventSerializer.Serialize(e),
                    OccurredAt = OccurredAt,
                    Source = source,
                    ActorId = actor?.Id,
                    ActorName = actor?.UserName,
                    ActorEmail = actor?.Email,
                });
                await arrange.SaveChangesAsync();
            }

            await using (var root = Services())
            await using (var job = root.CreateAsyncScope())
            {
                await job.ServiceProvider.GetRequiredService<OutboxDrainJob>().RunAsync(CancellationToken.None);
            }

            await using var db = NewContext();
            var message = await db.OutboxMessages.SingleAsync();
            message.LastError.Should().BeNull();
            message.ProcessedAt.Should().NotBeNull();

            return await db.AuditLogs.SingleAsync();
        }

        private static ExpectedRow Shape(AuditLog row) => new(
            row.EntityType, row.EntityId, row.Action, row.GroupId,
            row.PropertyName, row.OldValue, row.NewValue, row.IpAddress, row.UserAgent);

        [Fact]
        public void EveryEvent_HasExactlyOneAuditHandler()
        {
            Type[] handlers =
            [
                typeof(TaskAuditHandler), typeof(CommentAuditHandler), typeof(AttachmentAuditHandler),
                typeof(GroupAuditHandler), typeof(AccountAuditHandler), typeof(PremiumAuditHandler)
            ];

            var events = new EventTypeRegistry(typeof(IEvent).Assembly).EventTypes.ToList();

            events.Should().NotBeEmpty();
            events.Where(e => handlers.Count(h =>
                    typeof(IDurableEventHandler<>).MakeGenericType(e).IsAssignableFrom(h)) != 1)
                .Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(SignedInEvents))]
        public async Task Run_SignedInEvent_WritesItsRowUnderTheEnvelopesActor(IEvent e, ExpectedRow expected)
        {
            var row = await DrainAsync(e, Lead);

            Shape(row).Should().Be(expected);
            row.UserId.Should().Be(Lead.Id);
            row.UserName.Should().Be(Lead.UserName);
            row.UserEmail.Should().Be(Lead.Email);
            row.Source.Should().Be(EventSource.Request);
            row.OccurredAt.Should().Be(OccurredAt);
        }

        [Theory]
        [MemberData(nameof(EventsBeforeAToken))]
        public async Task Run_EventBeforeAToken_NamesThePersonFromThePayload(IEvent e, ExpectedRow expected)
        {
            var row = await DrainAsync(e, actor: null);

            Shape(row).Should().Be(expected);
            row.UserId.Should().Be(Lead.Id);
            row.UserName.Should().Be(Lead.UserName);
            row.UserEmail.Should().Be(Lead.Email);
        }

        [Fact]
        public async Task Run_PremiumFromAWebhook_HasNoActorAndSaysWebhook()
        {
            var row = await DrainAsync(
                new PremiumCancelled { UserId = User }, actor: null, EventSource.Webhook);

            row.EntityId.Should().Be(User);
            row.UserId.Should().BeNull();
            row.UserName.Should().BeNull();
            row.UserEmail.Should().BeNull();
            row.Source.Should().Be(EventSource.Webhook);
        }

        [Fact]
        public async Task Run_CommentLongerThanTheColumn_IsCutAndNotParked()
        {
            var content = new string('x', 2000);

            var row = await DrainAsync(
                new CommentEdited { CommentId = Comment, TaskId = TaskId, GroupId = Group, Content = content }, Lead);

            row.NewValue.Should().Be(content[..1000]);
        }

        [Fact]
        public async Task Run_UserAgentLongerThanTheColumn_IsCutAndNotParked()
        {
            var agent = new string('x', 900);

            var row = await DrainAsync(new UserLoggedOut { UserId = User, IpAddress = Ip, UserAgent = agent }, Lead);

            row.UserAgent.Should().Be(agent[..500]);
        }
    }
}
