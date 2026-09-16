using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreateOS.Sandbox.Internal;

internal sealed class EnumMemberJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(EnumMemberJsonConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class EnumMemberJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly Dictionary<string, T> FromWire = Enum.GetValues<T>().ToDictionary(ToWire, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<T, string> ToWireMap = Enum.GetValues<T>().ToDictionary(x => x, ToWire);

    private static string ToWire(T value)
    {
        var member = typeof(T).GetMember(value.ToString())[0];
        return member.GetCustomAttribute<EnumMemberAttribute>()?.Value
            ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is not null && FromWire.TryGetValue(value, out var result)
            ? result
            : throw new JsonException($"Unknown {typeof(T).Name} value '{value}'.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToWireMap[value]);
}
