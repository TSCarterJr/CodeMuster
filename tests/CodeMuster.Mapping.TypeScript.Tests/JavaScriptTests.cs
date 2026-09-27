using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class JavaScriptTests
{
    [Fact]
    public async Task A_javascript_repository_without_any_config_maps_to_the_express_golden()
    {
        using var temp = TestPaths.ExpressJsWithTypeScript();

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None);

        GoldenAssert.Matches(map, "express-js");
    }

    [Fact]
    public async Task Express_routes_are_http_entry_points_under_their_mount_prefix_with_brace_parameters()
    {
        using var temp = TestPaths.ExpressJsWithTypeScript();

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None);

        Assert.Equal(
            [
                "http GET /api/users <- routes/users.js#GET /",
                "http GET /api/users/{id} <- routes/users.js#getUser",
                "http GET /health <- server.js#GET /health",
                "page /users <- web/pages/users/index.jsx#UsersPage",
                "page /users/[id] <- web/pages/users/[id].jsx#UserPage",
            ],
            map.EntryPoints.Select(entry => $"{entry.Kind} {entry.Display} <- {entry.SymbolId}").Order(StringComparer.Ordinal));
        var handler = Assert.Single(map.Symbols, symbol => symbol.Id == "routes/users.js#GET /");
        Assert.Equal("router.get('/', async (req, res) =>", handler.Signature);
        Assert.Equal(new LineRange(6, 8), handler.Range);
        Assert.Contains(new Edge("routes/users.js#GET /", "services/users.js#listUsers", EdgeKind.Call), map.Edges);
        Assert.Contains(new Edge("web/pages/users/index.jsx#UsersPage", "web/lib/api.js#loadUsers", EdgeKind.Call), map.Edges);
        Assert.Contains(map.HttpCalls, call => call is { From: "web/lib/api.js#loadUser", Method: "GET", Url: "/api/users/{id}" });
    }

    [Fact]
    public async Task A_jsconfig_json_chooses_the_files_like_a_tsconfig_and_allows_javascript()
    {
        using var temp = new TempFolder();
        temp.Copy(TestPaths.TypeScriptPackage, "node_modules/typescript");
        temp.Write("jsconfig.json", """{ "compilerOptions": { "jsx": "preserve" }, "include": ["src"] }""");
        temp.Write("src/app.js", "export function start() {\n  return helper();\n}\n\nfunction helper() {\n  return 1;\n}\n");
        temp.Write("scripts/build.js", "function build() {\n  return 2;\n}\n");

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None);

        Assert.Equal(["src/app.js#helper", "src/app.js#start"], map.Symbols.Select(symbol => symbol.Id));
        Assert.Equal([new Edge("src/app.js#start", "src/app.js#helper", EdgeKind.Call)], map.Edges);
        Assert.Empty(map.Diagnostics);
    }

    [Fact]
    public async Task Express_koa_and_fastify_routes_in_typescript_are_http_entry_points()
    {
        using var temp = new TempFolder();
        temp.Copy(TestPaths.TypeScriptPackage, "node_modules/typescript");
        temp.Write("tsconfig.json", """{ "compilerOptions": { "strict": true, "noEmit": true, "module": "esnext", "moduleResolution": "bundler", "target": "es2022" } }""");
        temp.Write("src/express.ts", """
            import express, { Router } from "express";

            const app = express();
            const v1 = Router();

            export function removeOrder(): void {}

            v1.all("/orders/:id?", removeOrder);
            v1.delete("/orders/:id", removeOrder);
            v1.use("/files/*rest", function (req: unknown) {
              return req;
            });
            app.use("/v1/", v1);
            app.post("/login", (req: unknown) => req);
            app.get("/", (req: unknown) => req);
            app.get(`/dynamic/${Date.now()}`, (req: unknown) => req);
            app.use((req: unknown) => req);
            """ + "\n");
        temp.Write("src/koa.ts", """
            import Router from "@koa/router";

            const router = new Router();

            router.post("create-order", "/orders", async (ctx: unknown) => ctx);
            """ + "\n");
        temp.Write("src/fastify.ts", """
            import Fastify from "fastify";

            const server = Fastify();

            export async function health() {
              return { ok: true };
            }

            server.put("/orders/:id", { schema: {} }, health);
            server.get("/health", health);
            """ + "\n");

        var map = await new TypeScriptMapper(TestPaths.Node).MapAsync(temp.Root, TestPaths.RepoPaths(temp.Root), null, CancellationToken.None);

        Assert.Equal(
            [
                "ANY /v1/files/{*rest} <- src/express.ts#ANY /files/*rest",
                "ANY /v1/orders/{id?} <- src/express.ts#removeOrder",
                "DELETE /v1/orders/{id} <- src/express.ts#removeOrder",
                "GET / <- src/express.ts#GET /",
                "GET /health <- src/fastify.ts#health",
                "POST /login <- src/express.ts#POST /login",
                "POST /orders <- src/koa.ts#POST /orders",
                "PUT /orders/{id} <- src/fastify.ts#health",
            ],
            map.EntryPoints.Where(entry => entry.Kind == "http").Select(entry => $"{entry.Display} <- {entry.SymbolId}").Order(StringComparer.Ordinal));
        Assert.DoesNotContain(map.Symbols, symbol => symbol.Id.Contains("/dynamic", StringComparison.Ordinal));
    }
}
