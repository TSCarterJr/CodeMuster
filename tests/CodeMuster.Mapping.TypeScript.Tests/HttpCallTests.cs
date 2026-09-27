using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public sealed class HttpProject : IAsyncLifetime
{
    public const string Calls = "site/lib/calls.ts";

    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
    {
        ["site/tsconfig.json"] = """
            {
              "compilerOptions": {
                "strict": true,
                "noEmit": true,
                "moduleResolution": "bundler",
                "module": "esnext",
                "target": "es2022",
                "lib": ["es2022", "dom"]
              }
            }
            """,
        ["site/lib/http.ts"] = """
            import axios from "axios";

            export const api = axios.create({ baseURL: "/api/v2/" });
            export const remote = axios.create({ baseURL: "https://example.com/svc" });
            """,
        ["site/lib/local.ts"] = """
            function fetch(path: string) {
              return path;
            }

            export function notHttp() {
              return fetch("/quotes");
            }
            """,
        [Calls] = """
            import axios from "axios";
            import client from "axios";
            import { api, remote } from "./http";

            const QUOTES = "/quotes";

            export async function literal() {
              return fetch("/customers");
            }

            export async function template(id: number, tenantId: number) {
              return fetch(`/quotes/${id}?tenantId=${tenantId}#top`);
            }

            export async function withMethod(id: number, quote: { id: number }) {
              await fetch(`https://api.example.com:8443/quotes/${quote.id}`, { method: "delete" });
              await fetch("/quotes", { method: "POST", body: "{}" });
              await window.fetch("//cdn.example.com/orders");
            }

            export async function dynamicMethod(init: RequestInit, verb: string) {
              await fetch("/quotes", init);
              await fetch("/quotes", { method: verb });
            }

            export async function runtime(url: string, id: number, ids: number[]) {
              await fetch(url);
              await fetch(new URL("/quotes", location.origin));
              await fetch(QUOTES + "/" + id);
              await fetch(`${QUOTES}/${ids[0]}`);
            }

            export async function helpers(id: number, quote: object) {
              await axios.get(`/quotes/${id}`);
              await axios.post("/quotes", quote);
              await axios.put(`/quotes/${id}`, quote);
              await axios.patch(`/quotes/${id}`, quote);
              await client.delete(`/quotes/${id}`);
            }

            export async function configs(id: number) {
              await axios({ url: `/quotes/${id}`, method: "put" });
              await axios.request({ url: "/quotes" });
              await axios("/customers");
            }

            export async function instances(id: number) {
              await api.get(`/quotes/${id}`);
              await remote.post("/orders");
            }

            export class Store {
              load() {
                return fetch("/customers/");
              }
            }
            """,
    };

    private readonly TempFolder _temp = new();

    public CodeMap Map { get; private set; } = new([], [], [], new ResolutionStats(0, 0, []), []);

    public static int LineOf(string path, string text)
    {
        var lines = Files[path].Split('\n');
        return Array.FindIndex(lines, line => line.Contains(text, StringComparison.Ordinal)) + 1;
    }

    public async Task InitializeAsync()
    {
        _temp.Copy(Path.Combine(TestPaths.MixedRepoWithTypeScript(), "web", "node_modules"), "site/node_modules");
        foreach (var (path, content) in Files)
        {
            _temp.Write(path, content + "\n");
        }

        Map = await MapScript.RunAsync(_temp.Root, ["site/tsconfig.json"], [.. Files.Keys]);
    }

    public Task DisposeAsync()
    {
        _temp.Dispose();
        return Task.CompletedTask;
    }
}

public class HttpCallTests(HttpProject project) : IClassFixture<HttpProject>
{
    private const string Calls = HttpProject.Calls;

    private static HttpCall Call(string function, string method, string? url, string text, string? line = null) =>
        new($"{Calls}#{function}", method, url, text, Calls, HttpProject.LineOf(Calls, line ?? text));

    private List<HttpCall> From(string function) =>
        [.. project.Map.HttpCalls.Where(call => call.From == $"{Calls}#{function}")];

    [Fact]
    public void A_fetch_of_a_string_literal_is_a_get_of_that_path()
    {
        Assert.Equal([Call("literal", "GET", "/customers", "\"/customers\"")], From("literal"));
    }

    [Fact]
    public void A_fetch_template_turns_each_interpolation_into_a_parameter_and_drops_the_query_and_fragment()
    {
        Assert.Equal([Call("template", "GET", "/quotes/{id}", "`/quotes/${id}?tenantId=${tenantId}#top`")], From("template"));
    }

    [Fact]
    public void A_literal_method_in_the_init_object_is_the_method_and_an_origin_is_stripped()
    {
        Assert.Equal(
            [
                Call("withMethod", "DELETE", "/quotes/{quote.id}", "`https://api.example.com:8443/quotes/${quote.id}`"),
                Call("withMethod", "POST", "/quotes", "\"/quotes\"", "method: \"POST\""),
                Call("withMethod", "GET", "/orders", "\"//cdn.example.com/orders\""),
            ],
            From("withMethod"));
    }

    [Fact]
    public void A_method_chosen_at_runtime_is_any()
    {
        Assert.Equal(["ANY", "ANY"], From("dynamicMethod").Select(call => call.Method));
        Assert.All(From("dynamicMethod"), call => Assert.Equal("/quotes", call.Url));
    }

    [Fact]
    public void A_url_built_at_runtime_is_unresolved_with_its_text_and_folds_constants_and_concatenations()
    {
        Assert.Equal(
            [
                Call("runtime", "GET", null, "url", "fetch(url)"),
                Call("runtime", "GET", null, "new URL(\"/quotes\", location.origin)"),
                Call("runtime", "GET", "/quotes/{id}", "QUOTES + \"/\" + id"),
                Call("runtime", "GET", "/quotes/{param}", "`${QUOTES}/${ids[0]}`"),
            ],
            From("runtime"));
    }

    [Fact]
    public void Axios_verb_helpers_name_the_method_including_through_another_import_name()
    {
        Assert.Equal(
            [
                Call("helpers", "GET", "/quotes/{id}", "`/quotes/${id}`", "axios.get("),
                Call("helpers", "POST", "/quotes", "\"/quotes\"", "axios.post("),
                Call("helpers", "PUT", "/quotes/{id}", "`/quotes/${id}`", "axios.put("),
                Call("helpers", "PATCH", "/quotes/{id}", "`/quotes/${id}`", "axios.patch("),
                Call("helpers", "DELETE", "/quotes/{id}", "`/quotes/${id}`", "client.delete("),
            ],
            From("helpers"));
    }

    [Fact]
    public void Axios_config_calls_read_a_literal_url_and_method()
    {
        Assert.Equal(
            [
                Call("configs", "PUT", "/quotes/{id}", "`/quotes/${id}`", "axios({ url"),
                Call("configs", "GET", "/quotes", "\"/quotes\"", "axios.request("),
                Call("configs", "GET", "/customers", "\"/customers\"", "axios(\"/customers\")"),
            ],
            From("configs"));
    }

    [Fact]
    public void Calls_on_an_axios_create_instance_prefix_its_literal_base_url_without_its_origin()
    {
        Assert.Equal(
            [("GET", "/api/v2/quotes/{id}", "`/quotes/${id}`"), ("POST", "/svc/orders", "\"/orders\"")],
            From("instances").Select(call => (call.Method, call.Url, call.Text)));
    }

    [Fact]
    public void A_class_method_is_the_caller_and_a_fetch_that_resolves_to_mapped_code_is_not_http()
    {
        Assert.Equal([Call("Store.load", "GET", "/customers/", "\"/customers/\"")], From("Store.load"));
        Assert.DoesNotContain(project.Map.HttpCalls, call => call.Path == "site/lib/local.ts");
        Assert.Equal(
            ["literal", "template", "withMethod", "dynamicMethod", "runtime", "helpers", "configs", "instances", "Store.load"],
            project.Map.HttpCalls.Select(call => call.From[(Calls.Length + 1)..]).Distinct());
    }

}
