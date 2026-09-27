using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class NormalizedHashTests
{
    [Fact]
    public async Task Copies_that_differ_only_in_names_literals_and_comments_match()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class C
            {
                public int Total(int[] items)
                {
                    var sum = 0;
                    foreach (var item in items)
                    {
                        sum += item * 2;
                    }

                    return sum + Offset("a");
                }

                // A copy someone renamed.
                public int Sum(int[] values)
                {
                    var acc = 10;
                    foreach (var value in values)
                    {
                        acc += value * 3; /* scaled */
                    }

                    return acc + Shift("other");
                }

                private static int Offset(string text) => text.Length;

                private static int Shift(string label) => label.Length;
            }
            """);

        Assert.Equal(Hash(map, "M:N.C.Total(System.Int32[])"), Hash(map, "M:N.C.Sum(System.Int32[])"));
        Assert.Equal(Hash(map, "M:N.C.Offset(System.String)"), Hash(map, "M:N.C.Shift(System.String)"));
    }

    [Fact]
    public async Task A_changed_operator_or_an_extra_statement_does_not_match()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class C
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }

                public int Subtract(int a, int b)
                {
                    return a - b;
                }

                public int AddAndLog(int a, int b)
                {
                    System.Console.WriteLine(a);
                    return a + b;
                }

                public int AddConstant(int a, int b)
                {
                    return a + 1;
                }
            }
            """);

        var hashes = new[] { "Add", "Subtract", "AddAndLog", "AddConstant" }
            .Select(name => Hash(map, $"M:N.C.{name}(System.Int32,System.Int32)"))
            .ToList();
        Assert.Equal(hashes.Count, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Parameter_names_do_not_matter_but_keyword_types_and_keyword_literals_do()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class C
            {
                public bool First(int count) => count > 0 && true;

                public bool Second(int total) => total > 0 && true;

                public bool Wider(long count) => count > 0 && true;

                public bool Negated(int count) => count > 0 && false;
            }
            """);

        Assert.Equal(Hash(map, "M:N.C.First(System.Int32)"), Hash(map, "M:N.C.Second(System.Int32)"));
        Assert.NotEqual(Hash(map, "M:N.C.First(System.Int32)"), Hash(map, "M:N.C.Wider(System.Int64)"));
        Assert.NotEqual(Hash(map, "M:N.C.First(System.Int32)"), Hash(map, "M:N.C.Negated(System.Int32)"));
    }

    [Fact]
    public async Task Interpolated_and_raw_string_text_counts_as_a_literal()
    {
        var map = await Inline.MapAsync(""""
            namespace N;

            public class C
            {
                public string One(string name) => $"Hello {name}!" + """raw""";

                public string Two(string who) => $"Bye {who}." + """other""";
            }
            """");

        Assert.Equal(Hash(map, "M:N.C.One(System.String)"), Hash(map, "M:N.C.Two(System.String)"));
    }

    [Fact]
    public async Task Accessors_hash_the_property_header_with_the_accessor()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class C
            {
                private int _a;
                private int _b;

                public int A { get { return _a; } }

                public int B { get { return _b; } }

                public long L { get { return _a; } }
            }
            """);

        Assert.Equal(Hash(map, "M:N.C.get_A"), Hash(map, "M:N.C.get_B"));
        Assert.NotEqual(Hash(map, "M:N.C.get_A"), Hash(map, "M:N.C.get_L"));
    }

    private static string Hash(CodeMap map, string id) =>
        Assert.IsType<string>(map.Symbols.Single(symbol => symbol.Id == id).NormalizedHash);
}
