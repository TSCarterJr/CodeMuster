namespace CodeMuster.Infrastructure.Tests;

public sealed class ExecutableResolverTests : IDisposable
{
    private static readonly string[] WindowsExtensions = [".com", ".exe", ".bat", ".cmd"];
    private readonly TempDirectory _dir = new();

    [Fact]
    public void Windows_semantics_resolve_the_cmd_shim_when_only_the_shim_exists()
    {
        var shim = Path.Combine(_dir.Root, "codex.cmd");
        File.WriteAllText(shim, "@echo off\r\n");

        var resolved = ExecutableResolver.Resolve("codex", [_dir.Root], WindowsExtensions, isWindows: true);

        Assert.Equal(shim, resolved);
    }

    [Fact]
    public void Windows_semantics_prefer_the_shim_over_a_bare_file_in_the_same_directory()
    {
        File.WriteAllText(Path.Combine(_dir.Root, "codex"), "#!/bin/sh\n");
        var shim = Path.Combine(_dir.Root, "codex.cmd");
        File.WriteAllText(shim, "@echo off\r\n");

        Assert.Equal(shim, ExecutableResolver.Resolve("codex", [_dir.Root], WindowsExtensions, isWindows: true));
    }

    [Fact]
    public void Windows_semantics_follow_pathext_order()
    {
        File.WriteAllText(Path.Combine(_dir.Root, "claude.cmd"), "@echo off\r\n");
        var exe = Path.Combine(_dir.Root, "claude.exe");
        File.WriteAllText(exe, "MZ");

        Assert.Equal(exe, ExecutableResolver.Resolve("claude", [_dir.Root], WindowsExtensions, isWindows: true));
    }

    [Fact]
    public void Windows_semantics_ignore_a_bare_file_without_an_executable_extension()
    {
        File.WriteAllText(Path.Combine(_dir.Root, "codex"), "#!/bin/sh\n");

        Assert.Throws<InvalidOperationException>(() => ExecutableResolver.Resolve("codex", [_dir.Root], WindowsExtensions, isWindows: true));
    }

    [Fact]
    public void Unix_semantics_resolve_the_bare_file()
    {
        var bare = Path.Combine(_dir.Root, "codex");
        File.WriteAllText(bare, "#!/bin/sh\n");
        File.WriteAllText(Path.Combine(_dir.Root, "codex.cmd"), "@echo off\r\n");

        Assert.Equal(bare, ExecutableResolver.Resolve("codex", [_dir.Root], [], isWindows: false));
    }

    [Fact]
    public void The_first_directory_that_has_the_command_wins()
    {
        var first = Path.Combine(_dir.Root, "first");
        var second = Path.Combine(_dir.Root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var expected = Path.Combine(second, "gemini");
        File.WriteAllText(expected, "#!/bin/sh\n");
        File.WriteAllText(Path.Combine(_dir.Root, "gemini"), "#!/bin/sh\n");

        Assert.Equal(expected, ExecutableResolver.Resolve("gemini", [first, second, _dir.Root], [], isWindows: false));
    }

    [Fact]
    public void Missing_command_throws_naming_the_command_and_an_install_hint()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ExecutableResolver.Resolve("codex", [_dir.Root], WindowsExtensions, isWindows: true));

        Assert.Contains("codex", ex.Message);
        Assert.Contains("npm install -g @openai/codex", ex.Message);
    }

    [Fact]
    public void Convenience_overload_reads_the_real_path_and_finds_git()
    {
        var git = ExecutableResolver.Resolve("git");

        Assert.True(File.Exists(git));
        Assert.Equal("git", Path.GetFileNameWithoutExtension(git));
        Assert.Equal(OperatingSystem.IsWindows(), Path.GetExtension(git).Length > 0);
    }

    public void Dispose() => _dir.Dispose();
}
