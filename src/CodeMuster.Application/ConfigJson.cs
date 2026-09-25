using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Reads and writes <see cref="Config"/> with the shared Domain JSON options.</summary>
public static class ConfigJson
{
    /// <summary>Parses config JSON, throwing <see cref="JsonException"/> when a required field is missing or automation is invalid.</summary>
    public static Config Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("automation", out var automation)
            && (automation.ValueKind != JsonValueKind.String || automation.GetString() is not ("off" or "update" or "review" or "review_and_fix")))
        {
            throw new JsonException("automation must be one of: off, update, review, review_and_fix");
        }

        var config = document.RootElement.Deserialize<Config>(DomainJson.Options) ?? throw new JsonException("config is null");
        if (config.UserExperience is null || config.UserExperience.Include is null || config.UserExperience.Exclude is null)
            throw new JsonException("user_experience must be an object with non-null include/exclude arrays");
        if (config.UserExperience.Include.Concat(config.UserExperience.Exclude).Any(string.IsNullOrWhiteSpace))
            throw new JsonException("user_experience include/exclude must contain nonempty path globs");
        if (config.UserExperience.BaseUrl is { } address
            && (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0))
            throw new JsonException("user_experience.base_url must be an absolute HTTP(S) application URL without credentials");
        if (config.Exclude.Any(string.IsNullOrWhiteSpace))
            throw new JsonException("exclude must contain nonempty path globs");
        if (config.Lenses.Any(lens => lens is null))
            throw new JsonException("lenses must not contain null entries");
        if (config.Lenses.Any(lens => lens.Globs is null || lens.Globs.Any(string.IsNullOrWhiteSpace)))
            throw new JsonException("lens globs must be an array of nonempty path globs");
        if (config.Lenses.Any(lens => string.Equals(lens.Id?.Trim(), DeadCodeReview.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(lens.Id?.Trim(), UxReview.Id, StringComparison.OrdinalIgnoreCase)))
            throw new JsonException("built-in reviews reserve lens ids dead_code and user_experience; rename the conflicting custom lens");
        return config;
    }

    /// <summary>Writes config as indented JSON.</summary>
    public static string Serialize(Config config) => JsonSerializer.Serialize(config, DomainJson.Options);
}
