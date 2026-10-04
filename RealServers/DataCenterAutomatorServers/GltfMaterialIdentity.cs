using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DataCenterAutomatorServers;

internal static class GltfMaterialIdentity
{
    // No color approximation: retain the complete recipe except its display
    // label, and retain the old importer's label-derived fallback color as well.
    public static string Build(JsonElement material)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, material, ignoreName: true);
        return Encoding.UTF8.GetString(stream.ToArray()) + "|" + FallbackColor(material);
    }

    private static string FallbackColor(JsonElement material)
    {
        if (material.TryGetProperty("pbrMetallicRoughness", out var pbr)
            && pbr.TryGetProperty("baseColorFactor", out var factor)
            && factor.ValueKind == JsonValueKind.Array && factor.GetArrayLength() == 4) return "explicit";
        string name = material.TryGetProperty("name", out var label) ? label.GetString() ?? "" : "";
        if (name.Contains("GreyTexture", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Metal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("550", StringComparison.OrdinalIgnoreCase)) return "metal";
        return name.Contains("Up", StringComparison.OrdinalIgnoreCase) ? "up" : "white";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value, bool ignoreName = false)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (ignoreName && property.Name == "name") continue;
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
