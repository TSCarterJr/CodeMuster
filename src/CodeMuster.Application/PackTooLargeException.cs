using CodeMuster.Domain;

namespace CodeMuster.Application;

internal sealed class PackTooLargeException(string path, UnitKind kind) : InvalidOperationException(
    $"{path} exceeds the whole-file pack limit; increase slice_token_budget deliberately or split the file, then "
    + (kind == UnitKind.Fix ? "run fix again; no repair was attempted" : "rescan; no coverage was recorded"));
