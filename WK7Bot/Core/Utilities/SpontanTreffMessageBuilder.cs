namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using WK7Bot.Core.Entities;

/// <summary>
/// Builds the embed and button components for spontaneous meetups ("Spontan-Treff") shared by the slash command,
/// the button handler and the expiry background service, so every caller renders the exact same message state.
/// </summary>
public static class SpontanTreffMessageBuilder
{
    /// <summary>
    /// Prefix of every button custom ID owned by this feature; matched by the component interaction handler.
    /// </summary>
    public const string CustomIdPrefix = "spontan-treff";

    /// <summary>
    /// Button action for "on my way".
    /// </summary>
    public const string GoAction = "go";

    /// <summary>
    /// Button action for passing.
    /// </summary>
    public const string PassAction = "pass";

    /// <summary>
    /// Discord's per-field character limit.
    /// </summary>
    private const int FieldValueLimit = 1024;

    /// <summary>
    /// Head-room kept below <see cref="FieldValueLimit"/> so the "plus N more" suffix always fits.
    /// </summary>
    private const int FieldValueBudget = FieldValueLimit - 32;

    private const string EmptyValue = "—";

    /// <summary>
    /// Determines whether a meetup still accepts answers at the given point in time.
    /// </summary>
    /// <param name="meetup">The meetup to check.</param>
    /// <param name="now">The current local time.</param>
    /// <returns><see langword="true"/> while the meetup is neither closed nor expired.</returns>
    public static bool IsActive(SpontanTreff meetup, DateTime now)
        => !meetup.Closed && meetup.ExpiresAt > now;

    /// <summary>
    /// Builds the button custom ID for an action, e.g. <c>spontan-treff:go:7</c>.
    /// </summary>
    /// <param name="action">One of <see cref="GoAction"/> or <see cref="PassAction"/>.</param>
    /// <param name="meetupId">The meetup identifier.</param>
    /// <returns>The custom ID handed to Discord.</returns>
    public static string BuildCustomId(string action, int meetupId)
        => $"{CustomIdPrefix}:{action}:{meetupId}";

    /// <summary>
    /// Renders the meetup embed plus its buttons; once the meetup is over the component row is empty, which removes
    /// the buttons when the message is edited.
    /// </summary>
    /// <param name="meetup">The meetup to render.</param>
    /// <param name="responses">The stored answers of the meetup.</param>
    /// <param name="now">The current local time used for the remaining-time text.</param>
    /// <returns>The embed and the message components of the current state.</returns>
    public static (Embed Embed, MessageComponent Components) Build(
        SpontanTreff meetup,
        IReadOnlyList<SpontanTreffResponse> responses,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(meetup);
        ArgumentNullException.ThrowIfNull(responses);

        bool active = IsActive(meetup, now);
        var going = responses.Where(r => r.Going).Select(r => r.UserId).ToList();
        var passing = responses.Where(r => !r.Going).Select(r => r.UserId).ToList();

        var embed = new EmbedBuilder()
            .WithTitle(active ? "⚡ Spontan-Treff" : "⚡ Spontan-Treff · vorbei")
            .WithDescription(BuildDescription(meetup, active, now))
            .WithColor(active ? Color.Gold : Color.DarkGrey)
            .AddField($"✅ Auf dem Weg ({going.Count})", FormatMentions(going), false)
            .AddField($"🚫 Abgesagt ({passing.Count})", FormatMentions(passing), false)
            .WithFooter(active
                ? $"Offen bis {meetup.ExpiresAt:HH:mm} Uhr"
                : $"Beendet • lief bis {meetup.ExpiresAt:HH:mm} Uhr")
            .Build();

        var componentBuilder = new ComponentBuilder();
        if (active)
        {
            componentBuilder = componentBuilder
                .WithButton("✅ Auf dem Weg", BuildCustomId(GoAction, meetup.Id), ButtonStyle.Success)
                .WithButton("🚫 Absagen", BuildCustomId(PassAction, meetup.Id), ButtonStyle.Secondary);
        }

        return (embed, componentBuilder.Build());
    }

    /// <summary>
    /// Composes the embed description including plan, optional meeting point, organizer and remaining time.
    /// </summary>
    private static string BuildDescription(SpontanTreff meetup, bool active, DateTime now)
    {
        var lines = new List<string> { $"**{meetup.Plan}**" };

        if (!string.IsNullOrWhiteSpace(meetup.Location))
        {
            lines.Add($"📍 {meetup.Location}");
        }

        lines.Add($"Organisiert von <@{meetup.OrganizerId}>");

        if (active)
        {
            int remaining = Math.Max(1, (int)Math.Ceiling((meetup.ExpiresAt - now).TotalMinutes));
            lines.Add($"⏳ Noch {remaining} Min. offen – ein Tipp genügt.");
        }
        else
        {
            lines.Add("⌛ Die Anfrage ist abgelaufen.");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Renders user mentions as a bullet-less list, staying inside Discord's field value limit.
    /// </summary>
    private static string FormatMentions(IReadOnlyList<ulong> userIds)
    {
        if (userIds.Count == 0)
        {
            return EmptyValue;
        }

        var lines = new List<string>();
        int length = 0;
        int hidden = 0;

        for (int i = 0; i < userIds.Count; i++)
        {
            string line = $"<@{userIds[i]}>";
            int added = line.Length + (lines.Count > 0 ? 1 : 0);

            if (length + added > FieldValueBudget)
            {
                hidden = userIds.Count - lines.Count;
                break;
            }

            lines.Add(line);
            length += added;
        }

        string value = string.Join('\n', lines);
        return hidden > 0 ? $"{value}\n+{hidden} weitere" : value;
    }
}
