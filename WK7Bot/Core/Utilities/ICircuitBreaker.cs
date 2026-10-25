namespace WK7Bot.Core.Utilities;

/// <summary>
/// Common contract for circuit breakers that gate calls to an external dependency.
/// </summary>
public interface ICircuitBreaker
{
    /// <summary>Returns true when the circuit is open and calls should be skipped.</summary>
    bool IsCircuitOpen();

    /// <summary>Records a successful call.</summary>
    void RecordSuccess();

    /// <summary>Records a failed call.</summary>
    void RecordFailure();
}
