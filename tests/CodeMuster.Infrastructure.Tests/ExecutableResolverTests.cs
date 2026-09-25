namespace CodeMuster.Infrastructure.Tests;

public sealed class ExecutableResolverTests : IDisposable
{
    private static readonly string[] WindowsExtensions = [".com", ".exe", ".bat", ".cmd"];
    private readonly TempDirectory _dir = new();

    [Fact]
    public void Windows_semantics_resolve_the_cmd_shim_when_only_the_shim_exists()
    {
        var shim = Path.Combine(_dir.Root, "codex.cmd");
        WriteExecutable(shim);

        var resolved = ExecutableResolver.Resolve("codex", [_dir.Root], WindowsExtensions, isWindows: true);

        Assert.Equal(shim, resolved);
    }

    [Fact]
    public void Windows_semantics_prefer_the_shim_over_a_bare_file_in_the_same_directory()
    {
        File.WriteAllText(Path.Combine(_dir.Root, "codex"), "#!/bin/sh\n");
        var shim = Path.Combine(_dir.Root, "codex.cmd");
        WriteExecutable(shim);

        Assert.Equal(shim, ExecutableResolver.Resolve("codex", [_dir.Root], WindowsExtensions, isWindows: true));
    }

    [Fact]
    public void Windows_semantics_follow_pathext_order()
    {
        WriteExecutable(Path.Combine(_dir.Root, "claude.cmd"));
        var exe = Path.Combine(_dir.Root, "claude.exe");
        WriteExecutable(exe);

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
        WriteExecutable(bare);
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
        WriteExecutable(expected);
        WriteExecutable(Path.Combine(_dir.Root, "gemini"));

        Assert.Equal(expected, ExecutableResolver.Resolve("gemini", [first, second, _dir.Root], [], isWindows: false));
    }

    [Fact]
    public void Path_entries_that_are_not_fully_qualified_are_skipped()
    {
        var name = "codemuster-tool-" + Guid.NewGuid().ToString("N");
        var relative = "codemuster-relative-" + Guid.NewGuid().ToString("N");
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var inCurrentDirectory = Path.Combine(Environment.CurrentDirectory, fileName);
        var inRelativeDirectory = Path.Combine(Environment.CurrentDirectory, relative, fileName);
        var expected = Path.Combine(_dir.Root, fileName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(inRelativeDirectory)!);
            WriteExecutable(inCurrentDirectory);
            WriteExecutable(inRelativeDirectory);
            WriteExecutable(expected);

            var resolved = ExecutableResolver.Resolve(name, [".", relative, "", _dir.Root], [".exe"], OperatingSystem.IsWindows());

            Assert.Equal(expected, resolved);
        }
        finally
        {
            File.Delete(inCurrentDirectory);
            Directory.Delete(Path.Combine(Environment.CurrentDirectory, relative), recursive: true);
        }
    }

    [Fact]
    public void Only_relative_path_entries_mean_the_command_is_not_found()
    {
        var name = "codemuster-tool-" + Guid.NewGuid().ToString("N");
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var inCurrentDirectory = Path.Combine(Environment.CurrentDirectory, fileName);
        try
        {
            WriteExecutable(inCurrentDirectory);

            Assert.Throws<InvalidOperationException>(() => ExecutableResolver.Resolve(name, ["."], [".exe"], OperatingSystem.IsWindows()));
        }
        finally
        {
            File.Delete(inCurrentDirectory);
        }
    }

    [Fact]
    public void Unix_semantics_skip_a_file_without_the_execute_bit_on_unix()
    {
        var first = Path.Combine(_dir.Root, "first");
        var second = Path.Combine(_dir.Root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var plain = Path.Combine(first, "git");
        File.WriteAllText(plain, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(plain, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        var executable = Path.Combine(second, "git");
        WriteExecutable(executable);

        var resolved = ExecutableResolver.Resolve("git", [first, second], [], isWindows: false);

        // Windows file systems have no execute bit, so there the first file named git still wins.
        Assert.Equal(OperatingSystem.IsWindows() ? plain : executable, resolved);
    }

    [Fact]
    public void A_dangling_link_earlier_on_path_is_skipped_for_the_real_program_after_it()
    {
        // A stale link left by an uninstall (Homebrew, nvm, volta) sits ahead of the real program, as a shell's search would skip it.
        var stale = Path.Combine(_dir.Root, "stale");
        var real = Path.Combine(_dir.Root, "real");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(real);
        var name = OperatingSystem.IsWindows() ? "tool.exe" : "tool";
        var linked = TryLink(Path.Combine(stale, name), Path.Combine(_dir.Root, "uninstalled", name));
        var program = Path.Combine(real, name);
        WriteExecutable(program);

        var resolved = ExecutableResolver.Resolve("tool", [stale, real], [".exe"], OperatingSystem.IsWindows());

        // Windows needs developer mode or elevation to make the link; without it the stale directory is simply empty.
        Assert.True(linked || OperatingSystem.IsWindows());
        Assert.Equal(program, resolved);
    }

    private static bool TryLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

    [Theory]
    [InlineData("git", "git-scm.com")]
    [InlineData("node", "nodejs.org")]
    public void Missing_git_or_node_names_where_to_get_it(string name, string hint)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ExecutableResolver.Resolve(name, [_dir.Root], WindowsExtensions, isWindows: true));

        Assert.Contains(hint, ex.Message);
    }

    public void Dispose() => _dir.Dispose();

    private static void WriteExecutable(string path)
    {
        File.WriteAllText(path, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
