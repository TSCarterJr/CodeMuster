using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class CompositeMapperTests
{
    private const string Root = "/repos/mixed-repo";

    private static Task<CompositeMap> MapAsync(params ICodeMapper[] mappers) =>
        CompositeMapper.MapAsync(mappers, Root, MixedRepo.Included(), null, CancellationToken.None);

    [Fact]
    public async Task TwoMappers_MergeIntoOneMap_InMapperOrder()
    {
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp() with
        {
            Resolution = new ResolutionStats(17, 2, ["GetService", "Configure"]),
            Diagnostics = ["csharp: one project skipped"],
        });
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript() with
        {
            Resolution = new ResolutionStats(11, 1, ["fetch", "GetService"]),
            Diagnostics = ["typescript: no jsx factory"],
        });

        var mapped = await MapAsync(csharp, typescript);

        Assert.Equal(csharp.Map.Symbols.Concat(typescript.Map.Symbols), mapped.Map.Symbols);
        Assert.Equal(csharp.Map.Edges.Concat(typescript.Map.Edges), mapped.Map.Edges);
        Assert.Equal(csharp.Map.EntryPoints.Concat(typescript.Map.EntryPoints), mapped.Map.EntryPoints);
        Assert.Equal(new[] { "csharp: one project skipped", "typescript: no jsx factory" }, mapped.Map.Diagnostics);
        Assert.Equal(28, mapped.Map.Resolution.Resolved);
        Assert.Equal(3, mapped.Map.Resolution.Unresolved);
        Assert.Equal(new[] { "GetService", "fetch", "Configure" }, mapped.Map.Resolution.TopUnresolvedNames);
        Assert.Empty(mapped.FailedLanguages);

        var paths = MixedRepo.Included().Select(f => f.Path).ToList();
        Assert.All(new[] { csharp, typescript }, mapper =>
        {
            var call = Assert.Single(mapper.Calls);
            Assert.Equal(Root, call.RepoRoot);
            Assert.Equal(paths, call.Paths);
        });
    }

    [Fact]
    public async Task HttpCalls_OfEveryMapper_AreMergedInMapperOrder()
    {
        HttpCall[] csharpCalls = [new(MixedRepo.ControllerListQuotes, "GET", "/health", "\"/health\"", MixedRepo.ControllerPath, 11)];
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp() with { HttpCalls = csharpCalls });
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript() with { HttpCalls = MixedRepo.HttpCalls() });

        var mapped = await MapAsync(csharp, typescript);

        Assert.Equal(csharpCalls.Concat(MixedRepo.HttpCalls()), mapped.Map.HttpCalls);
    }

    [Fact]
    public async Task MapperWhoseLanguageHasNoIncludedFile_IsNotRun()
    {
        var go = new FakeCodeMapper(Languages.Go, MixedRepo.CSharp());
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript());

        var mapped = await MapAsync(go, typescript);

        Assert.Empty(go.Calls);
        Assert.Single(typescript.Calls);
        Assert.Equal(typescript.Map.Symbols, mapped.Map.Symbols);
    }

    [Fact]
    public async Task MapperCoveringSeveralLanguages_RunsForAnyOfThem_AndReportsOnlyThePresentOnesMapped()
    {
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript()) { Languages = [Languages.TypeScript, Languages.JavaScript] };
        FileRecord[] javascriptOnly = [.. MixedRepo.Included().Take(1).Select(file => file with { Path = "server.js", Language = Languages.JavaScript })];
        var both = javascriptOnly.Append(MixedRepo.Included().First() with { Path = "web/lib/api.ts", Language = Languages.TypeScript }).ToList();

        var javascript = await CompositeMapper.MapAsync([typescript], Root, javascriptOnly, null, CancellationToken.None);
        var mixed = await CompositeMapper.MapAsync([typescript], Root, both, null, CancellationToken.None);
        var tsOnly = await MapAsync(typescript);

        Assert.Equal(3, typescript.Calls.Count);
        Assert.Equal(new[] { Languages.JavaScript }, javascript.MappedLanguages);
        Assert.Equal(new[] { Languages.TypeScript, Languages.JavaScript }, mixed.MappedLanguages);
        Assert.Equal(new[] { Languages.TypeScript }, tsOnly.MappedLanguages);
    }

    [Fact]
    public async Task MapperCoveringSeveralLanguages_ThatThrows_MarksEachPresentLanguageFailed_UnderItsOwnName()
    {
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript())
        {
            Languages = [Languages.TypeScript, Languages.JavaScript],
            Throws = new InvalidOperationException("typescript was not found"),
        };
        FileRecord[] javascriptOnly = [.. MixedRepo.Included().Take(1).Select(file => file with { Path = "server.js", Language = Languages.JavaScript })];

        var mapped = await CompositeMapper.MapAsync([typescript], Root, javascriptOnly, null, CancellationToken.None);

        Assert.Equal(new[] { Languages.JavaScript }, mapped.FailedLanguages);
        Assert.Empty(mapped.MappedLanguages);
        Assert.Equal(new[] { "typescript mapper failed: typescript was not found" }, mapped.Map.Diagnostics);
    }

    [Fact]
    public async Task ThrowingMapper_AddsADiagnostic_AndMarksOnlyItsLanguageFailed()
    {
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp()) { Throws = new InvalidOperationException("no .NET SDK found") };
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript());

        var mapped = await MapAsync(csharp, typescript);

        Assert.Equal(new[] { Languages.CSharp }, mapped.FailedLanguages);
        Assert.Equal(new[] { "csharp mapper failed: no .NET SDK found" }, mapped.Map.Diagnostics);
        Assert.Equal(typescript.Map.Symbols, mapped.Map.Symbols);
        Assert.Equal(typescript.Map.Edges, mapped.Map.Edges);
        Assert.Equal(typescript.Map.EntryPoints, mapped.Map.EntryPoints);
        Assert.Equal(11, mapped.Map.Resolution.Resolved);
    }

    [Fact]
    public async Task MergedUnresolvedNames_TakeTurnsByRank_AndKeepAtMostTwenty()
    {
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp() with
        {
            Resolution = new ResolutionStats(1, 15, Enumerable.Range(0, 15).Select(i => "cs" + i).ToList()),
        });
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript() with
        {
            Resolution = new ResolutionStats(1, 15, Enumerable.Range(0, 15).Select(i => "ts" + i).ToList()),
        });

        var mapped = await MapAsync(csharp, typescript);

        var names = mapped.Map.Resolution.TopUnresolvedNames;
        Assert.Equal(20, names.Count);
        Assert.Equal(new[] { "cs0", "ts0", "cs1", "ts1" }, names.Take(4));
        Assert.Equal(new[] { "cs9", "ts9" }, names.TakeLast(2));
    }

    [Fact]
    public async Task ResolutionRate_IsTheResolvedShareOfEveryCallSite_WithTheMergedNames()
    {
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp() with { Resolution = new ResolutionStats(17, 3, ["GetService"]) });
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript() with { Resolution = new ResolutionStats(11, 1, []) });

        var mapped = await MapAsync(csharp, typescript);

        Assert.Equal(0.875, mapped.ResolutionRate);
        Assert.Equal(new[] { "GetService" }, mapped.TopUnresolvedNames);
        Assert.Equal(new[] { Languages.CSharp, Languages.TypeScript }, mapped.MappedLanguages);
    }

    [Fact]
    public async Task ResolutionRate_IsOne_WhenTheMappersFoundNoCallSites()
    {
        var typescript = new FakeCodeMapper(Languages.TypeScript, MixedRepo.TypeScript() with { Resolution = new ResolutionStats(0, 0, []) });

        var mapped = await MapAsync(typescript);

        Assert.Equal(1.0, mapped.ResolutionRate);
        Assert.Empty(mapped.TopUnresolvedNames!);
    }

    [Fact]
    public async Task ResolutionRateAndNames_AreNull_WhenNoMapperReturnedAMap()
    {
        var go = new FakeCodeMapper(Languages.Go, MixedRepo.CSharp());
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp()) { Throws = new InvalidOperationException("no .NET SDK found") };

        var none = await MapAsync(go);
        var failed = await MapAsync(csharp);

        Assert.Null(none.ResolutionRate);
        Assert.Null(none.TopUnresolvedNames);
        Assert.Empty(none.MappedLanguages);
        Assert.Null(failed.ResolutionRate);
        Assert.Null(failed.TopUnresolvedNames);
        Assert.Empty(failed.MappedLanguages);
    }

    [Fact]
    public async Task Cancellation_Propagates_InsteadOfBeingRecordedAsAMapperFailure()
    {
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp()) { Throws = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CompositeMapper.MapAsync([csharp], Root, MixedRepo.Included(), null, new CancellationToken(canceled: true)));
    }
}
