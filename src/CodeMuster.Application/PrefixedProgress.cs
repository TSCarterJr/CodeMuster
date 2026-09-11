namespace CodeMuster.Application;

internal sealed class PrefixedProgress(IProgress<string> inner, string prefix) : IProgress<string>
{
    public void Report(string value) => inner.Report($"{prefix}: {value}");

    public static IProgress<string>? For(IProgress<string>? inner, string prefix) =>
        inner is null ? null : new PrefixedProgress(inner, prefix);
}
