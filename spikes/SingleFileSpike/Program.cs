using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Data.Sqlite;

var processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
var total = Stopwatch.StartNew();
Console.WriteLine($"startup-to-first-line ms: {(DateTime.UtcNow - processStart).TotalMilliseconds:F0}");

var sqliteOk = RunSqlite();
MSBuildLocator.RegisterDefaults();
var roslynOk = await RunRoslynAsync(args[0]);

Console.WriteLine($"total ms: {total.ElapsedMilliseconds}");
Console.WriteLine($"sqlite: {(sqliteOk ? "ok" : "failed")}, roslyn: {(roslynOk ? "ok" : "failed")}");
return sqliteOk && roslynOk ? 0 : 1;

static bool RunSqlite()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"spike-{Guid.NewGuid():N}.db");
    var rows = 0;
    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = "create table notes(id integer primary key, body text not null)";
        create.ExecuteNonQuery();
        using var insert = connection.CreateCommand();
        insert.CommandText = "insert into notes(body) values ('hello from sqlite')";
        insert.ExecuteNonQuery();
        using var select = connection.CreateCommand();
        select.CommandText = "select id, body from notes";
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"sqlite row: {reader.GetInt64(0)} {reader.GetString(1)}");
            rows++;
        }
    }

    SqliteConnection.ClearAllPools();
    File.Delete(dbPath);
    return rows == 1;
}

static async Task<bool> RunRoslynAsync(string solutionPath)
{
    using var workspace = MSBuildWorkspace.Create();
    var solution = await workspace.OpenSolutionAsync(solutionPath);
    foreach (var diagnostic in workspace.Diagnostics)
    {
        Console.WriteLine($"workspace {diagnostic.Kind}: {diagnostic.Message}");
    }

    foreach (var project in solution.Projects)
    {
        var compilation = await project.GetCompilationAsync();
        var controller = compilation?.GetTypeByMetadataName("MixedRepo.Api.Controllers.QuotesController");
        var method = controller?.GetMembers("ListQuotes").OfType<IMethodSymbol>().FirstOrDefault();
        if (compilation is null || method is null)
        {
            continue;
        }

        var declaration = await method.DeclaringSyntaxReferences[0].GetSyntaxAsync();
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol callee)
            {
                Console.WriteLine($"unresolved: {invocation}");
                continue;
            }

            Console.WriteLine($"{controller!.Name}.{method.Name} calls {callee.ContainingType.ToDisplayString()}.{callee.Name}");
            if (callee.ContainingType.Name == "IQuoteService")
            {
                return true;
            }
        }
    }

    return false;
}
