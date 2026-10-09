namespace WK7Bot.Core.Utilities;

using System;
using System.Globalization;

/// <summary>
/// German date and time formatting, shared so every embed formats month names, weekdays and clock
/// times identically (and the culture is created once instead of per call).
/// </summary>
public static class DateTimeFormatExtensions
{
    /// <summary>
    /// The culture used for all user-facing German text.
    /// </summary>
    public static CultureInfo German { get; } = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Formats a date as the German month name (e.g. "Oktober").
    /// </summary>
    /// <param name="value">The date to format.</param>
    /// <returns>The German month name.</returns>
    public static string GermanMonthName(this DateTime value)
        => value.ToString("MMMM", German);

    /// <summary>
    /// Formats a date as the German weekday name (e.g. "Donnerstag").
    /// </summary>
    /// <param name="value">The date to format.</param>
    /// <returns>The German weekday name.</returns>
    public static string GermanDayName(this DateTime value)
        => value.ToString("dddd", German);
}
