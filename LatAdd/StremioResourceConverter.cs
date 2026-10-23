using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gelato.LatAdd;

/// <summary>
/// Reads a manifest's <c>resources</c> entry in both shapes the Stremio protocol allows: a bare
/// string (<c>"catalog"</c>) or an object with <c>name</c>, <c>types</c> and <c>idPrefixes</c>.
/// Gelato only understood the object, so a manifest that declares a resource as a string failed
/// to deserialize as a whole and no catalog was imported (fork §2).
/// </summary>
/// <remarks>
/// A bare string names the resource only: its <c>Types</c> and <c>IdPrefixes</c> are the
/// manifest root's own <c>types</c>/<c>idPrefixes</c>. The converter cannot see the manifest
/// while it reads, so it leaves them empty and
/// <see cref="InheritManifestDefaults(StremioManifest)"/> fills them in once the manifest is
/// deserialized.
/// </remarks>
public sealed class StremioResourceConverter : JsonConverter<StremioResource>
{
    public override StremioResource Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new StremioResource { Name = reader.GetString() ?? "" };

            case JsonTokenType.StartObject:
            {
                var resource = new StremioResource();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        reader.Skip();
                        continue;
                    }

                    var property = reader.GetString();
                    reader.Read();
                    switch (property?.ToLowerInvariant())
                    {
                        case "name":
                            resource.Name = ReadStringOrSkip(ref reader);
                            break;
                        case "types":
                            resource.Types = ReadStringList(ref reader);
                            break;
                        case "idprefixes":
                            resource.IdPrefixes = ReadStringList(ref reader);
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                return resource;
            }

            default:
                reader.Skip();
                return new StremioResource();
        }
    }

    private static string ReadStringOrSkip(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "";

    private static List<string> ReadStringList(ref Utf8JsonReader reader)
    {
        var values = new List<string>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return values;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } value)
                values.Add(value);
        }

        return values;
    }

    public override void Write(Utf8JsonWriter writer, StremioResource? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteStartArray("types");
        foreach (var type in value.Types)
            writer.WriteStringValue(type);
        writer.WriteEndArray();
        writer.WriteStartArray("idPrefixes");
        foreach (var prefix in value.IdPrefixes)
            writer.WriteStringValue(prefix);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Completes the resources a manifest declared as a bare string with the manifest root's
    /// <c>types</c> and <c>idPrefixes</c>, which is the Stremio semantics for them.
    /// </summary>
    public static void InheritManifestDefaults(StremioManifest manifest)
    {
        foreach (var resource in manifest.Resources)
        {
            if (resource.Types.Count == 0)
                resource.Types = new List<string>(manifest.Types);
            if (resource.IdPrefixes.Count == 0 && manifest.IdPrefixes.Count > 0)
                resource.IdPrefixes = new List<string>(manifest.IdPrefixes);
        }
    }
}
