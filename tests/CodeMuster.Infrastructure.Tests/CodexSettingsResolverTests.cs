using System.Diagnostics;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class CodexSettingsResolverTests
{
    [Fact]
    public async Task ExplicitSettings_DoNotStartCodex()
    {
        var identity = new AgentIdentity("codex", "explicit-model", "xhigh");
        Assert.Equal(identity, await CodexSettingsResolver.ResolveAsync(identity, "unused", CancellationToken.None));
    }

    [Theory]
    [InlineData(null, null, "configured-model", "high")]
    [InlineData("override", null, "override", "high")]
    [InlineData(null, "low", "configured-model", "low")]
    public async Task EffectiveConfig_IsUsedWithExplicitOverrides(string? model, string? effort, string expectedModel, string expectedEffort)
    {
        using var fixture = new Server("""
            if (request.method === 'config/read') {
              if (fs.realpathSync(request.params.cwd) !== fs.realpathSync(process.cwd()) || request.params.includeLayers !== false) process.exit(2);
              reply({config:{model:'configured-model',model_reasoning_effort:'high'}});
            } else throw new Error('model catalog not needed');
            """);
        var identity = await fixture.ResolveAsync(model, effort);
        Assert.Equal(new AgentIdentity("codex", expectedModel, expectedEffort), identity);
        fixture.AssertStopped();
        var adapter = new CodexAdapter("unused", identity.Model, identity.Effort, write: true);
        Assert.Equal(identity, adapter.Identity);
        Assert.Contains(expectedModel, adapter.Arguments);
        Assert.Contains("model_reasoning_effort=\"" + expectedEffort + "\"", adapter.Arguments);
    }

    [Fact]
    public async Task Catalog_ResolvesModelDefaultAndReasoningWithoutHardcoding()
    {
        using var fixture = new Server("""
            if (request.method === 'config/read') reply({config:{model:null,model_reasoning_effort:null}});
            else if (request.method === 'model/list') reply({data:[{model:'provider-model',isDefault:true,defaultReasoningEffort:'medium'}],nextCursor:null});
            """);
        Assert.Equal(new AgentIdentity("codex", "provider-model", "medium"), await fixture.ResolveAsync());
        fixture.AssertStopped();
    }

    [Fact]
    public async Task Catalog_PaginatesForRequestedModelsReasoning()
    {
        using var fixture = new Server("""
            if (request.method === 'config/read') reply({config:{model:'configured-model'}});
            else if (!request.params.cursor) reply({data:[{model:'other',isDefault:true,defaultReasoningEffort:'low'}],nextCursor:'page2'});
            else reply({data:[{model:'override',defaultReasoningEffort:'xhigh'}],nextCursor:null});
            """);
        Assert.Equal(new AgentIdentity("codex", "override", "xhigh"), await fixture.ResolveAsync("override"));
    }

    [Theory]
    [InlineData("reply({config:{}});", false)]
    [InlineData("process.stdout.write(JSON.stringify({id:request.id,error:{message:'SECRET'}})+'\\n');", false)]
    [InlineData("process.stdout.write('bad json\\n');", false)]
    [InlineData("process.exit(4);", false)]
    [InlineData("process.stderr.write('x'.repeat(100000));", true)]
    public async Task MissingUnsupportedMalformedExitedOrTimedOut_FailsActionablyWithoutLeakingOutput(string response, bool timeout)
    {
        using var fixture = new Server(response);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ResolveAsync(timeout: timeout ? TimeSpan.FromMilliseconds(700) : null));
        Assert.Contains("--model", error.Message);
        Assert.Contains("--effort", error.Message);
        Assert.DoesNotContain("SECRET", error.Message);
        fixture.AssertStopped(allowNotStarted: timeout);
    }

    [Fact]
    public async Task Cancellation_StopsLookupAndPropagates()
    {
        using var fixture = new Server("fs.writeFileSync('ready', '1');");
        using var cancellation = new CancellationTokenSource();
        var lookup = fixture.ResolveAsync(cancellationToken: cancellation.Token);
        await fixture.WaitForRequestAsync(lookup);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
        fixture.AssertStopped();
    }

    private sealed class Server : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "codemuster-settings-" + Guid.NewGuid().ToString("N"));
        private readonly string script;

        public Server(string response)
        {
            Directory.CreateDirectory(root);
            script = Path.Combine(root, "server.cjs");
            File.WriteAllText(script, """
                const fs = require('node:fs');
                fs.writeFileSync('pid', String(process.pid));
                const lines = require('node:readline').createInterface({input:process.stdin});
                let initialized = false;
                lines.on('line', line => {
                  const request = JSON.parse(line);
                  const reply = result => process.stdout.write(JSON.stringify({id:request.id,result})+'\n');
                  if (request.method === 'initialize') { reply({}); return; }
                  if (request.method === 'initialized') { initialized = true; return; }
                  if (!initialized || !['config/read','model/list'].includes(request.method)) process.exit(3);
                """ + response + "\n});\n");
        }

        public Task<AgentIdentity> ResolveAsync(string? model = null, string? effort = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            CodexSettingsResolver.ResolveAsync(new AgentIdentity("codex", model, effort), root, "node", [script], timeout ?? TimeSpan.FromSeconds(30), cancellationToken);

        public async Task WaitForRequestAsync(Task lookup)
        {
            while (!File.Exists(Path.Combine(root, "ready")))
            {
                if (lookup.IsCompleted) await lookup;
                await Task.Delay(20);
            }
        }

        public void AssertStopped(bool allowNotStarted = false)
        {
            var path = Path.Combine(root, "pid");
            if (allowNotStarted && !File.Exists(path)) return;
            var pid = int.Parse(File.ReadAllText(path), System.Globalization.CultureInfo.InvariantCulture);
            try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
            catch (ArgumentException) { }
        }

        public void Dispose()
        {
            WaitForServerExit();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private void WaitForServerExit()
        {
            var path = Path.Combine(root, "pid");
            if (!File.Exists(path)) return;
            try
            {
                using var server = Process.GetProcessById(int.Parse(File.ReadAllText(path), System.Globalization.CultureInfo.InvariantCulture));
                server.WaitForExit(milliseconds: 10_000);
            }
            catch (ArgumentException) { }
        }
    }
}
