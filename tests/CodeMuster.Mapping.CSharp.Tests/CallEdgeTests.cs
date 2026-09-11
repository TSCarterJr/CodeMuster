using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class CallEdgeTests
{
    [Fact]
    public async Task Property_and_indexer_reads_and_writes_reach_the_accessor_that_runs()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class Counter
            {
                private int _value;

                public int Value
                {
                    get { return _value; }
                    set { _value = value; }
                }

                public int this[int index]
                {
                    get { return index; }
                    set { }
                }
            }

            public class User
            {
                public int Read(Counter counter) { return counter.Value; }

                public void Write(Counter counter) { counter.Value = 1; }

                public void Both(Counter counter) { counter.Value += 1; }

                public void Index(Counter counter) { counter[0] = counter[1]; }

                public Counter Create() { return new Counter { Value = 2 }; }
            }
            """);

        AssertCalls(
            map,
            ("M:N.User.Both(N.Counter)", "M:N.Counter.get_Value"),
            ("M:N.User.Both(N.Counter)", "M:N.Counter.set_Value(System.Int32)"),
            ("M:N.User.Create", "M:N.Counter.set_Value(System.Int32)"),
            ("M:N.User.Index(N.Counter)", "M:N.Counter.get_Item(System.Int32)"),
            ("M:N.User.Index(N.Counter)", "M:N.Counter.set_Item(System.Int32,System.Int32)"),
            ("M:N.User.Read(N.Counter)", "M:N.Counter.get_Value"),
            ("M:N.User.Write(N.Counter)", "M:N.Counter.set_Value(System.Int32)"));
    }

    [Fact]
    public async Task Object_creations_constructor_initializers_and_method_groups_are_calls_but_nameof_is_not()
    {
        var map = await Inline.MapAsync("""
            using System.Linq;

            namespace N;

            public class Thing
            {
                public Thing() : this(0)
                {
                }

                public Thing(int size)
                {
                }
            }

            public class Special : Thing
            {
                public Special() : base(1)
                {
                }
            }

            public class Maker
            {
                public Thing Make() { return new Thing(); }

                public Thing MakeTargetTyped() { Thing thing = new(2); return thing; }

                public int[] Map(int[] values) { return values.Select(Twice).ToArray(); }

                public string Name() { return nameof(Twice); }

                private static int Twice(int value) { return value * 2; }
            }
            """);

        AssertCalls(
            map,
            ("M:N.Maker.Make", "M:N.Thing.#ctor"),
            ("M:N.Maker.MakeTargetTyped", "M:N.Thing.#ctor(System.Int32)"),
            ("M:N.Maker.Map(System.Int32[])", "M:N.Maker.Twice(System.Int32)"),
            ("M:N.Special.#ctor", "M:N.Thing.#ctor(System.Int32)"),
            ("M:N.Thing.#ctor", "M:N.Thing.#ctor(System.Int32)"));
    }

    [Fact]
    public async Task Calls_inside_local_functions_and_lambdas_belong_to_the_containing_symbol()
    {
        var map = await Inline.MapAsync("""
            using System;

            namespace N;

            public class Runner
            {
                public void Run()
                {
                    Local();
                    Action act = () => Helper();
                    act();

                    void Local() => Helper();
                }

                private static void Helper()
                {
                }
            }
            """);

        Assert.Equal(new[] { "M:N.Runner.Helper", "M:N.Runner.Run" }, map.Symbols.Select(symbol => symbol.Id).Order(StringComparer.Ordinal));
        AssertCalls(map, ("M:N.Runner.Run", "M:N.Runner.Helper"));
    }

    [Fact]
    public async Task Extension_and_generic_calls_reach_the_original_definition()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public static class Extensions
            {
                public static int Twice(this int value) { return value * 2; }

                public static T Echo<T>(T value) { return value; }
            }

            public class User
            {
                public int Run() { return 3.Twice() + Extensions.Echo(1); }
            }
            """);

        AssertCalls(
            map,
            ("M:N.User.Run", "M:N.Extensions.Echo``1(``0)"),
            ("M:N.User.Run", "M:N.Extensions.Twice(System.Int32)"));
    }

    [Fact]
    public async Task Files_outside_the_included_paths_contribute_no_symbols_and_receive_no_edges()
    {
        var map = await Inline.MapAsync(
            [
                ("src/A.cs", "namespace N; public class A { public void Run() { new B().Go(); } }"),
                ("src/Generated/B.g.cs", "namespace N; public class B { public void Go() { } }"),
            ],
            included: ["src/A.cs"]);

        Assert.Equal("M:N.A.Run", Assert.Single(map.Symbols).Id);
        Assert.Empty(map.Edges);
    }

    private static void AssertCalls(CodeMap map, params (string From, string To)[] expected)
    {
        Assert.Equal(
            expected.Select(edge => new Edge(edge.From, edge.To, EdgeKind.Call)),
            map.Edges.OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ThenBy(edge => edge.Kind));
    }
}
