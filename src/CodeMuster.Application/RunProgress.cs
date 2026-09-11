namespace CodeMuster.Application;

/// <summary>One attempt on one unit, reported by <see cref="Run"/> as soon as it finishes.</summary>
/// <param name="UnitId">The unit attempted.</param>
/// <param name="Attempt">How many times this run has tried the unit, counting this one.</param>
/// <param name="Outcome">What <see cref="Done"/> did with the response; Rejected when the adapter itself failed.</param>
/// <param name="Message">One line for the console: the Done message, the adapter error, or why the run gave up.</param>
/// <param name="Completed">Units recorded so far this run.</param>
/// <param name="Total">Units that needed work when the run started.</param>
public sealed record RunProgress(string UnitId, int Attempt, DoneOutcome Outcome, string Message, int Completed, int Total);
