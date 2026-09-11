using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class EntryPointTests
{
    private const string FrameworkStubs = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase
            {
            }

            public sealed class ApiControllerAttribute : Attribute
            {
            }

            public sealed class ControllerAttribute : Attribute
            {
            }

            public sealed class NonActionAttribute : Attribute
            {
            }

            public sealed class RouteAttribute(string template) : Attribute
            {
                public string Template => template;
            }

            public sealed class HttpGetAttribute : Routing.HttpMethodAttribute
            {
                public HttpGetAttribute()
                {
                }

                public HttpGetAttribute(string template)
                {
                }
            }

            public sealed class HttpPostAttribute(string template) : Routing.HttpMethodAttribute
            {
                public string Template => template;
            }

            public sealed class HttpDeleteAttribute(string template) : Routing.HttpMethodAttribute
            {
                public string Template => template;
            }
        }

        namespace Microsoft.AspNetCore.Mvc.Routing
        {
            public abstract class HttpMethodAttribute : Attribute
            {
            }
        }

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder
            {
            }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static object MapGet(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => handler;

                public static object MapDelete(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => handler;
            }
        }

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostedService
            {
                Task StartAsync(CancellationToken cancellationToken);

                Task StopAsync(CancellationToken cancellationToken);
            }

            public abstract class BackgroundService : IHostedService
            {
                protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

                public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public async Task Controller_actions_combine_class_and_method_routes()
    {
        var map = await MapWithStubsAsync("""
            using Microsoft.AspNetCore.Mvc;

            namespace N;

            [Route("api/[controller]")]
            public class OrdersController : ControllerBase
            {
                [HttpGet]
                public string List() { return "all"; }

                [HttpPost("{id}")]
                public string Update(int id) { return "updated"; }

                [HttpGet("/health")]
                public string Health() { return "ok"; }

                [HttpDelete("~/orders/[action]/{id}")]
                public string Remove(int id) { return "removed"; }

                [Route("search")]
                public string Search() { return "found"; }

                [NonAction]
                public string Helper() { return "helper"; }

                public static string Build() { return "static"; }

                private string Hidden() { return "hidden"; }
            }

            [Controller]
            public class ReportsController
            {
                public string Summary() { return "summary"; }
            }

            public abstract class BaseController : ControllerBase
            {
                public string Shared() { return "shared"; }
            }

            public class PlainService
            {
                public string Work() { return "work"; }
            }
            """);

        AssertEntries(
            map,
            ("M:N.OrdersController.List", "http", "GET /api/Orders"),
            ("M:N.OrdersController.Update(System.Int32)", "http", "POST /api/Orders/{id}"),
            ("M:N.OrdersController.Health", "http", "GET /health"),
            ("M:N.OrdersController.Remove(System.Int32)", "http", "DELETE /orders/Remove/{id}"),
            ("M:N.OrdersController.Search", "http", "ANY /api/Orders/search"),
            ("M:N.ReportsController.Summary", "http", "ANY /Reports/Summary"));
    }

    [Fact]
    public async Task Background_services_enter_at_ExecuteAsync_and_hosted_services_at_StartAsync()
    {
        var map = await MapWithStubsAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Hosting;

            namespace N;

            public class Worker : BackgroundService
            {
                protected override Task ExecuteAsync(CancellationToken stoppingToken) { return Task.CompletedTask; }
            }

            public abstract class BaseWorker : BackgroundService
            {
                protected override Task ExecuteAsync(CancellationToken stoppingToken) { return Task.CompletedTask; }
            }

            public class Pump : IHostedService
            {
                public Task StartAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }

                public Task StopAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            }
            """);

        AssertEntries(
            map,
            ("M:N.Worker.ExecuteAsync(System.Threading.CancellationToken)", "background", "Worker"),
            ("M:N.Pump.StartAsync(System.Threading.CancellationToken)", "background", "Pump"));
    }

    [Fact]
    public async Task Minimal_api_maps_with_method_group_handlers_are_entries_and_lambda_handlers_are_not()
    {
        var map = await MapWithStubsAsync("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;

            namespace N;

            public static class Api
            {
                public static void Map(IEndpointRouteBuilder app)
                {
                    app.MapGet("items", Items.List);
                    app.MapDelete("/items/{id}", (int id) => "gone");
                }
            }

            public static class Items
            {
                public static string List() { return "items"; }
            }
            """);

        AssertEntries(map, ("M:N.Items.List", "http", "GET /items"));
    }

    private static Task<CodeMap> MapWithStubsAsync(string source) =>
        Inline.MapAsync([("stubs/Framework.cs", FrameworkStubs), ("src/App.cs", source)], included: ["src/App.cs"]);

    private static void AssertEntries(CodeMap map, params (string SymbolId, string Kind, string Display)[] expected)
    {
        Assert.Equal(
            expected.Select(entry => new EntryPoint(entry.SymbolId, entry.Kind, entry.Display)).OrderBy(entry => entry.Display, StringComparer.Ordinal),
            map.EntryPoints.OrderBy(entry => entry.Display, StringComparer.Ordinal));
    }
}
