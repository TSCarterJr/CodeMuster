namespace CodeMuster.Cli;

public sealed record TerminalStyle(bool Enabled, bool Color, int Width = 80)
{
    public static TerminalStyle Detect(bool redirected, bool ci, string? term, string? noColor, int width = 80)
    {
        var enabled = !redirected && !ci && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
        return new(enabled, enabled && string.IsNullOrEmpty(noColor), width);
    }

    public string Accent(string text) => Paint(text, "1;36");
    public string Strong(string text) => Paint(text, "1");
    public string Thinking(string text) => Paint(text, "1;35");
    public string Paint(string text, string code) => Enabled && Color ? $"\u001b[{code}m{text}\u001b[0m" : text;

    public string Fit(string text)
    {
        var limit = Math.Max(1, Width - 1);
        return text.Length <= limit ? text : text[..limit];
    }
}
