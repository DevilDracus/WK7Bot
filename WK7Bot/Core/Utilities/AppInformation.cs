namespace WK7Bot.Core.Utilities;

using System;
using System.Reflection;

/// <summary>
/// Exposes the application version reported by <c>/health</c> and the Home Assistant device attributes.
/// </summary>
public static class AppInformation
{
    /// <summary>
    /// Gets the informational version of the running assembly (without the source-revision suffix),
    /// falling back to the assembly version.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(AppInformation).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The attribute appends "+<commit hash>" to release builds; trim it for display.
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
