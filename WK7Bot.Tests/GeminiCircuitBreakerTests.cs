using Microsoft.Extensions.Logging.Abstractions;
using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public sealed class GeminiCircuitBreakerTests
{
    [Fact]
    public void RecordFailure_AfterThreshold_OpensCircuit()
    {
        var breaker = new GeminiCircuitBreaker(NullLogger<GeminiCircuitBreaker>.Instance);

        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.False(breaker.IsCircuitOpen());

        breaker.RecordFailure();
        Assert.True(breaker.IsCircuitOpen());
    }

    [Fact]
    public void RecordSuccess_ResetsFailureCount()
    {
        var breaker = new GeminiCircuitBreaker(NullLogger<GeminiCircuitBreaker>.Instance);

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();

        breaker.RecordFailure();
        Assert.False(breaker.IsCircuitOpen());
    }

    [Fact]
    public void IsCircuitOpen_CoolsDownAfterDuration()
    {
        var now = DateTime.UtcNow;
        var breaker = new GeminiCircuitBreaker(NullLogger<GeminiCircuitBreaker>.Instance, () => now);

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.True(breaker.IsCircuitOpen());

        // After the cooldown, the circuit becomes half-open and allows a trial request.
        now = now + TimeSpan.FromMinutes(6);
        Assert.False(breaker.IsCircuitOpen());
    }
}
