using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

/// <summary>What one person last chose for a repository: the agent settings (D84) and the steps of <c>auto</c> (D85).</summary>
public sealed record RememberedChoices(string? Agent, string? Model, string? Effort, int? Jobs, IReadOnlyList<string>? Steps);

/// <summary>Keeps <see cref="RememberedChoices"/> per repository under the user's state directory, outside the ledger and the committed config (D86).</summary>
public sealed class ChoiceStore(string stateDirectory)
{
    public RememberedChoices? Load(string repoRoot)
    {
        try
        {
            return JsonSerializer.Deserialize<RememberedChoices>(File.ReadAllText(PathFor(repoRoot)), DomainJson.Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(string repoRoot, RememberedChoices choices)
    {
        var path = PathFor(repoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(choices, DomainJson.Options));
    }

    private string PathFor(string repoRoot)
    {
        var root = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) root = root.ToLowerInvariant();
        return Path.Combine(stateDirectory, "choices", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root))) + ".json");
    }
}
