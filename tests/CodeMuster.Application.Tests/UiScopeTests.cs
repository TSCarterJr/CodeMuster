namespace CodeMuster.Application.Tests;

public class UiScopeTests
{
    [Theory]
    [InlineData("web/pages/invoice.tsx")]
    [InlineData("web/components/Payment.jsx")]
    [InlineData("web/Invoice.vue")]
    [InlineData("web/Invoice.svelte")]
    [InlineData("web/Invoice.astro")]
    [InlineData("Views/Invoice.cshtml")]
    [InlineData("Components/Invoice.razor")]
    [InlineData("web/invoice.component.ts")]
    [InlineData("web/invoice.html")]
    [InlineData("web/invoice.htm")]
    [InlineData("web/theme.css")]
    [InlineData("web/theme.scss")]
    [InlineData("web/theme.sass")]
    [InlineData("web/theme.less")]
    public void RecognizesSupportedUiSourceConventions(string path)
    {
        Assert.True(UiScope.Applies(path));
    }

    [Theory]
    [InlineData("backend/UI/InvoiceService.cs")]
    [InlineData("web/api/invoice.ts")]
    [InlineData("web/services/invoice.js")]
    [InlineData("web/types/invoice.d.ts")]
    [InlineData("cli/Program.cs")]
    [InlineData("web/package.json")]
    [InlineData("web/README.md")]
    [InlineData("web/Invoice.test.tsx")]
    [InlineData("web/Invoice.spec.jsx")]
    [InlineData("web/__tests__/Invoice.tsx")]
    [InlineData("tests/invoice.html")]
    public void DoesNotTreatBackendOrTestCodeAsUi(string path)
    {
        Assert.False(UiScope.Applies(path));
    }

    [Fact]
    public void IncludesAreAdditiveAndExplicitExcludesAlwaysWin()
    {
        Assert.True(UiScope.Applies("web/legacy/render.js", ["web/legacy/**"]));
        Assert.True(UiScope.Applies("web/Invoice.tsx", ["web/legacy/**"]));
        Assert.False(UiScope.Applies("web/legacy/server.js", ["web/legacy/**"], ["**/server.js"]));
        Assert.False(UiScope.Applies("web/Invoice.tsx", exclude: ["web/**"]));
    }

    [Fact]
    public void ExplicitIncludeCanOptInUnusualUiFixtures()
    {
        Assert.True(UiScope.Applies("tests/browser-fixture/page.html", ["tests/browser-fixture/**"]));
    }

    [Fact]
    public void PathsNormalizeDeduplicateAndOrderOnlyApplicablePaths()
    {
        var paths = UiScope.Paths(["web\\Z.tsx", "./web/A.vue", "web/Z.tsx", "backend/UI/Service.cs"]);

        Assert.Equal(["web/A.vue", "web/Z.tsx"], paths);
    }
}
