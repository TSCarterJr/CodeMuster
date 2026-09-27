using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

// Applies the hidden engine commands (D65) to one run, verify or fix, and acknowledges each with an event; docs/internal/engine-protocol.md lists them.
// The owning loop waits on Arrival alongside its calls, then calls TryApply; resize and stop are how a command reaches the loop's own pool and token.
internal sealed class Steering(IEngineControl? control, IEngineEvents? events, int workers, AgentIdentity identity, bool canRetarget, Action<int> resize, Action stop)
{
    private readonly Task never = new TaskCompletionSource().Task;
    private Task<string>? pending;

    public bool Paused { get; private set; }

    public int Workers { get; private set; } = workers;

    // The agent units started from now on run with; a model or effort command changes it (D35 records it per analysis).
    public AgentIdentity Identity { get; private set; } = identity;

    public Task Arrival(CancellationToken cancellationToken) =>
        control is null ? never : pending ??= control.NextCommandAsync(cancellationToken);

    // Applies the command that has arrived, if one has; true when the loop should look again at its pool.
    public bool TryApply()
    {
        if (pending is not { IsCompleted: true } arrived) return false;
        pending = null;
        if (arrived.IsCanceled) return false;
        Apply(arrived.GetAwaiter().GetResult());
        return true;
    }

    private void Apply(string line)
    {
        if (line.Trim().Length == 0) return;
        if (!TryParse(line, out var command, out var value, out var problem))
        {
            Reject(line, problem);
            return;
        }

        switch (command)
        {
            case "pause" when Paused:
                Reject(line, "already paused");
                return;
            case "pause":
                Paused = true;
                Applied(line, command, null);
                events.Emit("paused");
                return;
            case "resume" when !Paused:
                Reject(line, "not paused");
                return;
            case "resume":
                Paused = false;
                Applied(line, command, null);
                events.Emit("resumed");
                return;
            case "stop":
                Applied(line, command, null);
                events.Emit("stopped");
                stop();
                return;
            case "workers":
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1)
                {
                    Reject(line, "workers needs a whole number of at least 1");
                    return;
                }

                Workers = count;
                resize(count);
                Applied(line, command, count);
                return;
            case "model" or "effort":
                if (string.IsNullOrEmpty(value))
                {
                    Reject(line, $"{command} needs a value");
                    return;
                }

                if (!canRetarget)
                {
                    Reject(line, $"this command cannot change the agent's {command}");
                    return;
                }

                Identity = command == "model" ? Identity with { Model = value } : Identity with { Effort = value };
                Applied(line, command, value);
                return;
            default:
                Reject(line, $"unknown command '{command}'");
                return;
        }
    }

    // A command is a plain line, "workers 2", or a JSON object such as {"command": "workers", "value": 2}.
    private static bool TryParse(string line, out string command, out string? value, out string problem)
    {
        var text = line.Trim();
        problem = "";
        value = null;
        if (!text.StartsWith('{'))
        {
            var space = text.IndexOfAny([' ', '\t']);
            command = (space < 0 ? text : text[..space]).ToLowerInvariant();
            value = space < 0 ? null : text[(space + 1)..].Trim();
            return true;
        }

        command = "";
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("command", out var name) || name.ValueKind != JsonValueKind.String)
            {
                problem = "a JSON command needs a string \"command\"";
                return false;
            }

            command = name.GetString()!.Trim().ToLowerInvariant();
            if (document.RootElement.TryGetProperty("value", out var given))
            {
                value = given.ValueKind switch
                {
                    JsonValueKind.String => given.GetString()!.Trim(),
                    JsonValueKind.Number => given.GetRawText(),
                    JsonValueKind.Null => null,
                    _ => given.GetRawText(),
                };
            }

            return true;
        }
        catch (JsonException ex)
        {
            problem = "not valid JSON: " + ex.Message;
            return false;
        }
    }

    private void Applied(string line, string command, object? value) =>
        events.Emit("command_applied", ("command", command), ("value", value), ("line", line));

    private void Reject(string line, string reason) =>
        events.Emit("command_rejected", ("line", line), ("reason", reason));
}
