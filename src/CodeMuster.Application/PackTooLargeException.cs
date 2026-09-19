namespace CodeMuster.Application;

internal sealed class PackTooLargeException(string path) : InvalidOperationException(
    $"{path} exceeds the whole-file pack limit; increase slice_token_budget deliberately or split the file, then rescan; no coverage was recorded");
