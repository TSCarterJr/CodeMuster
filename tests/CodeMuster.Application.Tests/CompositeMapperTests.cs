using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class CompositeMapperTests
{
    private const string Root = "/repos/mixed-repo";

    private static Task<CompositeMap> MapAsync(params ICodeMapper[] mappers) =>
        CompositeMapper.MapAsync(mappers, Root, MixedRepo.Included(), CancellationToken.None);

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
    public async Task Cancellation_Propagates_InsteadOfBeingRecordedAsAMapperFailure()
    {
        var csharp = new FakeCodeMapper(Languages.CSharp, MixedRepo.CSharp()) { Throws = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CompositeMapper.MapAsync([csharp], Root, MixedRepo.Included(), new CancellationToken(canceled: true)));
    }
}
