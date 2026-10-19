using System.Text.Json;
using System.Text.Json.Serialization;
using Plantitask.Core.Events;


namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// Only place the event payloads become JSON and come back. Every
    /// write and every read uses these options so a row written before a deploy reads the same
    /// after one. Set rules are camelCase, enums as text only, and a v1 record may only gain an optional field.
    /// </summary>
    public static class EventSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
        };

        // By the runtime Type, going through the IEvent type it would write {} since the interface has no properties
        public static string Serialize(IEvent e) =>
            JsonSerializer.Serialize(e, e.GetType(), Options);

        public static IEvent Deserialize(string payload, Type eventType) =>
            JsonSerializer.Deserialize(payload, eventType, Options) as IEvent
                ?? throw new JsonException($"The payload did not read back as {eventType.Name}");
    }
}
