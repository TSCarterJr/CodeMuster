namespace CodeMuster.Application;

/// <summary>Thrown when a verb runs in a repo that has no <c>.codemuster/config.json</c> yet (D24).</summary>
public sealed class NotInitializedException() : Exception("not set up here; run `codemuster init`");
