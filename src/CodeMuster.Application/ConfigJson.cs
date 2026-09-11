using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Reads and writes <see cref="Config"/> with the shared Domain JSON options.</summary>
public static class ConfigJson
{
    /// <summary>Parses config JSON, throwing <see cref="JsonException"/> when a required field is missing.</summary>
    public static Config Parse(string json) =>
        JsonSerializer.Deserialize<Config>(json, DomainJson.Options) ?? throw new JsonException("config is null");

    /// <summary>Writes config as indented JSON.</summary>
    public static string Serialize(Config config) => JsonSerializer.Serialize(config, DomainJson.Options);
}
