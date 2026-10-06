namespace WK7Bot.Services.Interfaces;

/// <summary>
/// Supplies pseudo-random numbers so randomized behaviour (recipe candidate selection, seasonal produce picking)
/// can be steered deterministically from tests.
/// </summary>
public interface IRandomSource
{
    /// <summary>
    /// Returns a non-negative random integer that is less than <paramref name="maxExclusive"/>.
    /// </summary>
    /// <param name="maxExclusive">The exclusive upper bound (must be positive).</param>
    /// <returns>A value in <c>[0, maxExclusive)</c>.</returns>
    int Next(int maxExclusive);
}
