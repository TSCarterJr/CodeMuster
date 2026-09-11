using System.Globalization;

namespace CodeMuster.Mapping.CSharp.Tests;

public class ResolutionTests
{
    [Fact]
    public async Task Counts_invocations_and_object_creations_inside_symbols_only()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class Thing
            {
            }

            public class C
            {
                private readonly Thing _field = new Thing();

                public void Run()
                {
                    Helper();
                    var thing = new Thing();
                    Thing other = new();
                    Missing();
                    thing.Absent();
                    var name = nameof(Run);
                    Local();

                    void Local()
                    {
                        Missing();
                    }
                }

                private object Helper() => new Unknown();
            }
            """);

        Assert.Equal(4, map.Resolution.Resolved);
        Assert.Equal(4, map.Resolution.Unresolved);
        Assert.Equal(new[] { "Missing", "Absent", "Unknown" }, map.Resolution.TopUnresolvedNames);
    }

    [Fact]
    public async Task Top_unresolved_names_are_the_twenty_most_frequent_by_count_then_name()
    {
        var calls = new[] { "Zeta();", "Zeta();", "Zeta();", "Beta();", "Alpha();", "Beta();", "Alpha();" }
            .Concat(Enumerable.Range(1, 19).Reverse().Select(index => "N" + index.ToString("00", CultureInfo.InvariantCulture) + "();"));

        var map = await Inline.MapAsync("namespace N; public class C { public void Run() { " + string.Join(" ", calls) + " } }");

        Assert.Equal(0, map.Resolution.Resolved);
        Assert.Equal(26, map.Resolution.Unresolved);
        Assert.Equal(
            new[] { "Zeta", "Alpha", "Beta" }.Concat(Enumerable.Range(1, 17).Select(index => "N" + index.ToString("00", CultureInfo.InvariantCulture))),
            map.Resolution.TopUnresolvedNames);
    }
}
