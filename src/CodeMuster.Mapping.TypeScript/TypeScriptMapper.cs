using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript;

public sealed class TypeScriptMapper : ICodeMapper
{
    private const string ProgressPrefix = "progress: ";

    private readonly Func<string> _node;

    // Resolved only when a tsconfig.json needs mapping, so a machine without Node.js can still map C#.
    public TypeScriptMapper(Func<string> node)
    {
        _node = node;
    }

    public string Language => Languages.TypeScript;

    public async Task<CodeMap> MapAsync(string repoRoot, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        List<string> normalized = [.. paths.Select(RepoPath.Normalize)];
        List<string> tsconfigs = [.. normalized.Where(path => Path.GetFileName(path) == "tsconfig.json").Order(StringComparer.Ordinal)];
        if (tsconfigs.Count == 0)
        {
            return new CodeMap([], [], [], new ResolutionStats(0, 0, []), []);
        }

        var folder = Directory.CreateTempSubdirectory("codemuster-ts-");
        try
        {
            var script = Path.Combine(folder.FullName, "map.js");
            await ExtractScriptAsync(script, cancellationToken).ConfigureAwait(false);
            var request = JsonSerializer.Serialize(new MapRequest(Path.GetFullPath(repoRoot), tsconfigs, normalized), DomainJson.Options);
            return CodeMapJson.Parse(await RunNodeAsync(script, request, progress, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task ExtractScriptAsync(string target, CancellationToken cancellationToken)
    {
        await using var resource = typeof(TypeScriptMapper).Assembly.GetManifestResourceStream("map.js")
            ?? throw new InvalidOperationException("map.js is not embedded in the TypeScript mapper assembly");
        await using var file = File.Create(target);
        await resource.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RunNodeAsync(string script, string request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var node = _node();
        var startInfo = new ProcessStartInfo(node)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        startInfo.ArgumentList.Add(script);

        using var process = StartNode(node, startInfo);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = new StringBuilder();
            var stderr = ReadErrorsAsync(process.StandardError, progress, errors, cancellationToken);
            await process.StandardInput.WriteAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            await stderr.ConfigureAwait(false);
            return process.ExitCode == 0 ? output : throw new InvalidOperationException($"TypeScript mapping failed: {errors.ToString().Trim()}");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private static async Task ReadErrorsAsync(StreamReader reader, IProgress<string>? progress, StringBuilder errors, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            {
                progress?.Report(line[ProgressPrefix.Length..]);
            }
            else
            {
                errors.AppendLine(line);
            }
        }
    }

    private static Process StartNode(string node, ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException($"{node} did not start.");
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException($"{node} was not found on PATH; install Node.js 22 or later from https://nodejs.org to map TypeScript.", error);
        }
    }

    private sealed record MapRequest(string RepoRoot, IReadOnlyList<string> Tsconfigs, IReadOnlyList<string> Paths);
}
