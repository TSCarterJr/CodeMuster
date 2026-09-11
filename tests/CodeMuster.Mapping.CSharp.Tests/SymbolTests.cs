using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class SymbolTests
{
    [Fact]
    public async Task Accessors_with_bodies_are_symbols_and_auto_accessors_are_not()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public class C
            {
                private int _count;

                public int Auto { get; set; }

                public int Count
                {
                    get { return _count; }
                    set => _count = value;
                }

                public int Double => _count * 2;

                public int this[int index] => index;
            }
            """);

        Assert.Equal(
            new[]
            {
                ("M:N.C.get_Count", "accessor", "public class C\npublic int Count { get; }", 11, 11),
                ("M:N.C.get_Double", "accessor", "public class C\npublic int Double { get; }", 15, 15),
                ("M:N.C.get_Item(System.Int32)", "accessor", "public class C\npublic int this[int index] { get; }", 17, 17),
                ("M:N.C.set_Count(System.Int32)", "accessor", "public class C\npublic int Count { set; }", 12, 12),
            },
            map.Symbols
                .Select(symbol => (symbol.Id, symbol.Kind, symbol.Signature, symbol.Range.StartLine, symbol.Range.EndLine))
                .OrderBy(symbol => symbol.Id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Constructors_operators_and_default_interface_methods_are_symbols_but_bodiless_and_synthesized_members_are_not()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public interface IShape
            {
                double Area();

                string Describe()
                {
                    return "shape";
                }
            }

            public abstract class Base
            {
                static Base()
                {
                }

                protected Base(int size)
                {
                    Size = size;
                }

                public int Size { get; }

                public abstract void Draw();

                public static Base operator +(Base left, Base right) => left;

                public static implicit operator int(Base value) => value.Size;
            }

            public record Point(int X, int Y);

            public class Primary(int value)
            {
                public int Value() => value;
            }
            """);

        Assert.Equal(
            new[]
            {
                ("M:N.Base.#cctor", "constructor"),
                ("M:N.Base.#ctor(System.Int32)", "constructor"),
                ("M:N.Base.op_Addition(N.Base,N.Base)", "operator"),
                ("M:N.Base.op_Implicit(N.Base)~System.Int32", "operator"),
                ("M:N.IShape.Describe", "method"),
                ("M:N.Primary.Value", "method"),
            },
            map.Symbols.Select(symbol => (symbol.Id, symbol.Kind)).OrderBy(symbol => symbol.Id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Range_keeps_attributes_and_drops_leading_comments_and_signature_collapses_whitespace()
    {
        var map = await Inline.MapAsync("""
            using System;

            namespace N;

            /// <summary>Type docs.</summary>
            [Serializable]
            public sealed class Box<T> : IDisposable
                where T : class
            {
                // A leading comment.
                /// <summary>Docs.</summary>
                [Obsolete("old")]
                public   void   Dispose( )
                {
                }
            }
            """);

        var symbol = Assert.Single(map.Symbols);
        Assert.Equal("M:N.Box`1.Dispose", symbol.Id);
        Assert.Equal("src/Code.cs", symbol.Path);
        Assert.Equal(new LineRange(12, 15), symbol.Range);
        Assert.Equal("[Serializable] public sealed class Box<T> : IDisposable where T : class\n[Obsolete(\"old\")] public void Dispose( )", symbol.Signature);
        Assert.Equal(Hashing.Sha256Hex("[ Obsolete ( \"old\" ) ] public void Dispose ( ) { }"), symbol.BodyHash);
    }
}
