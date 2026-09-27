using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

/// <summary>Appends engine events (D65) to a file as UTF-8 JSON lines, each ending in LF and flushed as it is written, so a reader tailing the file never sees half an event.</summary>
public sealed class EngineEventFile : IEngineEvents, IDisposable
{
    public const int Version = 1;

    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly FileStream file;
    private readonly IClock clock;
    private readonly Lock gate = new();

    public EngineEventFile(string path, IClock clock)
    {
        this.clock = clock;
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null) Directory.CreateDirectory(folder);
        // Readers may open, tail and even delete the file while a command writes to it.
        file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    }

    public void Emit(EngineEvent engineEvent)
    {
        using var line = new MemoryStream();
        using (var writer = new Utf8JsonWriter(line, Options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteString("t", Timestamps.Format(clock.UtcNow));
            writer.WriteString("type", engineEvent.Type);
            foreach (var (name, value) in engineEvent.Fields)
            {
                writer.WritePropertyName(name);
                Write(writer, value);
            }

            writer.WriteEndObject();
        }

        line.WriteByte((byte)'\n');
        lock (gate)
        {
            file.Write(line.GetBuffer(), 0, (int)line.Length);
            file.Flush();
        }
    }

    public void Dispose() => file.Dispose();

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case int number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            case IEnumerable<KeyValuePair<string, object?>> fields:
                writer.WriteStartObject();
                foreach (var (name, field) in fields)
                {
                    writer.WritePropertyName(name);
                    Write(writer, field);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items) Write(writer, item);
                writer.WriteEndArray();
                break;
            default: writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
    }
}
