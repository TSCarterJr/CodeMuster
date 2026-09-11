using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class RepoPathTests
{
    [Theory]
    [InlineData("src/a.cs", "src/a.cs")]
    [InlineData(@"src\Data\a.cs", "src/Data/a.cs")]
    [InlineData(@".\src\a.cs", "src/a.cs")]
    [InlineData("./src/a.cs", "src/a.cs")]
    [InlineData("././src/a.cs", "src/a.cs")]
    [InlineData("src//a.cs", "src/a.cs")]
    [InlineData("src///a//b.cs", "src/a/b.cs")]
    [InlineData(".//src/a.cs", "src/a.cs")]
    [InlineData("src/a/", "src/a")]
    [InlineData(@"src\a\", "src/a")]
    [InlineData("a.cs", "a.cs")]
    [InlineData("", "")]
    public void Normalize_produces_forward_slash_repo_relative_path(string input, string expected)
    {
        Assert.Equal(expected, RepoPath.Normalize(input));
    }
}
