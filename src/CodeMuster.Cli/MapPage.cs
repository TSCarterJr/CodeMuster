using System.Text;

namespace CodeMuster.Cli;

/// <summary>The self-contained interactive page map --out &lt;file&gt;.html writes (D62): MapPage.html with the map's JSON in place of its placeholder.</summary>
public static class MapPage
{
    private const string Placeholder = "__CODEMUSTER_MAP_DATA__";

    private static string Template { get; } = Read();

    /// <summary>The page with <paramref name="data"/> embedded; the data must already escape '&lt;', as CodeMapQuery's JSON does, so it cannot close its script element.</summary>
    public static string Render(string data) => Template.Replace(Placeholder, data, StringComparison.Ordinal);

    private static string Read()
    {
        using var stream = typeof(MapPage).Assembly.GetManifestResourceStream("MapPage.html")
            ?? throw new InvalidOperationException("MapPage.html is not embedded in the codemuster assembly");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
