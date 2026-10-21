namespace WK7Bot.Core.Utilities;

using System.Globalization;
using System.Text;
using Discord;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Renders a member profile as a Discord Components V2 card: one container with a text header, a
/// section carrying the Steam avatar as an accessory, and separated text blocks for the Steam and
/// Battle.net sections. Components V2 messages carry no embeds or content, so every line lives in a
/// <see cref="TextDisplayBuilder"/>.
/// </summary>
public static class ProfileCardBuilder
{
    /// <summary>Discord's per-text-display character ceiling.</summary>

    /// <summary>Accent colour of the profile container.</summary>
    private static readonly Color AccentColor = new(0x9B, 0x59, 0xB6);

    /// <summary>Builds the profile card.</summary>
    /// <param name="profile">The aggregated profile data.</param>
    /// <returns>The message components to send with <see cref="MessageFlags.ComponentsV2"/>.</returns>
    public static MessageComponent Build(ProfileResult profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var container = new ContainerBuilder().WithAccentColor(AccentColor);
        container.AddComponent(new TextDisplayBuilder().WithContent(EmbedText.TruncateHard($"# 🎮 {profile.DisplayName}", CardBuilder.MaxTextDisplayLength)));

        if (profile.Steam is not null)
        {
            AddSteamSection(container, profile.Steam);
        }

        if (profile.BattleNet is not null)
        {
            AddBattleNetSection(container, profile.BattleNet);
        }

        return new ComponentBuilderV2().AddComponent(container).Build();
    }

    /// <summary>Adds the Steam block: a section with the avatar accessory plus an optional games list.</summary>
    /// <param name="container">The container to extend.</param>
    /// <param name="steam">The Steam section data.</param>
    private static void AddSteamSection(ContainerBuilder container, SteamProfileSection steam)
    {
        container.AddComponent(new SeparatorBuilder().WithIsDivider(true).WithSpacing(SeparatorSpacingSize.Small));

        var header = new StringBuilder();
        header.AppendLine($"**Steam** · {steam.PersonaName}");

        if (!string.IsNullOrWhiteSpace(steam.PersonaState))
        {
            header.AppendLine($"Status: {steam.PersonaState}");
        }

        if (!string.IsNullOrWhiteSpace(steam.CurrentGameTitle))
        {
            header.AppendLine($"🎮 Spielt gerade: **{steam.CurrentGameTitle}**");
        }

        header.Append($"⏱️ 14 Tage: {steam.PlaytimeDisplay}");

        if (steam.AchievementsTotal > 0)
        {
            header.Append($" · 🏆 {steam.AchievementsUnlocked}/{steam.AchievementsTotal}");
        }

        var text = new TextDisplayBuilder().WithContent(EmbedText.TruncateHard(header.ToString(), CardBuilder.MaxTextDisplayLength));

        if (!string.IsNullOrWhiteSpace(steam.AvatarUrl))
        {
            container.AddComponent(new SectionBuilder()
                .AddComponent(text)
                .WithAccessory(new ThumbnailBuilder().WithMedia(new UnfurledMediaItemProperties(steam.AvatarUrl))));
        }
        else
        {
            container.AddComponent(text);
        }

        if (steam.TopRecentGames.Count == 0)
        {
            return;
        }

        var games = new StringBuilder();
        games.AppendLine("**Zuletzt gespielt**");

        foreach (var game in steam.TopRecentGames)
        {
            games.Append($"• {game.Name} — {SteamUserData.FormatPlaytime(game.PlaytimeTwoWeeksMinutes)}");
            if (game.AchievementsTotal > 0)
            {
                games.Append($" · 🏆 {game.AchievementsUnlocked}/{game.AchievementsTotal}");
            }

            games.AppendLine();
        }

        container.AddComponent(new TextDisplayBuilder().WithContent(EmbedText.TruncateHard(games.ToString().TrimEnd(), CardBuilder.MaxTextDisplayLength)));
    }

    /// <summary>Adds the Battle.net block with the World of Warcraft characters and Diablo heroes.</summary>
    /// <param name="container">The container to extend.</param>
    /// <param name="battleNet">The Battle.net section data.</param>
    private static void AddBattleNetSection(ContainerBuilder container, BattleNetProfileSection battleNet)
    {
        container.AddComponent(new SeparatorBuilder().WithIsDivider(true).WithSpacing(SeparatorSpacingSize.Small));

        var region = string.IsNullOrWhiteSpace(battleNet.Region) ? string.Empty : $" · {battleNet.Region.ToUpperInvariant()}";
        container.AddComponent(new TextDisplayBuilder().WithContent(EmbedText.TruncateHard($"**Battle.net** · {battleNet.BattleTag}{region}", CardBuilder.MaxTextDisplayLength)));

        if (battleNet.WowCharacters.Count > 0)
        {
            var characters = new StringBuilder();
            characters.AppendLine("**WoW-Charaktere**");

            foreach (var character in battleNet.WowCharacters)
            {
                var main = character.IsMain ? " ⭐" : string.Empty;
                var guild = string.IsNullOrWhiteSpace(character.GuildName) ? string.Empty : $" · <{character.GuildName}>";
                var itemLevel = character.AverageItemLevel > 0
                    ? $" · iLvl {character.AverageItemLevel.ToString("F0", CultureInfo.InvariantCulture)}"
                    : string.Empty;

                characters.AppendLine($"• **{character.Name}**{main} — Stufe {character.Level} {character.CharacterClass} ({character.Faction}) · {character.Realm}{itemLevel}{guild}");
            }

            container.AddComponent(new TextDisplayBuilder().WithContent(EmbedText.TruncateHard(characters.ToString().TrimEnd(), CardBuilder.MaxTextDisplayLength)));
        }

        if (battleNet.DiabloHeroes.Count > 0)
        {
            var heroes = new StringBuilder();
            heroes.AppendLine("**Diablo-Helden**");

            foreach (var hero in battleNet.DiabloHeroes)
            {
                var score = hero.ParagonLevel > 0 ? $"Paragon {hero.ParagonLevel}" : $"Stufe {hero.Level}";
                var flags = new List<string>();
                if (hero.Seasonal)
                {
                    flags.Add("Season");
                }

                if (hero.Hardcore)
                {
                    flags.Add(hero.Dead ? "Hardcore †" : "Hardcore");
                }

                var suffix = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : string.Empty;
                heroes.AppendLine($"• **{hero.Name}** — {hero.HeroClass} · {score}{suffix}");
            }

            container.AddComponent(new TextDisplayBuilder().WithContent(EmbedText.TruncateHard(heroes.ToString().TrimEnd(), CardBuilder.MaxTextDisplayLength)));
        }
    }
}
