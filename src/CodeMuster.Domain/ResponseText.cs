namespace CodeMuster.Domain;

/// <summary>Pulls the JSON document out of whatever a model printed around it: code fences, prose before, prose after.</summary>
public static class ResponseText
{
    /// <summary>The text from the first <c>{</c> to the last <c>}</c> inclusive, or the trimmed text unchanged when there is no such pair, so parsing reports the real error.</summary>
    public static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text.Trim();
    }
}
