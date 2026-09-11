using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class DispatchEdgeTests
{
    private const string DependencyInjectionStubs = """
        using System;

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService => services;

                public static IServiceCollection AddScoped(this IServiceCollection services, Type serviceType, Type implementationType) => services;

                public static IServiceCollection AddSingleton(this IServiceCollection services, Type serviceType, Type implementationType) => services;
            }
        }

        namespace Microsoft.Extensions.DependencyInjection.Extensions
        {
            public static class ServiceCollectionDescriptorExtensions
            {
                public static void TryAddTransient<TService, TImplementation>(this IServiceCollection services) where TImplementation : class, TService
                {
                }
            }
        }
        """;

    [Fact]
    public async Task Interface_calls_with_no_binding_reach_every_implementation()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public interface IStore
            {
                string Name { get; }

                string Load(int id);
            }

            public class FileStore : IStore
            {
                public string Name => "file";

                public string Load(int id) { return "file"; }
            }

            public class MemoryStore : IStore
            {
                public string Name { get { return "memory"; } }

                public string Load(int id) { return "memory"; }
            }

            public class Reader
            {
                public string Read(IStore store) { return store.Load(1) + store.Name; }
            }
            """);

        AssertEdges(
            map,
            ("M:N.Reader.Read(N.IStore)", "M:N.FileStore.get_Name", EdgeKind.Implements),
            ("M:N.Reader.Read(N.IStore)", "M:N.FileStore.Load(System.Int32)", EdgeKind.Implements),
            ("M:N.Reader.Read(N.IStore)", "M:N.MemoryStore.get_Name", EdgeKind.Implements),
            ("M:N.Reader.Read(N.IStore)", "M:N.MemoryStore.Load(System.Int32)", EdgeKind.Implements));
    }

    [Fact]
    public async Task Virtual_calls_reach_the_method_and_every_override_and_abstract_calls_reach_the_overrides()
    {
        var map = await Inline.MapAsync("""
            namespace N;

            public abstract class Shape
            {
                public abstract double Area();

                public virtual string Describe() { return "shape"; }
            }

            public class Circle : Shape
            {
                public override double Area() { return 3.14; }

                public override string Describe() { return "circle"; }
            }

            public class Square : Shape
            {
                public override double Area() { return 1; }
            }

            public sealed class Printer
            {
                public string Print(Shape shape) { return shape.Describe() + shape.Area(); }
            }
            """);

        AssertEdges(
            map,
            ("M:N.Printer.Print(N.Shape)", "M:N.Circle.Area", EdgeKind.Overrides),
            ("M:N.Printer.Print(N.Shape)", "M:N.Circle.Describe", EdgeKind.Overrides),
            ("M:N.Printer.Print(N.Shape)", "M:N.Shape.Describe", EdgeKind.Call),
            ("M:N.Printer.Print(N.Shape)", "M:N.Square.Area", EdgeKind.Overrides));
    }

    [Fact]
    public async Task DI_registrations_bind_interface_calls_to_the_registered_implementation_only()
    {
        var map = await Inline.MapAsync(
            [
                ("stubs/DependencyInjection.cs", DependencyInjectionStubs),
                ("src/App.cs", """
                    using Microsoft.Extensions.DependencyInjection;
                    using Microsoft.Extensions.DependencyInjection.Extensions;

                    namespace N;

                    public interface IClock { string Now(); }

                    public interface IStore<T> { T Load(int id); }

                    public interface IRepository<T> { T Find(int id); }

                    public interface IAudit { void Write(); }

                    public class SystemClock : IClock { public string Now() { return "now"; } }

                    public class FakeClock : IClock { public string Now() { return "fake"; } }

                    public class QuoteStore : IStore<string> { public string Load(int id) { return "quote"; } }

                    public class OtherStore : IStore<string> { public string Load(int id) { return "other"; } }

                    public class Repository<T> : IRepository<T> { public T Find(int id) { return default!; } }

                    public class CachedRepository<T> : IRepository<T> { public T Find(int id) { return default!; } }

                    public class DbAudit : IAudit { public void Write() { } }

                    public class NullAudit : IAudit { public void Write() { } }

                    public static class Setup
                    {
                        public static void Register(IServiceCollection services)
                        {
                            services.AddScoped<IClock, SystemClock>();
                            services.AddSingleton(typeof(IStore<string>), typeof(QuoteStore));
                            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                            services.TryAddTransient<IAudit, DbAudit>();
                        }
                    }

                    public class User
                    {
                        public string Run(IClock clock, IStore<string> store, IRepository<int> repository, IAudit audit)
                        {
                            audit.Write();
                            return clock.Now() + store.Load(1) + repository.Find(2);
                        }
                    }
                    """),
            ],
            included: ["src/App.cs"]);

        AssertEdges(
            map.Edges.Where(edge => edge.From.StartsWith("M:N.User.", StringComparison.Ordinal)),
            ("M:N.User.Run(N.IClock,N.IStore{System.String},N.IRepository{System.Int32},N.IAudit)", "M:N.DbAudit.Write", EdgeKind.Bound),
            ("M:N.User.Run(N.IClock,N.IStore{System.String},N.IRepository{System.Int32},N.IAudit)", "M:N.QuoteStore.Load(System.Int32)", EdgeKind.Bound),
            ("M:N.User.Run(N.IClock,N.IStore{System.String},N.IRepository{System.Int32},N.IAudit)", "M:N.Repository`1.Find(System.Int32)", EdgeKind.Bound),
            ("M:N.User.Run(N.IClock,N.IStore{System.String},N.IRepository{System.Int32},N.IAudit)", "M:N.SystemClock.Now", EdgeKind.Bound));
    }

    private static void AssertEdges(CodeMap map, params (string From, string To, EdgeKind Kind)[] expected) => AssertEdges(map.Edges, expected);

    private static void AssertEdges(IEnumerable<Edge> edges, params (string From, string To, EdgeKind Kind)[] expected)
    {
        Assert.Equal(Sorted(expected.Select(edge => new Edge(edge.From, edge.To, edge.Kind))), Sorted(edges));
    }

    private static IEnumerable<Edge> Sorted(IEnumerable<Edge> edges) =>
        edges.OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ThenBy(edge => edge.Kind);
}
