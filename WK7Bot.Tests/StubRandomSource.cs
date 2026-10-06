using WK7Bot.Services.Interfaces;

namespace WK7Bot.Tests;

/// <summary>
/// Deterministic randomness for tests: values are served from a script and fall back to 0, which keeps
/// candidate ordering and weighted picks reproducible.
/// </summary>
internal sealed class StubRandomSource : IRandomSource
{
    private readonly Queue<int> _values;

    public StubRandomSource(params int[] values)
    {
        _values = new Queue<int>(values ?? Array.Empty<int>());
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            return 0;
        }

        int raw = _values.Count > 0 ? _values.Dequeue() : 0;
        int value = raw % maxExclusive;
        return value < 0 ? value + maxExclusive : value;
    }
}
