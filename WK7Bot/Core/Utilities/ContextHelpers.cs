namespace WK7Bot.Core.Utilities;

/// <summary>
/// Shared helper for extracting a short display name from a dotted context identifier.
/// </summary>
public static class ContextHelpers
{
    /// <summary>
    /// Extracts the trailing type or hook name from a dotted context identifier.
    /// </summary>
    /// <param name="context">The full context identifier.</param>
    /// <returns>The segment after the last dot, or the input when it contains no dot. Returns "Unbekannt" when the input is blank.</returns>
    public static string ShortContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return "Unbekannt";
        }

        var separatorIndex = context.LastIndexOf('.');
        return separatorIndex >= 0 && separatorIndex < context.Length - 1 ? context[(separatorIndex + 1)..] : context;
    }
}