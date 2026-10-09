namespace WK7Bot.Core.Utilities;

/// <summary>
/// Shared Discord embed length limits and truncation helpers so every builder enforces the same
/// boundaries instead of maintaining divergent private implementations.
/// </summary>
public static class EmbedText
{
    /// <summary>Maximum length of an embed title.</summary>
    public const int TitleLimit = 256;

    /// <summary>Maximum length of an embed description.</summary>
    public const int DescriptionLimit = 4096;

    /// <summary>Maximum length of a field name.</summary>
    public const int FieldNameLimit = 256;

    /// <summary>Maximum length of a field value.</summary>
    public const int FieldValueLimit = 1024;

    /// <summary>Maximum length of the embed footer text.</summary>
    public const int FooterLimit = 2048;

    /// <summary>Combined character budget for the whole embed (title, description, fields, footer, author).</summary>
    public const int TotalLimit = 6000;

    /// <summary>Maximum number of fields per embed.</summary>
    public const int MaxFields = 25;

    /// <summary>
    /// Truncates <paramref name="value"/> to <paramref name="limit"/> characters, appending an ellipsis when cut.
    /// </summary>
    /// <param name="value">The text to truncate; <see langword="null"/> becomes an empty string.</param>
    /// <param name="limit">The maximum number of characters to keep (including the ellipsis).</param>
    /// <returns>The original or truncated text.</returns>
    public static string Truncate(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (limit <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= limit)
        {
            return value;
        }

        if (limit == 1)
        {
            return "…";
        }

        return value[..(limit - 1)] + "…";
    }

    /// <summary>Truncates a value to the embed title limit.</summary>
    /// <param name="value">The text to truncate.</param>
    /// <returns>The text, at most <see cref="TitleLimit"/> characters.</returns>
    public static string Title(string? value) => Truncate(value, TitleLimit);

    /// <summary>Truncates a value to the embed description limit.</summary>
    /// <param name="value">The text to truncate.</param>
    /// <returns>The text, at most <see cref="DescriptionLimit"/> characters.</returns>
    public static string Description(string? value) => Truncate(value, DescriptionLimit);

    /// <summary>Truncates a value to the embed field value limit.</summary>
    /// <param name="value">The text to truncate.</param>
    /// <returns>The text, at most <see cref="FieldValueLimit"/> characters.</returns>
    public static string Field(string? value) => Truncate(value, FieldValueLimit);
}
