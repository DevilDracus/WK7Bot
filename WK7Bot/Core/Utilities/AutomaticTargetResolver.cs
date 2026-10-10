namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using WK7Bot.Options;

/// <summary>
/// Resolves which Discord servers receive the automatic messages of a feature: the WK7 server by
/// default, the bot test server while the feature is listed in <c>servers.testing_features</c>, and
/// (with a warning) every guild the bot is in while the server IDs are still the shipped
/// placeholders or empty.
/// </summary>
public static class AutomaticTargetResolver
{
    /// <summary>
    /// Resolves the target guild IDs for the given feature.
    /// </summary>
    /// <param name="servers">The configured server IDs and testing features.</param>
    /// <param name="featureKey">The feature key (e.g. <c>weekend_digest</c>) being routed.</param>
    /// <param name="availableGuildIds">The guild IDs the bot is currently a member of.</param>
    /// <param name="logger">Optional logger used when falling back to every guild.</param>
    /// <returns>The target Discord guild IDs (never <see langword="null"/>).</returns>
    public static IReadOnlyList<ulong> Resolve(
        ServersOptions? servers,
        string featureKey,
        IEnumerable<ulong> availableGuildIds,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(availableGuildIds);

        var preferred = SelectPreferredServerId(servers, featureKey);
        if (preferred.HasValue)
        {
            return new[] { preferred.Value };
        }

        var guildIds = availableGuildIds as IReadOnlyList<ulong> ?? availableGuildIds.ToList();

        logger?.LogWarning(
            "servers.wk7_server_id / servers.test_server_id are not configured for feature {Feature}; posting to all {Count} guild(s) the bot is in.",
            featureKey,
            guildIds.Count);

        return guildIds;
    }

    /// <summary>
    /// Resolves the target guild IDs for a feature that must never post to the WK7 production
    /// server: only the bot test server is eligible. When <c>servers.test_server_id</c> is not a
    /// usable numeric ID the result is empty, so the caller falls back to direct messages instead
    /// of leaking content to the production server (or to every guild the bot is in).
    /// </summary>
    /// <param name="servers">The configured server IDs and testing features.</param>
    /// <param name="logger">Optional logger used when no test server is configured.</param>
    /// <returns>The test server guild ID, or an empty list when none is configured.</returns>
    public static IReadOnlyList<ulong> ResolveTestServerOnly(ServersOptions? servers, ILogger? logger = null)
    {
        if (servers is not null && servers.TryGetTestServerId(out var testServerId))
        {
            return new[] { testServerId };
        }

        logger?.LogWarning(
            "servers.test_server_id is not configured; restricting delivery to direct messages instead of posting to the WK7 server.");

        return Array.Empty<ulong>();
    }

    /// <summary>
    /// Picks the configured server for a feature: the test server while the feature is being
    /// tested, otherwise the WK7 server.
    /// </summary>
    /// <param name="servers">The configured server IDs and testing features.</param>
    /// <param name="featureKey">The feature key being routed.</param>
    /// <returns>The preferred guild ID, or <see langword="null"/> when none is configured.</returns>
    private static ulong? SelectPreferredServerId(ServersOptions? servers, string featureKey)
    {
        if (servers is null)
        {
            return null;
        }

        if (servers.IsFeatureBeingTested(featureKey) && servers.TryGetTestServerId(out var testServerId))
        {
            return testServerId;
        }

        return servers.TryGetWk7ServerId(out var wk7ServerId) ? wk7ServerId : null;
    }
}
