namespace WK7Bot.Core.Exceptions;

using System;

/// <summary>
/// Thrown when every recipe search provider (DuckDuckGo HTML search and the Chefkoch fallback) failed to respond,
/// e.g. because the search endpoint serves a bot challenge or returns HTTP errors. This is distinct from a
/// <see langword="null"/> result, which simply means the providers answered but no parseable recipe was found.
/// </summary>
public class RecipeSearchUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecipeSearchUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The error message describing why the providers failed.</param>
    public RecipeSearchUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RecipeSearchUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The error message describing why the providers failed.</param>
    /// <param name="innerException">The underlying provider failure.</param>
    public RecipeSearchUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
