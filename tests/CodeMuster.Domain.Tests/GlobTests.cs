using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class GlobTests
{
    [Theory]
    [InlineData("**/*.cs", "src/a/b.cs", true)]
    [InlineData("**/*.cs", "a.cs", true)]
    [InlineData("**/*.cs", "src/a/b.ts", false)]
    [InlineData("src/**/*.cs", "src/a.cs", true)]
    [InlineData("src/**/*.cs", "src/a/b/c.cs", true)]
    [InlineData("src/**/*.cs", "lib/a.cs", false)]
    [InlineData("src/*.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", "src/a/b.cs", false)]
    [InlineData("*.g.cs", "src/Data/QuoteDto.g.cs", true)]
    [InlineData("*.g.cs", "src/Data/QuoteDto.cs", false)]
    [InlineData("web/**", "web/app/page.tsx", true)]
    [InlineData("web/**", "web/a.ts", true)]
    [InlineData("web/**", "src/a.cs", false)]
    [InlineData("**", "any/thing/at/all.txt", true)]
    [InlineData("?.cs", "a.cs", true)]
    [InlineData("?.cs", "ab.cs", false)]
    [InlineData("?.cs", "src/a.cs", true)]
    [InlineData("a.cs", "a.cs", true)]
    [InlineData("a.cs", "b/a.cs", true)]
    [InlineData("a.cs", "axcs", false)]
    [InlineData("a.cs", "b/axcs", false)]
    [InlineData("a.cs", "ba.cs", false)]
    [InlineData("src/a.cs", "src/a.cs", true)]
    [InlineData("src/a.cs", "x/src/a.cs", false)]
    [InlineData("src/a.cs", "src/a.csx", false)]
    [InlineData("src/A.cs", "src/a.cs", false)]
    [InlineData("*.CS", "src/a.cs", false)]
    [InlineData("src/(a).cs", "src/(a).cs", true)]
    [InlineData("src/a+b.cs", "src/a+b.cs", true)]
    [InlineData("src/a+b.cs", "src/aab.cs", false)]
    [InlineData("src/[a].cs", "src/[a].cs", true)]
    [InlineData("src/[a].cs", "src/a.cs", false)]
    [InlineData("./src/*.cs", "src/a.cs", true)]
    [InlineData(@"src\*.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", @"src\a.cs", true)]
    [InlineData("src/*.cs", "./src/a.cs", true)]
    [InlineData("src/**/Migrations/*.cs", "src/Data/Migrations/Initial.cs", true)]
    [InlineData("src/**/Migrations/*.cs", "src/Migrations/Initial.cs", true)]
    [InlineData("src/**/Migrations/*.cs", "src/Data/MigrationsHelper/Initial.cs", false)]
    public void IsMatch_follows_gitignore_style_semantics(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Glob.IsMatch(pattern, path));
    }
}
