namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using WK7Bot.Options;

/// <summary>
/// The overall health of the bot as reported by <c>/health</c>.
/// </summary>
/// <param name="Status">Either <c>ok</c> or <c>degraded</c> while an enabled feature is in problem state.</param>
/// <param name="Version">The application version.</param>
/// <param name="StartedAtUtc">The UTC process start time.</param>
/// <param name="UptimeSeconds">The process uptime in seconds.</param>
/// <param name="DiscordConnection">The Discord gateway connection state.</param>
/// <param name="Guilds">The number of guilds the bot is connected to.</param>
/// <param name="Features">The per-feature health of every monitored feature.</param>
public sealed record HealthReport(
    string Status,
    string Version,
    DateTime StartedAtUtc,
    long UptimeSeconds,
    string DiscordConnection,
    int Guilds,
    IReadOnlyList<FeatureHealthReport> Features);

/// <summary>
/// The health of a single monitored feature as reported by <c>/health</c>.
/// </summary>
/// <param name="Feature">The feature key (see <see cref="FeatureKeys"/>).</param>
/// <param name="Enabled">Whether the feature flag is set.</param>
/// <param name="IsProblem">Whether the feature's most recent recorded outcome was a failure.</param>
/// <param name="LastSuccessUtc">The UTC timestamp of the last completed pass, or <see langword="null"/>.</param>
/// <param name="LastFailureUtc">The UTC timestamp of the last failed pass, or <see langword="null"/>.</param>
/// <param name="LastError">The message of the last failure, or <see langword="null"/>.</param>
public sealed record FeatureHealthReport(
    string Feature,
    bool Enabled,
    bool IsProblem,
    DateTime? LastSuccessUtc,
    DateTime? LastFailureUtc,
    string? LastError);

/// <summary>
/// Projects the feature health tracker into the <see cref="HealthReport"/> served by <c>/health</c>,
/// so the endpoint stays a projection and the reporting rules live in one testable place.
/// </summary>
public static class HealthReportBuilder
{
    /// <summary>
    /// Builds the current health report.
    /// </summary>
    /// <param name="tracker">The feature health tracker to project.</param>
    /// <param name="features">The bound feature options resolving each feature's enabled flag.</param>
    /// <param name="startedAtUtc">The UTC process start time.</param>
    /// <param name="discordConnection">The current Discord gateway connection state.</param>
    /// <param name="guildCount">The number of guilds the bot is connected to.</param>
    /// <returns>The health report.</returns>
    public static HealthReport Build(
        FeatureHealthTracker tracker,
        FeatureOptions features,
        DateTime startedAtUtc,
        string discordConnection,
        int guildCount)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(features);

        var uptime = DateTime.UtcNow - startedAtUtc;

        var featureReports = BotStatusEntities.MonitoredFeatures
            .Select(feature =>
            {
                var snapshot = tracker.Get(feature.FeatureKey);
                return new FeatureHealthReport(
                    feature.FeatureKey,
                    features.IsEnabled(feature.FeatureKey),
                    snapshot.IsProblem,
                    snapshot.LastSuccessUtc,
                    snapshot.LastFailureUtc,
                    snapshot.LastError);
            })
            .ToList();

        // A disabled feature cannot be degraded, and a feature that never ran (still waiting for its
        // first interval) is not a problem either — only a failed pass of an enabled feature is.
        var degraded = featureReports.Any(feature => feature.Enabled && feature.IsProblem);

        return new HealthReport(
            degraded ? "degraded" : "ok",
            AppInformation.Version,
            startedAtUtc,
            (long)(uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime).TotalSeconds,
            discordConnection,
            guildCount,
            featureReports);
    }
}
