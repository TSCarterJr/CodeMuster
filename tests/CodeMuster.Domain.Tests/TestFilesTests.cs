using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class TestFilesTests
{
    [Theory]
    [InlineData("web/src/app/page.test.tsx")]
    [InlineData("web/src/lib/api.spec.ts")]
    [InlineData("scripts/check.test.mjs")]
    [InlineData("pkg/server_test.go")]
    [InlineData("tools/test_parser.py")]
    [InlineData("src/Api.Tests/QuoteServiceTests.cs")]
    [InlineData("src/Api/QuoteServiceTest.cs")]
    [InlineData("tests/helpers/login.ts")]
    [InlineData("src/Test/Builder.cs")]
    [InlineData("web/src/__tests__/render.tsx")]
    [InlineData("web/spec/support/env.js")]
    [InlineData("web/specs/checkout.js")]
    [InlineData("web/e2e/journey.ts")]
    [InlineData("web/e2e-dev/helpers/login.ts")]
    public void TestPaths_AreTests(string path) => Assert.True(TestFiles.IsTestPath(path));

    [Theory]
    [InlineData("src/Api/Contest.cs")]
    [InlineData("src/Api/QuoteService.cs")]
    [InlineData("web/src/lib/testing-library.ts")]
    [InlineData("web/src/app/latest/page.tsx")]
    [InlineData("web/src/attest.ts")]
    [InlineData("tools/parser_test_utils.py")]
    [InlineData("web/e2ebuilder/index.ts")]
    public void ProductPaths_AreNotTests(string path) => Assert.False(TestFiles.IsTestPath(path));

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" /></ItemGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3" /></ItemGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="NUnit" /></ItemGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="MSTest.TestFramework" /></ItemGroup></Project>""")]
    [InlineData("""<Project Sdk="MSTest.Sdk/3.6.1"></Project>""")]
    public void TestProjects_AreRecognized(string xml) => Assert.True(TestFiles.IsTestProject(xml));

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><PackageReference Include="Npgsql" /></ItemGroup></Project>""")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include="xunit.abstractions" /></ItemGroup></Project>""")]
    [InlineData("not xml at all")]
    public void OtherProjects_AreNot(string xml) => Assert.False(TestFiles.IsTestProject(xml));
}
