using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Loads <c>.codemuster/config.json</c> from a repo root; this is the first-run gate every verb but <c>init</c> passes through (D24).</summary>
public sealed class ConfigLoader(IFileSystem fileSystem)
{
    /// <summary>Absolute path of the config file for a repo root.</summary>
    public static string PathFor(string repoRoot) => Path.Combine(repoRoot, ".codemuster", "config.json");

    /// <summary>The repo's config; throws <see cref="NotInitializedException"/> until <c>init</c> has written one.</summary>
    public async Task<Config> LoadAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var path = PathFor(repoRoot);
        if (!fileSystem.FileExists(path))
        {
            throw new NotInitializedException();
        }

        return ConfigJson.Parse(await fileSystem.ReadAllTextAsync(path, cancellationToken));
    }
}
