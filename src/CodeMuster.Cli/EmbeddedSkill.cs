using System.Text;

namespace CodeMuster.Cli;

public static class EmbeddedSkill
{
    public static string Text { get; } = Read();

    private static string Read()
    {
        using var stream = typeof(EmbeddedSkill).Assembly.GetManifestResourceStream("SKILL.md")
            ?? throw new InvalidOperationException("SKILL.md is not embedded in the codemuster assembly");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
