using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class MetricsRegistryTests
{
    [Fact]
    public void RecordCommand_RendersCommandAndErrorCounters()
    {
        var registry = new MetricsRegistry();
        registry.RecordCommand("unit-test-command", succeeded: true);
        registry.RecordCommand("unit-test-command", succeeded: false);

        var text = registry.RenderPrometheus(new FeatureHealthTracker());

        Assert.Contains("wk7bot_commands_total{command=\"unit-test-command\"} 2", text);
        Assert.Contains("wk7bot_command_errors_total{command=\"unit-test-command\"} 1", text);
    }

    [Fact]
    public void RecordError_IncrementsErrorCounter()
    {
        var registry = new MetricsRegistry();
        registry.RecordError();
        registry.RecordError();

        var text = registry.RenderPrometheus(new FeatureHealthTracker());

        Assert.Contains("wk7bot_errors_total 2", text);
    }

    [Fact]
    public void RenderPrometheus_ExposesFeatureProblemGauge()
    {
        var health = new FeatureHealthTracker();
        health.RecordFailure("rss_polling_enabled", "boom");

        var text = new MetricsRegistry().RenderPrometheus(health);

        Assert.Contains("wk7bot_feature_problem{feature=\"rss_polling_enabled\"} 1", text);
        Assert.Contains("wk7bot_up 1", text);
    }

    [Fact]
    public void RecordHttp_RendersRequestAndFailureCounters()
    {
        var registry = new MetricsRegistry();
        registry.RecordHttp("steam", 250, succeeded: true);
        registry.RecordHttp("steam", 500, succeeded: false);

        var text = registry.RenderPrometheus(new FeatureHealthTracker());

        Assert.Contains("wk7bot_http_requests_total{operation=\"steam\"} 2", text);
        Assert.Contains("wk7bot_http_failures_total{operation=\"steam\"} 1", text);
    }
}
