using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class DeclarationTests
{
    [Fact]
    public async Task Types_members_without_a_body_and_properties_are_declarations_and_symbols_are_unchanged()
    {
        var map = await Inline.MapAsync("""
            using System;

            namespace N;

            public interface IShape
            {
                double Area();
            }

            public delegate int Measure(string text);

            public enum Status
            {
                Open = 1,
                [Obsolete] Closed,
            }

            public struct Size
            {
                public const int Max = 10, Min = 0;
                public int Width;
            }

            public record Point(int X, int Y);

            public class Counter : IShape
            {
                private readonly int _start = 1;

                public event EventHandler? Changed;

                public event EventHandler Moved { add { } remove { } }

                public int Auto { get; private set; } = 3;

                public int Count
                {
                    get { return _start; }
                    set { }
                }

                public int Double => _start * 2;

                public double Area() => 0;
            }
            """);

        Assert.Equal(
            new[]
            {
                ("E:N.Counter.Changed", "event", "public event EventHandler? Changed", 30, 30),
                ("E:N.Counter.Moved", "event", "public event EventHandler Moved", 32, 32),
                ("F:N.Counter._start", "field", "private readonly int _start", 28, 28),
                ("F:N.Size.Max", "constant", "public const int Max", 20, 20),
                ("F:N.Size.Min", "constant", "public const int Min", 20, 20),
                ("F:N.Size.Width", "field", "public int Width", 21, 21),
                ("F:N.Status.Closed", "enum_member", "[Obsolete] Closed", 15, 15),
                ("F:N.Status.Open", "enum_member", "Open", 14, 14),
                ("M:N.IShape.Area", "method", "double Area()", 7, 7),
                ("P:N.Counter.Auto", "property", "public int Auto { get; private set; }", 34, 34),
                ("P:N.Counter.Count", "property", "public int Count { get; set; }", 36, 40),
                ("P:N.Counter.Double", "property", "public int Double { get; }", 42, 42),
                ("P:N.Point.X", "property", "int X", 24, 24),
                ("P:N.Point.Y", "property", "int Y", 24, 24),
                ("T:N.Counter", "class", "public class Counter : IShape", 26, 45),
                ("T:N.IShape", "interface", "public interface IShape", 5, 8),
                ("T:N.Measure", "delegate", "public delegate int Measure(string text)", 10, 10),
                ("T:N.Point", "record", "public record Point(int X, int Y)", 24, 24),
                ("T:N.Size", "struct", "public struct Size", 18, 22),
                ("T:N.Status", "enum", "public enum Status", 12, 16),
            },
            map.Declarations
                .Select(declaration => (declaration.Id, declaration.Kind, declaration.Signature, declaration.Range.StartLine, declaration.Range.EndLine))
                .OrderBy(declaration => declaration.Id, StringComparer.Ordinal));
        Assert.Equal(
            new[] { "M:N.Counter.Area", "M:N.Counter.add_Moved(System.EventHandler)", "M:N.Counter.get_Count", "M:N.Counter.get_Double", "M:N.Counter.remove_Moved(System.EventHandler)", "M:N.Counter.set_Count(System.Int32)" },
            map.Symbols.Select(symbol => symbol.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Interface_abstract_extern_and_unimplemented_partial_methods_and_indexers_are_declarations()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public interface IStore
            {
                int Find(string key);
                int this[int index] { get; set; }
                event System.EventHandler Saved;
                int Count { get; }
                void Log() { }
            }

            public abstract class Shape
            {
                public abstract double Area();
                public virtual double Scale() => 1;
            }

            public static partial class Native
            {
                [System.Runtime.InteropServices.DllImport("lib")]
                public static extern int Beep(int hz);
                static partial void Traced();
                static partial void Done();
                static partial void Done() { }
            }
            """);

        Assert.Equal(
            new[]
            {
                ("M:N.IStore.Find(System.String)", "method", "int Find(string key)", 5, 5),
                ("P:N.IStore.Item(System.Int32)", "indexer", "int this[int index] { get; set; }", 6, 6),
                ("M:N.Shape.Area", "method", "public abstract double Area()", 14, 14),
                ("M:N.Native.Beep(System.Int32)", "method", "[System.Runtime.InteropServices.DllImport(\"lib\")] public static extern int Beep(int hz)", 20, 21),
                ("M:N.Native.Traced", "method", "static partial void Traced()", 22, 22),
            },
            map.Declarations
                .Where(declaration => declaration.Kind is "method" or "indexer")
                .Select(declaration => (declaration.Id, declaration.Kind, declaration.Signature, declaration.Range.StartLine, declaration.Range.EndLine)));
        Assert.Equal(
            new[] { "M:N.IStore.Log", "M:N.Native.Done", "M:N.Shape.Scale" },
            map.Symbols.Select(symbol => symbol.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(map.Declarations, declaration => map.Symbols.Any(symbol => symbol.Id == declaration.Id));
    }

    [Fact]
    public async Task A_bodiless_method_hash_ignores_whitespace_and_changes_with_its_signature()
    {
        const string source = "namespace N;\n\npublic interface I\n{\n    int  Find( string key );\n}\n";
        var map = await Inline.MapAsync(source);
        var respaced = await Inline.MapAsync(source.Replace("int  Find( string key );", "int Find(string key); // note", StringComparison.Ordinal));
        var changed = await Inline.MapAsync(source.Replace("string key", "string key, int limit", StringComparison.Ordinal));

        var method = map.Declarations.Single(declaration => declaration.Kind == "method");
        Assert.Equal(Hashing.Sha256Hex("int Find ( string key ) ;"), method.BodyHash);
        Assert.Equal(method.BodyHash, respaced.Declarations.Single(declaration => declaration.Kind == "method").BodyHash);
        Assert.NotEqual(method.BodyHash, changed.Declarations.Single(declaration => declaration.Kind == "method").BodyHash);
    }

    [Fact]
    public async Task Declarations_are_ordered_by_path_and_line()
    {
        var map = await Inline.MapAsync([("src/B.cs", "namespace N;\n\npublic class B\n{\n    public int X;\n}\n"), ("src/A.cs", "namespace N;\n\npublic class A\n{\n}\n")]);

        Assert.Equal(["T:N.A", "T:N.B", "F:N.B.X"], map.Declarations.Select(declaration => declaration.Id));
    }

    [Fact]
    public async Task Range_keeps_attributes_and_drops_leading_comments_and_hashes_ignore_whitespace()
    {
        const string source = """
            using System;

            namespace N;

            public class C
            {
                // A leading comment.
                /// <summary>Docs.</summary>
                [Obsolete("old")]
                public   int   Limit  =  5;
            }
            """;
        var map = await Inline.MapAsync(source);
        var respaced = await Inline.MapAsync(source.Replace("public   int   Limit  =  5;", "public int Limit = 5; // changed comment", StringComparison.Ordinal));

        var field = map.Declarations.Single(declaration => declaration.Id == "F:N.C.Limit");
        Assert.Equal(new LineRange(9, 10), field.Range);
        Assert.Equal("[Obsolete(\"old\")] public int Limit", field.Signature);
        Assert.Equal(Hashing.Sha256Hex("[ Obsolete ( \"old\" ) ] public int Limit = 5 ;"), field.BodyHash);
        Assert.Equal(field.BodyHash, respaced.Declarations.Single(declaration => declaration.Id == "F:N.C.Limit").BodyHash);
    }

    [Fact]
    public async Task A_type_hash_covers_its_header_so_a_member_edit_does_not_change_it()
    {
        const string source = """
            namespace N;

            public sealed class C : System.IDisposable
            {
                public void Dispose() { }
            }
            """;
        var map = await Inline.MapAsync(source);
        var edited = await Inline.MapAsync(source.Replace("{ }", "{ System.GC.Collect(); }", StringComparison.Ordinal));

        var type = Assert.Single(map.Declarations);
        Assert.Equal(Hashing.Sha256Hex("public sealed class C : System . IDisposable"), type.BodyHash);
        Assert.Equal(type.BodyHash, Assert.Single(edited.Declarations).BodyHash);
        Assert.NotEqual(Assert.Single(map.Symbols).BodyHash, Assert.Single(edited.Symbols).BodyHash);
    }

    [Fact]
    public async Task A_partial_type_is_one_declaration_at_its_first_definition_and_its_members_are_all_kept()
    {
        var map = await Inline.MapAsync(
        [
            ("src/A.cs", "namespace N;\n\npublic partial class P\n{\n    public int First;\n}\n"),
            ("src/B.cs", "namespace N;\n\n[System.Serializable]\npublic partial class P\n{\n    public int Second;\n}\n"),
        ]);

        Assert.Equal(
            new[] { ("T:N.P", "src/A.cs", 3), ("F:N.P.First", "src/A.cs", 5), ("F:N.P.Second", "src/B.cs", 6) },
            map.Declarations.Select(declaration => (declaration.Id, declaration.Path, declaration.Range.StartLine)));
    }
}
