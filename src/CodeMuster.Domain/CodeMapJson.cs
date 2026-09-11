using System.Text.Json;

namespace CodeMuster.Domain;

/// <summary>Reads and writes a <see cref="CodeMap"/> as JSON using the shared Domain options.</summary>
public static class CodeMapJson
{
    /// <summary>Parses a code map from JSON, throwing <see cref="JsonException"/> on a missing field or a null document.</summary>
    public static CodeMap Parse(string json) =>
        JsonSerializer.Deserialize<CodeMap>(json, DomainJson.Options) ?? throw new JsonException("code map is null");

    /// <summary>Writes a code map as indented JSON.</summary>
    public static string Serialize(CodeMap map) => JsonSerializer.Serialize(map, DomainJson.Options);
}
