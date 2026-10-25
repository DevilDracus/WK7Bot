namespace WK7Bot.Core.Utilities;

/// <summary>
/// Tracks the last outcome of every monitored feature's periodic work.
/// </summary>
public interface IFeatureHealthTracker
{
    /// <summary>Records a successful pass for the given feature.</summary>
    void RecordSuccess(string feature);

    /// <summary>Records a failed pass for the given feature.</summary>
    void RecordFailure(string feature, string? error = null);

    /// <summary>Returns the health snapshot for one feature.</summary>
    FeatureHealthSnapshot Get(string feature);

    /// <summary>Returns the health snapshot for all tracked features.</summary>
    IReadOnlyDictionary<string, FeatureHealthSnapshot> GetAll();
}
