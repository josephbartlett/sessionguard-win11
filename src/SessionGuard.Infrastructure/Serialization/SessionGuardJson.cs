using System.Text.Json;
using System.Text.Json.Serialization;

namespace SessionGuard.Infrastructure.Serialization;

public static class SessionGuardJson
{
    public static readonly JsonSerializerOptions Default = CreateDefault();

    public static readonly JsonSerializerOptions Indented = CreateIndented();

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateIndented()
    {
        var options = CreateDefault();
        options.WriteIndented = true;
        return options;
    }
}
