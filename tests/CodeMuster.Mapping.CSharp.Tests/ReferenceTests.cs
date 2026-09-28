using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class ReferenceTests
{
    [Fact]
    public async Task Calls_constructor_calls_and_method_groups_are_calls_and_a_call_to_itself_is_skipped()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class Box
            {
                public Box(int size) { }

                public static int Make() => 1;
            }

            public class User
            {
                public void Run()
                {
                    var box = new Box(Box.Make());
                    System.Func<int> make = Box.Make;
                    Box made = new(2);
                    Run();
                }
            }
            """);

        const string run = "M:N.User.Run";
        Assert.Equal(
            new[]
            {
                (run, "M:N.Box.#ctor(System.Int32)", ReferenceKind.Call, 14, 23),
                (run, "T:N.Box", ReferenceKind.Type, 14, 23),
                (run, "T:N.Box", ReferenceKind.Type, 14, 27),
                (run, "M:N.Box.Make", ReferenceKind.Call, 14, 31),
                (run, "T:N.Box", ReferenceKind.Type, 15, 33),
                (run, "M:N.Box.Make", ReferenceKind.Call, 15, 37),
                (run, "T:N.Box", ReferenceKind.Type, 16, 9),
                (run, "M:N.Box.#ctor(System.Int32)", ReferenceKind.Call, 16, 20),
                (run, "T:N.Box", ReferenceKind.Type, 16, 20),
            },
            Rows(map));
    }

    [Fact]
    public async Task Assignments_compound_assignments_increments_and_out_or_ref_arguments_are_writes_and_other_uses_are_reads()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class Counter
            {
                public int Field;
                public int Prop { get; set; }
                public event System.EventHandler? Changed;

                public static void Set(out int value) { value = 1; }
                public static void Bump(ref int value) { value++; }
            }

            public class User
            {
                public void Run(Counter c)
                {
                    c.Field = 1;
                    c.Field += c.Prop;
                    c.Prop++;
                    Counter.Set(out c.Field);
                    Counter.Bump(ref c.Field);
                    var x = c.Field + c.Prop;
                    c.Changed += (s, e) => { };
                    c.Changed?.Invoke(c, System.EventArgs.Empty);
                    _ = new Counter { Prop = 2 };
                }
            }
            """);

        const string run = "M:N.User.Run(N.Counter)";
        Assert.Equal(
            new[]
            {
                (run, "T:N.Counter", ReferenceKind.Type, 15, 21),
                (run, "F:N.Counter.Field", ReferenceKind.Write, 17, 11),
                (run, "F:N.Counter.Field", ReferenceKind.Write, 18, 11),
                (run, "P:N.Counter.Prop", ReferenceKind.Read, 18, 22),
                (run, "P:N.Counter.Prop", ReferenceKind.Write, 19, 11),
                (run, "T:N.Counter", ReferenceKind.Type, 20, 9),
                (run, "M:N.Counter.Set(System.Int32@)", ReferenceKind.Call, 20, 17),
                (run, "F:N.Counter.Field", ReferenceKind.Write, 20, 27),
                (run, "T:N.Counter", ReferenceKind.Type, 21, 9),
                (run, "M:N.Counter.Bump(System.Int32@)", ReferenceKind.Call, 21, 17),
                (run, "F:N.Counter.Field", ReferenceKind.Write, 21, 28),
                (run, "F:N.Counter.Field", ReferenceKind.Read, 22, 19),
                (run, "P:N.Counter.Prop", ReferenceKind.Read, 22, 29),
                (run, "E:N.Counter.Changed", ReferenceKind.Write, 23, 11),
                (run, "E:N.Counter.Changed", ReferenceKind.Read, 24, 11),
                (run, "T:N.Counter", ReferenceKind.Call, 25, 17),
                (run, "T:N.Counter", ReferenceKind.Type, 25, 17),
                (run, "P:N.Counter.Prop", ReferenceKind.Write, 25, 27),
            },
            Rows(map));
    }

    [Fact]
    public async Task Type_uses_inheritance_implementation_attributes_enum_members_and_constants_have_their_own_kinds()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public interface IShape { }

            public class Base { }

            public class MarkAttribute : System.Attribute { }

            public enum Color { Red, Green }

            public static class Limits
            {
                public const int Max = 3;
            }

            [Mark]
            public class Square : Base, IShape
            {
                public System.Collections.Generic.List<Base> Items = new();
                public Base Convert(object value, Color color)
                {
                    var limit = Limits.Max;
                    var red = color == Color.Red;
                    var list = new System.Collections.Generic.List<IShape>();
                    var cast = (Base)value;
                    var isShape = value is IShape;
                    var type = typeof(Square);
                    return value as Base;
                }

                public int Limit => Limits.Max;

                public Color Tint { get; } = Color.Green;
            }
            """);

        const string convert = "M:N.Square.Convert(System.Object,N.Color)";
        Assert.Equal(
            new[]
            {
                ("T:N.Square", "T:N.MarkAttribute", ReferenceKind.Attribute, 16, 2),
                ("T:N.Square", "T:N.Base", ReferenceKind.Inherit, 17, 23),
                ("T:N.Square", "T:N.IShape", ReferenceKind.Implement, 17, 29),
                ("F:N.Square.Items", "T:N.Base", ReferenceKind.Type, 19, 44),
                (convert, "T:N.Base", ReferenceKind.Type, 20, 12),
                (convert, "T:N.Color", ReferenceKind.Type, 20, 39),
                (convert, "T:N.Limits", ReferenceKind.Type, 22, 21),
                (convert, "F:N.Limits.Max", ReferenceKind.Read, 22, 28),
                (convert, "T:N.Color", ReferenceKind.Type, 23, 28),
                (convert, "F:N.Color.Red", ReferenceKind.Read, 23, 34),
                (convert, "T:N.IShape", ReferenceKind.Type, 24, 56),
                (convert, "T:N.Base", ReferenceKind.Type, 25, 21),
                (convert, "T:N.IShape", ReferenceKind.Type, 26, 32),
                (convert, "T:N.Square", ReferenceKind.Type, 27, 27),
                (convert, "T:N.Base", ReferenceKind.Type, 28, 25),
                ("M:N.Square.get_Limit", "T:N.Limits", ReferenceKind.Type, 31, 25),
                ("M:N.Square.get_Limit", "F:N.Limits.Max", ReferenceKind.Read, 31, 32),
                ("P:N.Square.Tint", "T:N.Color", ReferenceKind.Type, 33, 12),
                ("P:N.Square.Tint", "T:N.Color", ReferenceKind.Type, 33, 34),
                ("P:N.Square.Tint", "F:N.Color.Green", ReferenceKind.Read, 33, 40),
            },
            Rows(map));
    }

    [Fact]
    public async Task Calls_through_an_interface_or_abstract_member_and_interface_property_and_indexer_uses_are_recorded()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public interface IStore
            {
                int Find(string key);
                int Count { get; set; }
                int this[int index] { get; set; }
            }

            public abstract class Shape
            {
                public abstract double Area();
            }

            public class User
            {
                public double Run(IStore store, Shape shape)
                {
                    store.Count = store.Find("a");
                    store[0] = store[1] + store.Count;
                    return shape.Area();
                }
            }
            """);

        const string run = "M:N.User.Run(N.IStore,N.Shape)";
        Assert.Equal(
            new[]
            {
                (run, "T:N.IStore", ReferenceKind.Type, 17, 23),
                (run, "T:N.Shape", ReferenceKind.Type, 17, 37),
                (run, "P:N.IStore.Count", ReferenceKind.Write, 19, 15),
                (run, "M:N.IStore.Find(System.String)", ReferenceKind.Call, 19, 29),
                (run, "P:N.IStore.Item(System.Int32)", ReferenceKind.Write, 20, 14),
                (run, "P:N.IStore.Item(System.Int32)", ReferenceKind.Read, 20, 25),
                (run, "P:N.IStore.Count", ReferenceKind.Read, 20, 37),
                (run, "M:N.Shape.Area", ReferenceKind.Call, 21, 22),
            },
            Rows(map));
    }

    [Fact]
    public async Task New_of_a_type_without_a_declared_constructor_is_a_call_to_the_type()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public record Quote(int Id);

            public class Plain { }

            public class User
            {
                public void Run()
                {
                    var quote = new Quote(1);
                    Plain plain = new();
                    _ = new Plain();
                }
            }
            """);

        const string run = "M:N.User.Run";
        Assert.Equal(
            new[]
            {
                (run, "T:N.Quote", ReferenceKind.Call, 11, 25),
                (run, "T:N.Quote", ReferenceKind.Type, 11, 25),
                (run, "T:N.Plain", ReferenceKind.Type, 12, 9),
                (run, "T:N.Plain", ReferenceKind.Call, 12, 23),
                (run, "T:N.Plain", ReferenceKind.Type, 12, 23),
                (run, "T:N.Plain", ReferenceKind.Call, 13, 17),
                (run, "T:N.Plain", ReferenceKind.Type, 13, 17),
            },
            Rows(map));
    }

    [Fact]
    public async Task References_to_metadata_types_and_members_are_not_recorded()
    {
        var map = await Inline.MapAsync("""
            using System;
            using System.Text;

            namespace N;

            public class Log : IDisposable
            {
                public void Write(string text)
                {
                    var builder = new StringBuilder(text);
                    Console.WriteLine(builder.Length + text.Length + (int)DayOfWeek.Monday);
                }

                public void Dispose() { }
            }
            """);

        Assert.Empty(map.References);
    }

    [Fact]
    public async Task References_across_files_point_at_the_other_file_and_repeated_parallel_maps_are_identical()
    {
        var files = Enumerable.Range(0, 48)
            .Select(index => ($"src/F{index:D2}.cs", $$"""
                namespace N;

                public static class C{{index:D2}}
                {
                    public const int Value = {{index}};

                    public static int Next() => C{{(index + 1) % 48:D2}}.Value + C{{(index + 47) % 48:D2}}.Next();
                }
                """))
            .ToArray();

        var first = CodeMapJson.Serialize(await Inline.MapAsync(files));
        for (var run = 0; run < 4; run++)
        {
            Assert.Equal(first, CodeMapJson.Serialize(await Inline.MapAsync(files)));
        }

        var map = CodeMapJson.Parse(first);
        Assert.Equal(48 * 4, map.References.Count);
        Assert.Equal(
            new[]
            {
                new Reference("M:N.C00.Next", "T:N.C01", ReferenceKind.Type, "src/F00.cs", 7, 33),
                new Reference("M:N.C00.Next", "F:N.C01.Value", ReferenceKind.Read, "src/F00.cs", 7, 37),
                new Reference("M:N.C00.Next", "T:N.C47", ReferenceKind.Type, "src/F00.cs", 7, 45),
                new Reference("M:N.C00.Next", "M:N.C47.Next", ReferenceKind.Call, "src/F00.cs", 7, 49),
            },
            map.References.Take(4));
    }

    private static IEnumerable<(string From, string To, ReferenceKind Kind, int Line, int Column)> Rows(CodeMap map)
    {
        Assert.All(map.References, reference => Assert.Equal("src/Code.cs", reference.Path));
        return map.References.Select(reference => (reference.From, reference.To, reference.Kind, reference.Line, reference.Column));
    }
}
