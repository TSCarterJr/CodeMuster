namespace CodeMuster.Domain;

/// <summary>What one agent call returned: the response text, exactly as the model wrote it, and the call's usage (D63).</summary>
/// <param name="Text">The response text handed to <c>done</c> or fix.</param>
/// <param name="Usage">Tokens, answering model and any harness-reported cost; <see cref="AgentUsage.Unknown"/> when not reported.</param>
public sealed record AgentReply(string Text, AgentUsage Usage);
