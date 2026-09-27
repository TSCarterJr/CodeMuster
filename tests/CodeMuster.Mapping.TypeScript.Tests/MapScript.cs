using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

internal static class MapScript
{
    public static async Task<CodeMap> RunAsync(string repoRoot, IReadOnlyList<string> tsconfigs, IReadOnlyList<string> paths) =>
        CodeMapJson.Parse(await RunRawAsync(repoRoot, tsconfigs, paths));

    /// <summary>The script's stdout as it wrote it; with <paramref name="debug"/> it also reports parsed_files, the count of source files it parsed.</summary>
    public static async Task<string> RunRawAsync(string repoRoot, IReadOnlyList<string> tsconfigs, IReadOnlyList<string> paths, bool debug = false)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var startInfo = new ProcessStartInfo(TestPaths.Node())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        startInfo.ArgumentList.Add(Path.Combine(TestPaths.RepoRoot, "src", "CodeMuster.Mapping.TypeScript", "map.js"));

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("node did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(debug ? new { repo_root = repoRoot, tsconfigs, paths, debug } : (object)new { repo_root = repoRoot, tsconfigs, paths }));
        process.StandardInput.Close();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? await stdout
            : throw new InvalidOperationException($"map.js exited with code {process.ExitCode}: {await stderr}");
    }
}
