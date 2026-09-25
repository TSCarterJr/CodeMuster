using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class GlobTests
{
    public static TheoryData<string, string, bool> Cases => new()
    {
        { "**/*.cs", "src/a/b.cs", true },
        { "**/*.cs", "a.cs", true },
        { "**/*.cs", "src/a/b.ts", false },
        { "src/**/*.cs", "src/a.cs", true },
        { "src/**/*.cs", "src/a/b/c.cs", true },
        { "src/**/*.cs", "lib/a.cs", false },
        { "src/*.cs", "src/a.cs", true },
        { "src/*.cs", "src/a/b.cs", false },
        { "*.g.cs", "src/Data/QuoteDto.g.cs", true },
        { "*.g.cs", "src/Data/QuoteDto.cs", false },
        { "web/**", "web/app/page.tsx", true },
        { "web/**", "web/a.ts", true },
        { "web/**", "src/a.cs", false },
        { "web/**", "web", false },
        { "web/**", "webapp/a.ts", false },
        { "web/", "web/a.ts", false },
        { "web/", "src/web", true },
        { "**/web/**", "web/a.ts", true },
        { "**/web/**", "src/web/a.ts", true },
        { "**/web/**", "src/webapp/a.ts", false },
        { "**", "any/thing/at/all.txt", true },
        { "?.cs", "a.cs", true },
        { "?.cs", "ab.cs", false },
        { "?.cs", "src/a.cs", true },
        { "src/?/*.cs", "src/a/b.cs", true },
        { "src/?/*.cs", "src/ab/b.cs", false },
        { "a.cs", "a.cs", true },
        { "a.cs", "b/a.cs", true },
        { "a.cs", "axcs", false },
        { "a.cs", "b/axcs", false },
        { "a.cs", "ba.cs", false },
        { "src/a.cs", "src/a.cs", true },
        { "src/a.cs", "x/src/a.cs", false },
        { "src/a.cs", "src/a.csx", false },
        { "src/A.cs", "src/a.cs", false },
        { "*.CS", "src/a.cs", false },
        { "*.CS", "src/A.CS", true },
        { "*.cs", "src/A.CS", false },
        { "SRC/**", "src/a.cs", false },
        { "src/**", "SRC/a.cs", false },
        { "Web/**", "Web/App.tsx", true },
        { "src/(a).cs", "src/(a).cs", true },
        { "src/a+b.cs", "src/a+b.cs", true },
        { "src/a+b.cs", "src/aab.cs", false },
        { "src/[a].cs", "src/[a].cs", true },
        { "src/[a].cs", "src/a.cs", false },
        { "./src/*.cs", "src/a.cs", true },
        { "./web/**", "web/a.ts", true },
        { "./a.cs", "b/a.cs", true },
        { "./src/a.cs", "./src/a.cs", true },
        { @"src\*.cs", "src/a.cs", true },
        { "src/*.cs", @"src\a.cs", true },
        { "src/*.cs", "./src/a.cs", true },
        { "src/**/Migrations/*.cs", "src/Data/Migrations/Initial.cs", true },
        { "src/**/Migrations/*.cs", "src/Migrations/Initial.cs", true },
        { "src/**/Migrations/*.cs", "src/Data/MigrationsHelper/Initial.cs", false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void IsMatch_follows_gitignore_style_semantics(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Glob.IsMatch(pattern, path));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Compiled_glob_matches_exactly_like_IsMatch(string pattern, string path, bool expected)
    {
        var glob = new Glob(pattern);

        Assert.Equal(pattern, glob.Pattern);
        Assert.Equal(expected, glob.IsMatch(path));
        Assert.Equal(expected, glob.IsMatch(path));
    }

    [Fact]
    public void One_compiled_glob_matches_each_path_on_its_own()
    {
        var byName = new Glob("*.cs");
        var byPath = new Glob("./src/**/*.cs");

        Assert.Equal(
            [true, false, true, true, false],
            new[] { "src/a.cs", "a.ts", "./b.cs", @"x\y.cs", "src/cs" }.Select(byName.IsMatch));
        Assert.Equal(
            [true, false, true, true, false],
            new[] { "src/a.cs", "lib/a.cs", @"src\a\b.cs", "./src/a.cs", "a.cs" }.Select(byPath.IsMatch));
    }

    [Fact]
    public void GlobList_names_the_first_pattern_that_matches_in_list_order()
    {
        var globs = new GlobList(["web/**", "*.ts", "src/**"]);

        Assert.Equal(["web/**", "*.ts", "src/**"], globs.Patterns);
        Assert.Equal("web/**", globs.FirstMatch("web/lib/api.ts"));
        Assert.Equal("*.ts", globs.FirstMatch("tools/gen.ts"));
        Assert.Equal("src/**", globs.FirstMatch(@"src\A.cs"));
        Assert.Null(globs.FirstMatch("lib/A.cs"));
        Assert.True(globs.AnyMatch("./web/a.css"));
        Assert.False(globs.AnyMatch("Web/a.css"));
        Assert.False(new GlobList([]).AnyMatch("anything.cs"));
    }
}
