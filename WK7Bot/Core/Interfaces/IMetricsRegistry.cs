namespace WK7Bot.Core.Interfaces;

using WK7Bot.Core.Utilities;

/// <summary>
/// Contract for the process-wide Prometheus metrics registry.
/// </summary>
public interface IMetricsRegistry
{
    /// <summary>Records one slash command invocation.</summary>
    void RecordCommand(string command, bool succeeded);

    /// <summary>Records one buffered error notification.</summary>
    void RecordError();

    /// <summary>Records one external HTTP operation.</summary>
    void RecordHttp(string operation, double milliseconds, bool succeeded);

    /// <summary>Renders the current snapshot in Prometheus text exposition format.</summary>
    string RenderPrometheus(IFeatureHealthTracker health);
}