namespace WK7Bot.Core.Utilities;

using WK7Bot.Core.Interfaces;

/// <summary>
/// Production randomness backed by <see cref="Random.Shared"/>, which is safe for concurrent use.
/// </summary>
public sealed class SystemRandomSource : IRandomSource
{
    /// <inheritdoc />
    public int Next(int maxExclusive)
        => Random.Shared.Next(maxExclusive);
}