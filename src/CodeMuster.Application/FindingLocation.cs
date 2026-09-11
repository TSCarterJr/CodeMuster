using System.Globalization;
using CodeMuster.Domain;

namespace CodeMuster.Application;

internal static class FindingLocation
{
    public static string Of(Finding finding) => finding.LineStart == finding.LineEnd
        ? string.Create(CultureInfo.InvariantCulture, $"{finding.Path}:{finding.LineStart}")
        : string.Create(CultureInfo.InvariantCulture, $"{finding.Path}:{finding.LineStart}-{finding.LineEnd}");
}
