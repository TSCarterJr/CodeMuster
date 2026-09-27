using CodeMuster.Infrastructure.Audits;

namespace CodeMuster.Infrastructure.Tests;

public class DependencyAuditorConcurrencyTests
{
    private const string Root = "/repo";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);
    private static readonly ProcessResult Clean = new(0, "{\"auditReportVersion\":2,\"vulnerabilities\":{}}", "");
    private static readonly ProcessResult CleanDotnet = new(0, "{\"version\":1,\"projects\":[]}", "");

    private static string Folder(string directory) => Path.GetFileName(directory.TrimEnd('/', '\\'));

    [Fact]
    public async Task NodeAudits_RunAtTheSameTime()
    {
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var auditor = new DependencyAuditor(async (_, _, directory, _) =>
        {
            if (Folder(directory) == "b")
            {
                secondStarted.TrySetResult();
            }
            else
            {
                // Sequential audits would never start b while a waits, so this times out instead of passing.
                await secondStarted.Task.WaitAsync(Guard);
            }

            return Clean;
        });

        var audit = await auditor.AuditAsync(Root, ["a/package.json", "a/package-lock.json", "b/package.json", "b/package-lock.json"], null, CancellationToken.None);

        Assert.Equal(["a/package.json", "b/package.json"], audit.Manifests.Select(m => m.Manifest));
        Assert.Empty(audit.Diagnostics);
    }

    [Fact]
    public async Task NodeAudits_RunAtMostFourAtATime()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fourStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var auditor = new DependencyAuditor(async (_, _, _, _) =>
        {
            if (Interlocked.Increment(ref started) == 4)
            {
                fourStarted.TrySetResult();
            }

            await gate.Task;
            return Clean;
        });
        string[] folders = ["a", "b", "c", "d", "e", "f"];

        var auditing = auditor.AuditAsync(Root, [.. folders.SelectMany(f => new[] { f + "/package.json", f + "/package-lock.json" })], null, CancellationToken.None);
        await fourStarted.Task.WaitAsync(Guard);
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
        }

        Assert.Equal(4, Volatile.Read(ref started));
        gate.SetResult();
        var audit = await auditing.WaitAsync(Guard);

        Assert.Equal(6, started);
        Assert.Equal(folders.Select(f => f + "/package.json"), audit.Manifests.Select(m => m.Manifest));
    }

    [Fact]
    public async Task DotnetAudits_RunOneAfterAnother_AlongsideTheNodeAudits()
    {
        var releaseFirstDotnet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nodeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dotnetStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dotnetCalls = new List<string>();
        var auditor = new DependencyAuditor(async (file, arguments, _, _) =>
        {
            if (file == "npm")
            {
                nodeStarted.TrySetResult();
                await dotnetStarted.Task.WaitAsync(Guard);
                return Clean;
            }

            lock (dotnetCalls)
            {
                dotnetCalls.Add(arguments[1]);
            }

            dotnetStarted.TrySetResult();

            if (arguments[1] == "A.sln")
            {
                await releaseFirstDotnet.Task;
            }

            return CleanDotnet;
        });

        var auditing = auditor.AuditAsync(Root, ["A.sln", "B.sln", "web/package.json", "web/package-lock.json"], null, CancellationToken.None);
        await nodeStarted.Task.WaitAsync(Guard);
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
        }

        lock (dotnetCalls)
        {
            Assert.Equal(["A.sln"], dotnetCalls);
        }

        releaseFirstDotnet.SetResult();
        var audit = await auditing.WaitAsync(Guard);

        Assert.Equal(["A.sln", "B.sln"], dotnetCalls);
        Assert.Equal(["web/package.json", "A.sln", "B.sln"], audit.Manifests.Select(m => m.Manifest));
    }

    [Fact]
    public async Task ManifestsAndWarnings_KeepThePlannedOrder_WhateverOrderTheAuditsFinishIn()
    {
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var auditor = new DependencyAuditor(async (file, _, directory, _) =>
        {
            if (Folder(directory) == "a")
            {
                await releaseA.Task;
                return new ProcessResult(1, "", "a failed");
            }

            if (Folder(directory) == "c")
            {
                releaseA.TrySetResult();
                return new ProcessResult(1, "", "c failed");
            }

            return Clean;
        });

        var audit = await auditor.AuditAsync(
            Root,
            ["a/package.json", "a/package-lock.json", "b/package.json", "b/package-lock.json", "c/package.json", "c/package-lock.json"],
            null,
            CancellationToken.None).WaitAsync(Guard);

        Assert.Equal(["b/package.json"], audit.Manifests.Select(m => m.Manifest));
        Assert.Equal(2, audit.Diagnostics.Count);
        Assert.StartsWith("a/package.json: ", audit.Diagnostics[0]);
        Assert.StartsWith("c/package.json: ", audit.Diagnostics[1]);
    }
}
