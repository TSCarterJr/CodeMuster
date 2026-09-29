using System.Xml.Linq;

namespace CodeMuster.Domain;

/// <summary>Recognizes test code by its path and C# test projects by their project file (D79).</summary>
public static class TestFiles
{
    private static readonly string[] TestDirectories = ["test", "tests", "__tests__", "spec", "specs", "e2e"];

    private static readonly string[] TestPackages = ["Microsoft.NET.Test.Sdk", "xunit", "xunit.v3", "xunit.core", "NUnit", "MSTest", "MSTest.TestFramework"];

    /// <summary>True when a directory of the path is a test directory, or the file name follows a test naming convention.</summary>
    public static bool IsTestPath(string path)
    {
        var normalized = RepoPath.Normalize(path);
        var slash = normalized.LastIndexOf('/');
        var name = normalized[(slash + 1)..];
        var directories = slash < 0 ? [] : normalized[..slash].Split('/');
        if (directories.Any(d => TestDirectories.Contains(d, StringComparer.OrdinalIgnoreCase) || d.StartsWith("e2e-", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return name.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("_test", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("test_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test.cs", StringComparison.Ordinal)
            || name.EndsWith("Tests.cs", StringComparison.Ordinal);
    }

    /// <summary>True when a C# project file sets <c>IsTestProject</c>, uses the MSTest SDK, or references a test framework package; false for anything unreadable.</summary>
    public static bool IsTestProject(string projectXml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(projectXml);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        var elements = document.Descendants().ToList();
        if (elements.Any(e => e.Name.LocalName == "IsTestProject" && string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (document.Root?.Attribute("Sdk")?.Value.StartsWith("MSTest.Sdk", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return elements.Any(e => e.Name.LocalName == "PackageReference"
            && TestPackages.Contains(e.Attribute("Include")?.Value.Trim(), StringComparer.OrdinalIgnoreCase));
    }
}
