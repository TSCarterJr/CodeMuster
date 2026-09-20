using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public static class CodexSettingsResolver
{
    private const string Failure = "Could not resolve Codex model and thinking settings. Check your Codex configuration or pass both --model <model-id> and --effort <level>. No agent work has started.";

    public static Task<AgentIdentity> ResolveAsync(AgentIdentity requested, string workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requested.Agent != "codex" || requested.Model is not null && requested.Effort is not null)
            return Task.FromResult(requested);
        var executable = ExecutableResolver.Resolve("codex");
        IReadOnlyList<string> prefix = [];
        if (OperatingSystem.IsWindows() && Path.GetExtension(executable).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var script = Path.Combine(Path.GetDirectoryName(executable)!, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (!File.Exists(script)) throw new InvalidOperationException(Failure);
            executable = ExecutableResolver.Resolve("node");
            prefix = [script];
        }
        return ResolveAsync(requested, workingDirectory, executable, prefix, TimeSpan.FromSeconds(5), cancellationToken);
    }

    internal static async Task<AgentIdentity> ResolveAsync(AgentIdentity requested, string workingDirectory, string executable,
        IReadOnlyList<string> prefix, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        var utf8 = new UTF8Encoding(false);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            CreateNoWindow = true,
        };
        foreach (var argument in prefix.Concat(["app-server", "--listen", "stdio://"])) start.ArgumentList.Add(argument);
        start.Environment["CODEMUSTER_WORKER"] = "1";
        try
        {
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(start) ?? throw new InvalidOperationException(Failure);
            var stderr = DrainAsync(process.StandardError, token);
            try
            {
                var requestId = 0;
                await RequestAsync("initialize", new { clientInfo = new { name = "codemuster", version = "1" } });
                await SendAsync(new { method = "initialized", @params = new { } });
                var response = await RequestAsync("config/read", new { cwd = workingDirectory, includeLayers = false });
                var config = response.GetProperty("config");
                var model = requested.Model ?? Text(config, "model");
                var effort = requested.Effort ?? Text(config, "model_reasoning_effort");
                if (model is not null && effort is not null) return new("codex", model, effort);

                string? cursor = null;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    var catalog = await RequestAsync("model/list", new { cursor, limit = 100, includeHidden = true });
                    foreach (var item in catalog.GetProperty("data").EnumerateArray())
                    {
                        var candidate = Text(item, "model");
                        if (candidate is null || (model is null
                            ? !item.TryGetProperty("isDefault", out var isDefault) || isDefault.ValueKind != JsonValueKind.True
                            : candidate != model)) continue;
                        model = candidate;
                        effort ??= Text(item, "defaultReasoningEffort");
                        if (effort is not null) return new("codex", model, effort);
                    }
                    cursor = Text(catalog, "nextCursor");
                } while (cursor is not null && seen.Add(cursor));
                throw new InvalidOperationException(Failure);

                async Task SendAsync(object message)
                {
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
                }

                async Task<JsonElement> RequestAsync(string method, object parameters)
                {
                    var id = ++requestId;
                    await SendAsync(new { id, method, @params = parameters });
                    while (await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                    {
                        using var document = JsonDocument.Parse(line);
                        var root = document.RootElement;
                        if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number || responseId.GetInt32() != id) continue;
                        if (!root.TryGetProperty("result", out var result)) throw new InvalidOperationException(Failure);
                        return result.Clone();
                    }
                    throw new InvalidOperationException(Failure);
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                try { await stderr.ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is OperationCanceledException or JsonException or IOException or InvalidOperationException or Win32Exception or KeyNotFoundException)
        {
            throw new InvalidOperationException(Failure);
        }
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString() : null;

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0) { }
    }
}
