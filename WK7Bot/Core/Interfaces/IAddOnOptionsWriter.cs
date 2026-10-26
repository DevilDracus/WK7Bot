namespace WK7Bot.Core.Interfaces;

using System.Text.Json.Nodes;

/// <summary>
/// Contract for reading and writing Home Assistant add-on options.
/// </summary>
public interface IAddOnOptionsWriter
{
    /// <summary>Reads the current add-on options.</summary>
    Task<JsonObject> ReadCurrentOptionsAsync();

    /// <summary>Merges the supplied top-level keys into the current options.</summary>
    Task<bool> MergeAsync(IDictionary<string, object?> patch, CancellationToken cancellationToken = default);

    /// <summary>Merges a single feature switch into the current options.</summary>
    Task<bool> MergeFeatureAsync(string featureKey, bool enabled, CancellationToken cancellationToken = default);
}