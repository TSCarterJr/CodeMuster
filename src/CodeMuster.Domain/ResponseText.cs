namespace CodeMuster.Domain;

/// <summary>Pulls the JSON document out of whatever a model printed around it: code fences, prose before, prose after.</summary>
public static class ResponseText
{
    /// <summary>The first balanced <c>{</c>...<c>}</c> object in the text; when no object closes, the text from the first <c>{</c> to the last <c>}</c>, or the trimmed text when there is no pair, so parsing reports the real error.</summary>
    public static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return text.Trim();
        }

        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        var end = text.LastIndexOf('}');
        return end > start ? text[start..(end + 1)] : text.Trim();
    }
}
