namespace WK7Bot.Core.Utilities;

using Discord;
using WK7Bot.Models;

/// <summary>
/// Builds the rich embed used for Discord error report direct messages, presenting context,
/// message, exception details, stack trace and timestamp in a single readable card.
/// </summary>
public static class ErrorEmbedBuilder
{
    /// <summary>
    /// Accent color of error embeds.
    /// </summary>
    public static readonly Color ErrorColor = new(231, 76, 60);

    /// <summary>
    /// Prefix of the embed title, followed by the short context name.
    /// </summary>
    public const string TitlePrefix = "⚠️ Fehler in ";

    /// <summary>
    /// Footer text shown on every error embed.
    /// </summary>
    public const string FooterText = "WK7 Bot • Fehlerbenachrichtigung";

    private const int TitleLimit = 256;
    private const int FieldValueLimit = 1024;
    private const int CodeBlockOverhead = 8;

    /// <summary>
    /// Builds the error embed for the given report, truncating every section to Discord's field limits.
    /// </summary>
    /// <param name="notification">The error report to render.</param>
    /// <returns>The rendered Discord embed.</returns>
    public static Embed Build(ErrorNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var builder = new EmbedBuilder()
            .WithColor(ErrorColor)
            .WithTitle(Truncate($"{TitlePrefix}{ShortContext(notification.Context)}", TitleLimit))
            .WithFooter(FooterText);

        if (!string.IsNullOrWhiteSpace(notification.ExceptionType))
        {
            var detail = string.IsNullOrWhiteSpace(notification.ExceptionMessage)
                ? notification.ExceptionType
                : $"{notification.ExceptionType}: {notification.ExceptionMessage}";

            builder.AddField("Ausnahme", Truncate(detail, FieldValueLimit), false);
        }

        if (!string.IsNullOrWhiteSpace(notification.Message) && notification.Message != notification.ExceptionMessage)
        {
            builder.AddField("Meldung", CodeBlock(notification.Message), false);
        }

        if (!string.IsNullOrWhiteSpace(notification.StackTrace))
        {
            builder.AddField("Stack-Trace", CodeBlock(notification.StackTrace), false);
        }

        var context = string.IsNullOrWhiteSpace(notification.Context) ? "Unbekannt" : notification.Context;
        builder.AddField("Kontext", Truncate(context, FieldValueLimit), false);
        builder.AddField("Zeitpunkt", notification.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss zzz"), false);

        return builder.Build();
    }

    /// <summary>
    /// Extracts the trailing type or hook name from a dotted context identifier.
    /// </summary>
    /// <param name="context">The full context identifier.</param>
    /// <returns>The segment after the last dot, or the input when it contains no dot.</returns>
    private static string ShortContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return "Unbekannt";
        }

        var separatorIndex = context.LastIndexOf('.');
        return separatorIndex >= 0 && separatorIndex < context.Length - 1 ? context[(separatorIndex + 1)..] : context;
    }

    /// <summary>
    /// Wraps text in a fenced code block, neutralizing embedded fences to keep the layout intact.
    /// </summary>
    /// <param name="text">The text to wrap.</param>
    /// <returns>The fenced text, truncated to Discord's field value limit.</returns>
    private static string CodeBlock(string text)
    {
        var sanitized = text.Replace("```", "'''", StringComparison.Ordinal);
        return $"```\n{Truncate(sanitized, FieldValueLimit - CodeBlockOverhead)}\n```";
    }

    /// <summary>
    /// Cuts a string to the given limit, appending an ellipsis when content had to be removed.
    /// </summary>
    /// <param name="value">The text to truncate.</param>
    /// <param name="limit">The maximum allowed length.</param>
    /// <returns>The truncated text.</returns>
    private static string Truncate(string value, int limit)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= limit)
        {
            return value;
        }

        if (limit <= 1)
        {
            return value[..limit];
        }

        return $"{value[..(limit - 1)]}…";
    }
}
