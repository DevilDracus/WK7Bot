namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using WK7Bot.Options;

/// <summary>
/// In-memory record of the last outcome of every monitored feature's periodic work. The MQTT status
/// sensors, the deep health endpoint and future alerting read it; the background services only record
/// outcomes, so a feature never depends on its consumers.
/// </summary>
public sealed class FeatureHealthTracker
{
    private readonly Dictionary<string, FeatureHealth> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>
    /// Records that a feature just completed a pass without errors. A successful pass also clears the
    /// feature's problem state, so a feature that recovers on its own stops reporting problems.
    /// </summary>
    /// <param name="feature">The feature key (see <see cref="FeatureKeys"/>).</param>
    public void RecordSuccess(string feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        lock (_gate)
        {
            GetOrAdd(feature).RecordSuccess(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Records that a feature's pass failed. The feature reports problems until the next successful pass.
    /// </summary>
    /// <param name="feature">The feature key (see <see cref="FeatureKeys"/>).</param>
    /// <param name="error">The failure message surfaced to Home Assistant and <c>/health</c>.</param>
    public void RecordFailure(string feature, string? error = null)
    {
        ArgumentNullException.ThrowIfNull(feature);

        lock (_gate)
        {
            GetOrAdd(feature).RecordFailure(DateTime.UtcNow, error);
        }
    }

    /// <summary>
    /// Returns the recorded state of a feature, or a never-run snapshot when the feature has not been
    /// recorded yet (for example because it is disabled).
    /// </summary>
    /// <param name="feature">The feature key (see <see cref="FeatureKeys"/>).</param>
    /// <returns>The feature's current health snapshot.</returns>
    public FeatureHealthSnapshot Get(string feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        lock (_gate)
        {
            return _states.TryGetValue(feature, out var state)
                ? state.ToSnapshot(feature)
                : new FeatureHealthSnapshot(feature);
        }
    }

    /// <summary>
    /// Returns a consistent copy of every recorded feature state. The copy keeps a concurrent
    /// <see cref="RecordSuccess"/> from mutating the dictionary while it is enumerated.
    /// </summary>
    /// <returns>The snapshots keyed by feature key.</returns>
    public IReadOnlyDictionary<string, FeatureHealthSnapshot> GetAll()
    {
        lock (_gate)
        {
            var snapshots = new Dictionary<string, FeatureHealthSnapshot>(_states.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (feature, state) in _states)
            {
                snapshots[feature] = state.ToSnapshot(feature);
            }

            return snapshots;
        }
    }

    private FeatureHealth GetOrAdd(string feature)
    {
        if (!_states.TryGetValue(feature, out var state))
        {
            state = new FeatureHealth();
            _states[feature] = state;
        }

        return state;
    }

    /// <summary>
    /// Mutable per-feature bookkeeping behind <see cref="FeatureHealthTracker"/>.
    /// </summary>
    private sealed class FeatureHealth
    {
        public DateTime? LastSuccessUtc { get; private set; }

        public DateTime? LastFailureUtc { get; private set; }

        public string? LastError { get; private set; }

        public void RecordSuccess(DateTime succeededAtUtc)
        {
            LastSuccessUtc = succeededAtUtc;
        }

        public void RecordFailure(DateTime failedAtUtc, string? error)
        {
            LastFailureUtc = failedAtUtc;
            LastError = error;
        }

        public FeatureHealthSnapshot ToSnapshot(string feature) => new(feature)
        {
            LastSuccessUtc = LastSuccessUtc,
            LastFailureUtc = LastFailureUtc,
            LastError = LastError
        };
    }
}

/// <summary>
/// Immutable view of one feature's last recorded outcomes.
/// </summary>
/// <param name="Feature">The feature key (see <see cref="FeatureKeys"/>).</param>
public sealed record FeatureHealthSnapshot(string Feature)
{
    /// <summary>Gets the UTC timestamp of the last completed pass, or <see langword="null"/> when the feature never completed one.</summary>
    public DateTime? LastSuccessUtc { get; init; }

    /// <summary>Gets the UTC timestamp of the last failed pass, or <see langword="null"/> when the feature never failed.</summary>
    public DateTime? LastFailureUtc { get; init; }

    /// <summary>Gets the message of the last failure, or <see langword="null"/>.</summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets a value indicating whether the feature has ever been recorded, i.e. it is neither disabled
    /// nor waiting for its first pass.
    /// </summary>
    public bool HasRun => LastSuccessUtc.HasValue || LastFailureUtc.HasValue;

    /// <summary>
    /// Gets a value indicating whether the most recent outcome was a failure. A feature that never ran
    /// (disabled, or still in its first interval) is not a problem.
    /// </summary>
    public bool IsProblem => LastFailureUtc.HasValue && LastFailureUtc > LastSuccessUtc.GetValueOrDefault();
}
