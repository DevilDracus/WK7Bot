#pragma warning disable CS1591
#pragma warning disable CS1587
namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using WK7Bot.Core.Entities;

public static class GameNightMessageBuilder
{
    public const string CustomIdPrefix = "game-night";
    public const string ToggleAction = "toggle";

    public static bool IsActive(GameNight gameNight, DateTime now)
        => !gameNight.Closed && gameNight.ClosesAt > now;

    public static string BuildCustomId(int slotId)
        => $"{CustomIdPrefix}:{ToggleAction}:{slotId}";

    public static (Embed Embed, MessageComponent Components) Build(
        GameNight gameNight,
        IReadOnlyList<GameNightSlot> slots,
        IReadOnlyList<GameNightVote> votes,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(gameNight);
        slots ??= Array.Empty<GameNightSlot>();
        votes ??= Array.Empty<GameNightVote>();

        bool active = IsActive(gameNight, now);
        var votesBySlot = votes.GroupBy(v => v.SlotId).ToDictionary(g => g.Key, g => g.Where(v => v.Available).Select(v => v.UserId).ToList());

        var embed = new EmbedBuilder()
            .WithTitle(active ? "🎲 Game Night" : "🎲 Game Night · beendet")
            .WithDescription(BuildDescription(gameNight, active, now))
            .WithColor(active ? Color.Purple : Color.DarkGrey);

        foreach (var slot in slots.OrderBy(s => s.Position).ThenBy(s => s.Id))
        {
            var count = votesBySlot.TryGetValue(slot.Id, out var list) ? list.Count : 0;
            var voters = votesBySlot.TryGetValue(slot.Id, out var l) ? l : [];
            embed.AddField($"{slot.Label} ({count})", FormatMentions(voters), false);
        }

        if (slots.Count == 0)
        {
            embed.AddField("Slots", "Noch keine Slots – füge welche hinzu.", false);
        }

        embed.WithFooter(active
            ? $"Offen bis {gameNight.ClosesAt:dd.MM. HH:mm} Uhr"
            : $"Beendet • bester Slot: {gameNight.BestSlot ?? "—"}");

        var componentBuilder = new ComponentBuilder();
        if (active)
        {
            int row = 0;
            foreach (var slot in slots.OrderBy(s => s.Position).ThenBy(s => s.Id))
            {
                var count = votesBySlot.TryGetValue(slot.Id, out var list) ? list.Count : 0;
                componentBuilder = componentBuilder.WithButton(
                    $"{slot.Label} ({count})",
                    BuildCustomId(slot.Id),
                    ButtonStyle.Primary,
                    row: Math.Min(row++ / 5, 4));
            }
        }

        return (embed.Build(), componentBuilder.Build());
    }

    public static (Embed Embed, MessageComponent Components) Build(
        GameNight gameNight,
        IReadOnlyList<GameNightSlot> slots,
        DateTime now)
        => Build(gameNight, slots, Array.Empty<GameNightVote>(), now);

    public static (Embed Embed, MessageComponent Components) Build(
        GameNight gameNight,
        IReadOnlyList<GameNightVote> votes,
        DateTime now)
        => Build(gameNight, gameNight.Slots?.ToList() ?? [], votes, now);

    private static string BuildDescription(GameNight gameNight, bool active, DateTime now)
    {
        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(gameNight.Title))
        {
            lines.Add($"**{gameNight.Title}**");
        }
        lines.Add($"Organisiert von <@{gameNight.OrganizerId}>");
        if (active)
        {
            int remaining = Math.Max(1, (int)Math.Ceiling((gameNight.ClosesAt - now).TotalHours));
            lines.Add($"⏳ Noch {remaining} Std. offen – tippe auf einen Slot.");
        }
        else
        {
            lines.Add("⌛ Die Umfrage ist beendet.");
        }
        return string.Join('\n', lines);
    }

    private static string FormatMentions(IReadOnlyList<ulong> userIds)
    {
        if (userIds.Count == 0) return "—";
        return string.Join(", ", userIds.Select(u => $"<@{u}>"));
    }
}
