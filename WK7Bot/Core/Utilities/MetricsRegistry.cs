namespace WK7Bot.Core.Utilities;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading;

/// <summary>
/// Process-wide Prometheus metrics registry for command usage, error notifications and external HTTP calls.
/// Registered as a singleton in DI; injected into services that record metrics.
/// </summary>
public sealed class MetricsRegistry
{
    private readonly ConcurrentDictionary<string, long> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _commandErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _httpRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> _httpDurationSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _httpFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private long _errors;

    /// <summary>Records one slash command invocation.</summary>
    /// <param name="command">The command name.</param>
    /// <param name="succeeded">Whether the command completed successfully.</param>
    public void RecordCommand(string command, bool succeeded)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _commands.AddOrUpdate(command, 1, static (_, current) => current + 1);
        if (!succeeded)
        {
            _commandErrors.AddOrUpdate(command, 1, static (_, current) => current + 1);
        }
    }

    /// <summary>Records one buffered error notification.</summary>
    public void RecordError() => Interlocked.Increment(ref _errors);

    /// <summary>Records one external HTTP operation.</summary>
    /// <param name="operation">The logical operation name.</param>
    /// <param name="milliseconds">The elapsed time in milliseconds.</param>
    /// <param name="succeeded">Whether the operation succeeded.</param>
    public void RecordHttp(string operation, double milliseconds, bool succeeded)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return;
        }

        _httpRequests.AddOrUpdate(operation, 1, static (_, current) => current + 1);
        _httpDurationSeconds.AddOrUpdate(operation, milliseconds / 1000.0, (_, current) => current + milliseconds / 1000.0);
        if (!succeeded)
        {
            _httpFailures.AddOrUpdate(operation, 1, static (_, current) => current + 1);
        }
    }

    /// <summary>
    /// Renders the current snapshot in Prometheus text exposition format.
    /// </summary>
    /// <param name="health">The feature health tracker whose problem states are exposed as gauges.</param>
    /// <returns>The Prometheus payload.</returns>
    public string RenderPrometheus(IFeatureHealthTracker health)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# HELP wk7bot_up Whether the bot process is running.");
        builder.AppendLine("# TYPE wk7bot_up gauge");
        builder.AppendLine("wk7bot_up 1");

        builder.AppendLine("# HELP wk7bot_uptime_seconds Process uptime in seconds.");
        builder.AppendLine("# TYPE wk7bot_uptime_seconds gauge");
        builder.AppendLine($"wk7bot_uptime_seconds {Format((DateTime.UtcNow - _startedAtUtc).TotalSeconds)}");

        builder.AppendLine("# HELP wk7bot_commands_total Slash command invocations.");
        builder.AppendLine("# TYPE wk7bot_commands_total counter");
        foreach (var (command, count) in _commands.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"wk7bot_commands_total{{command=\"{Escape(command)}\"}} {count}");
        }

        builder.AppendLine("# HELP wk7bot_command_errors_total Failed slash command invocations.");
        builder.AppendLine("# TYPE wk7bot_command_errors_total counter");
        foreach (var (command, count) in _commandErrors.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"wk7bot_command_errors_total{{command=\"{Escape(command)}\"}} {count}");
        }

        builder.AppendLine("# HELP wk7bot_errors_total Buffered error notifications.");
        builder.AppendLine("# TYPE wk7bot_errors_total counter");
        builder.AppendLine($"wk7bot_errors_total {Interlocked.Read(ref _errors)}");

        builder.AppendLine("# HELP wk7bot_feature_problem Whether a monitored feature is currently in problem state.");
        builder.AppendLine("# TYPE wk7bot_feature_problem gauge");
        foreach (var (feature, snapshot) in health.GetAll().OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"wk7bot_feature_problem{{feature=\"{Escape(feature)}\"}} {(snapshot.IsProblem ? 1 : 0)}");
        }

        builder.AppendLine("# HELP wk7bot_http_requests_total External HTTP operations.");
        builder.AppendLine("# TYPE wk7bot_http_requests_total counter");
        foreach (var (operation, count) in _httpRequests.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"wk7bot_http_requests_total{{operation=\"{Escape(operation)}\"}} {count}");
        }

        builder.AppendLine("# HELP wk7bot_http_failures_total Failed external HTTP operations.");
        builder.AppendLine("# TYPE wk7bot_http_failures_total counter");
        foreach (var (operation, count) in _httpFailures.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"wk7bot_http_failures_total{{operation=\"{Escape(operation)}\"}} {count}");
        }

        builder.AppendLine("# HELP wk7bot_http_duration_seconds_total Cumulative external HTTP duration.");
        builder.AppendLine("# TYPE wk7bot_http_duration_seconds_total counter");
        foreach (var (operation, seconds) in _httpDurationSeconds.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"wk7bot_http_duration_seconds_total{{operation=\"{Escape(operation)}\"}} {Format(seconds)}");
        }

        return builder.ToString();
    }

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
