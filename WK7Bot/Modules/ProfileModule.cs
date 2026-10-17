namespace WK7Bot.Modules;

using System.Text;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Rich profile card command for linked members: Steam level, playtime, achievements plus the
/// World of Warcraft characters and Diablo heroes of the linked Battle.net account.
/// </summary>
public class ProfileModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxEmbedFields = 25;
    private const int MaxFieldValueLength = 1024;

    private readonly IProfileService _profileService;
    private readonly ILogger<ProfileModule> _logger;

    /// <summary>Initializes a new instance of the <see cref="ProfileModule"/> class.</summary>
    /// <param name="profileService">The profile aggregation service.</param>
    /// <param name="logger">The logger instance.</param>
    public ProfileModule(IProfileService profileService, ILogger<ProfileModule> logger)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Posts the profile card of the invoking member (or of the mentioned member).
    /// </summary>
    /// <param name="user">The member to show; defaults to the invoking user.</param>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("profile", "Zeigt eine Profilkarte mit Steam- und Battle.net-Daten.")]
    [RequireContext(ContextType.Guild)]
    public async Task ProfileAsync([Summary("user", "Mitglied, dessen Profil gezeigt werden soll")] IUser? user = null)
    {
        await DeferAsync();

        var target = user ?? Context.User;
        var result = await _profileService.GetAsync(target.Id, Context.Guild.Id);

        if (result.Steam is null && result.BattleNet is null)
        {
            await FollowupAsync(
                $"Für **{target.Username}** ist kein Spielkonto verknüpft. Nutze `/link-steam` oder `/link-battlenet`.",
                ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"🎮 Profil — {result.DisplayName}")
            .WithColor(Color.Purple)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("WK7 Bot • Profil");

        if (result.Steam is not null)
        {
            AddSteamFields(embed, result.Steam);
        }

        if (result.BattleNet is not null)
        {
            AddBattleNetFields(embed, result.BattleNet);
        }

        await FollowupAsync(embed: embed.Build());
    }

    /// <summary>Adds the Steam section fields to the embed.</summary>
    private static void AddSteamFields(EmbedBuilder embed, SteamProfileSection steam)
    {
        if (!string.IsNullOrWhiteSpace(steam.AvatarUrl))
        {
            embed.WithThumbnailUrl(steam.AvatarUrl);
        }

        var header = new StringBuilder();
        header.AppendLine($"**{steam.PersonaName}**");
        header.AppendLine($"Status: {steam.PersonaState}");

        if (!string.IsNullOrWhiteSpace(steam.CurrentGameTitle))
        {
            header.AppendLine($"🎮 Spielt gerade: **{steam.CurrentGameTitle}**");
        }

        header.Append($"⏱️ 14 Tage: {steam.PlaytimeDisplay}");

        if (steam.AchievementsTotal > 0)
        {
            header.Append($" · 🏆 {steam.AchievementsUnlocked}/{steam.AchievementsTotal}");
        }

        embed.AddField("🎮 Steam", EmbedText.Truncate(header.ToString(), MaxFieldValueLength), inline: false);

        if (steam.TopRecentGames.Count == 0 || embed.Fields.Count >= MaxEmbedFields - 1)
        {
            return;
        }

        var games = steam.TopRecentGames.Select(game =>
        {
            var line = $"**{game.Name}** — {SteamUserData.FormatPlaytime(game.PlaytimeTwoWeeksMinutes)}";
            if (game.AchievementsTotal > 0)
            {
                line += $" · 🏆 {game.AchievementsUnlocked}/{game.AchievementsTotal}";
            }

            return line;
        });

        embed.AddField("🕹️ Zuletzt gespielt", EmbedText.Truncate(string.Join('\n', games), MaxFieldValueLength), inline: false);
    }

    /// <summary>Adds the Battle.net section fields to the embed.</summary>
    private static void AddBattleNetFields(EmbedBuilder embed, BattleNetProfileSection battleNet)
    {
        var region = string.IsNullOrWhiteSpace(battleNet.Region) ? string.Empty : $" · {battleNet.Region.ToUpperInvariant()}";
        embed.AddField("⚔️ Battle.net", EmbedText.Truncate($"**{battleNet.BattleTag}**{region}", MaxFieldValueLength), inline: false);

        if (battleNet.WowCharacters.Count > 0 && embed.Fields.Count < MaxEmbedFields - 1)
        {
            var characters = battleNet.WowCharacters.Select(character =>
            {
                var main = character.IsMain ? " ⭐" : string.Empty;
                var guild = string.IsNullOrWhiteSpace(character.GuildName) ? string.Empty : $" · <{character.GuildName}>";
                var itemLevel = character.AverageItemLevel > 0 ? $" · iLvl {character.AverageItemLevel:F0}" : string.Empty;
                return $"**{character.Name}**{main} — Stufe {character.Level} {character.CharacterClass} ({character.Faction}) · {character.Realm}{itemLevel}{guild}";
            });

            embed.AddField("⚔️ WoW-Charaktere", EmbedText.Truncate(string.Join('\n', characters), MaxFieldValueLength), inline: false);
        }

        if (battleNet.DiabloHeroes.Count > 0 && embed.Fields.Count < MaxEmbedFields)
        {
            var heroes = battleNet.DiabloHeroes.Select(hero =>
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
                return $"**{hero.Name}** — {hero.HeroClass} · {score}{suffix}";
            });

            embed.AddField("😈 Diablo-Helden", EmbedText.Truncate(string.Join('\n', heroes), MaxFieldValueLength), inline: false);
        }
    }
}
