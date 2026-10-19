using System.Text.Json;
using FluentAssertions;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Events;

namespace Plantitask.Tests.Events
{
    /// <summary>
    /// Pins the payload rules from decision 5. Pure, so it never touches the database.
    /// </summary>
    public class EventSerializerTests
    {
        [EventName("test.something-moved.v1")]
        public record SomethingMoved : IEvent
        {
            public required Guid Id { get; init; }
            public required EventSource Source { get; init; }
            public string? Note { get; init; }
        }

        [Fact]
        public void SerializeThenDeserialize_GivesTheSameEventBack()
        {
            var original = new SomethingMoved { Id = Guid.NewGuid(), Source = EventSource.Webhook, Note = "moved" };

            var payload = EventSerializer.Serialize(original);
            var back = EventSerializer.Deserialize(payload, typeof(SomethingMoved));

            back.Should().Be(original, "records compare by value, so every field must survive the trip");
        }

        [Fact]
        public void Serialize_WritesCamelCaseNamesAndEnumsAsText()
        {
            var payload = EventSerializer.Serialize(new SomethingMoved { Id = Guid.Empty, Source = EventSource.Webhook });

            payload.Should().Contain("\"id\":").And.Contain("\"source\":\"Webhook\"");
        }

        [Fact]
        public void Deserialize_RefusesAnEnumStoredAsANumber()
        {
            var payload = "{\"id\":\"00000000-0000-0000-0000-000000000000\",\"source\":2}";

            var read = () => EventSerializer.Deserialize(payload, typeof(SomethingMoved));

            read.Should().Throw<JsonException>();
        }

        [Fact]
        public void Deserialize_RefusesAPayloadMissingARequiredField()
        {
            var read = () => EventSerializer.Deserialize("{\"source\":\"Job\"}", typeof(SomethingMoved));

            read.Should().Throw<JsonException>();
        }
    }
}
