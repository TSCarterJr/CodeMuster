using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Loads <c>.codemuster/config.json</c> from a repo root.</summary>
public sealed class ConfigLoader(IFileSystem fileSystem)
{
    /// <summary>Absolute path of the config file for a repo root.</summary>
    public static string PathFor(string repoRoot) => Path.Combine(repoRoot, ".codemuster", "config.json");

    /// <summary>The repo's config, or <see cref="Config.Default"/> until <c>init</c> has written one.</summary>
    public async Task<Config> LoadAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var path = PathFor(repoRoot);
        if (!fileSystem.FileExists(path))
        {
            return Config.Default;
        }

        return ConfigJson.Parse(await fileSystem.ReadAllTextAsync(path, cancellationToken));
    }
}
