namespace CodeMuster.Domain;

/// <summary>Supplies the engine commands (D65) a controller writes while run, verify or fix is working, one line per command.</summary>
public interface IEngineControl
{
    /// <summary>Completes with the next command line once one has arrived; each line is returned once, in the order it was written.</summary>
    Task<string> NextCommandAsync(CancellationToken cancellationToken);
}
