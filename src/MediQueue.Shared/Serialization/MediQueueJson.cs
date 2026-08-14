using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediQueue.Shared.Serialization;

/// <summary>
/// One JSON configuration for the whole system. Enums travel as names rather
/// than numbers, so a payload stays readable in a browser's network tab and
/// reordering an enum cannot silently change what a stored value means.
/// Every consumer — API, SignalR, Blazor client, tests — uses these options,
/// so the wire format is defined in exactly one place.
/// </summary>
public static class MediQueueJson
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options) =>
        options.Converters.Add(new JsonStringEnumConverter());
}
