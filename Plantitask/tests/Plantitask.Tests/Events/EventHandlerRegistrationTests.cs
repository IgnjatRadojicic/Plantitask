using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Events;

namespace Plantitask.Tests.Events
{
    public class EventHandlerRegistrationTests
    {
        [EventName("test.first.v1")]
        public record First : IEvent;

        [EventName("test.second.v1")]
        public record Second : IEvent;

        private sealed class DurableForBoth : IDurableEventHandler<First>,
            IDurableEventHandler<Second>
        {
            public string Name => "test-both";
            public Task HandleAsync(First e, EventContext cotext, CancellationToken ct) =>
                Task.CompletedTask;
            public Task HandleAsync(Second e, EventContext cotext, CancellationToken ct) =>
                Task.CompletedTask;
        }
        private sealed class SyncForFirst : IEventHandler<First>
        {
            public Task HandleAsync(First e, EventContext context, CancellationToken ct) => Task.CompletedTask;
        }
        private sealed class NotAHandler { }

        private static IServiceScope ScopeWith(Action<IServiceCollection> register)
        {
            var services = new ServiceCollection();
            register(services);
            return services.BuildServiceProvider(validateScopes: true).CreateScope();
        }

        [Fact]
        public void AddEventHandler_DurableHandler_IsFoundUnderEveryEventItHandles()
        {
            using var scope = ScopeWith(s => s.AddEventHandler<DurableForBoth>());

            scope.ServiceProvider.GetServices<IDurableEventHandler<First>>()
                .Should().ContainSingle().Which.Should().BeOfType<DurableForBoth>();
            scope.ServiceProvider.GetServices<IDurableEventHandler<Second>>()
                .Should().ContainSingle().Which.Should().BeOfType<DurableForBoth>();
        }

        [Fact]
        public void AddEventHandler_DurableHandler_IsKeptOutOfTheSyncPath()
        {
            using var scope = ScopeWith(s => s.AddEventHandler<DurableForBoth>());

            scope.ServiceProvider.GetServices<IEventHandler<First>>()
                .Should().BeEmpty("the request would build it only to skip it");
        }

        [Fact]
        public void AddEventHandler_SyncHandler_IsOnlyFoundOnTheSyncPath()
        {
            using var scope = ScopeWith(s => s.AddEventHandler<SyncForFirst>());

            scope.ServiceProvider.GetServices<IEventHandler<First>>()
                .Should().ContainSingle().Which.Should().BeOfType<SyncForFirst>();
            scope.ServiceProvider.GetServices<IDurableEventHandler<First>>().Should().BeEmpty();
        }
        [Fact]
        public void AddEventHandler_ClassThatHandlesNothing_Throws()
        {
            var register = () => new ServiceCollection().AddEventHandler<NotAHandler>();

            register.Should().Throw<InvalidOperationException>();
        }
    }
}
