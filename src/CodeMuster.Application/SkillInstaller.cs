using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Writes the bundled SKILL.md where a coding harness discovers Agent Skills, in one repo or in the user's home.</summary>
public sealed class SkillInstaller(IFileSystem fileSystem)
{
    /// <summary>The harnesses <c>skill install --for</c> accepts.</summary>
    public static readonly string[] Harnesses = ["claude", "codex", "gemini", "opencode"];

    private const string SkillName = "codemuster";

    /// <summary>Absolute path of the harness's SKILL.md under the repo root, or under the home directory when <paramref name="global"/>.</summary>
    public static string PathFor(string harness, bool global, string repoRoot, string homeDirectory)
    {
        var root = global ? homeDirectory : repoRoot;
        var skillsFolder = harness switch
        {
            "claude" => Path.Combine(root, ".claude", "skills"),
            "codex" => Path.Combine(root, ".codex", "skills"),
            "gemini" => Path.Combine(root, ".gemini", "skills"),
            "opencode" => global ? Path.Combine(root, ".config", "opencode", "skills") : Path.Combine(root, ".opencode", "skills"),
            _ => throw new ArgumentException($"unknown harness '{harness}'; expected one of {string.Join(", ", Harnesses)}", nameof(harness)),
        };
        return Path.Combine(skillsFolder, SkillName, "SKILL.md");
    }

    /// <summary>Writes <paramref name="skillText"/> to the harness's path unless an identical file is already there, and returns that path.</summary>
    public async Task<string> InstallAsync(string harness, bool global, string repoRoot, string homeDirectory, string skillText, CancellationToken cancellationToken)
    {
        var path = PathFor(harness, global, repoRoot, homeDirectory);
        if (!fileSystem.FileExists(path) || await fileSystem.ReadAllTextAsync(path, cancellationToken) != skillText)
        {
            await fileSystem.WriteAllTextAsync(path, skillText, cancellationToken);
        }

        return path;
    }
}
