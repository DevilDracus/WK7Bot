using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WK7Bot.Models;
using WK7Bot.Models.BattleNet;
using WK7Bot.Options;
namespace WK7Bot.Services;

public partial class BattleNetService
{

    /// <summary>
    /// Reads the account's World of Warcraft characters and enriches each of them (up to
    /// <see cref="MaxWowCharacters"/>) with level, class, faction, item levels, guild, last login and avatar.
    /// </summary>
    /// <param name="userData">The user data model to populate.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A task tracking the asynchronous operation.</returns>
    private async Task EnrichWowCharactersAsync(BattleNetUserData userData, string accessToken, string region, CancellationToken cancellationToken)
    {
        string url = $"https://{region}.api.blizzard.com/profile/user/wow?namespace=profile-{region}&locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(url, accessToken, cancellationToken), "GetAccountProfileSummary", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Battle.net API returned 403 Forbidden for the WoW account profile (BattleTag: {BattleTag}). "
                    + "The refresh token was likely issued without the 'wow.profile' scope; re-authorize with that scope granted to show WoW characters.",
                    userData.BattleTag);
            }
            else
            {
                _logger.LogWarning(
                    "Battle.net API returned status code {StatusCode} for the WoW account profile (BattleTag: {BattleTag}); skipping WoW characters.",
                    response.StatusCode,
                    userData.BattleTag);
            }

            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<BattleNetWowAccountProfileResponse>(cancellationToken: cancellationToken);

        var characterKeys = (payload?.WowAccounts ?? new List<BattleNetWowAccount>())
            .SelectMany(account => account.Characters ?? new List<BattleNetWowCharacterKeyEntry>())
            .Select(entry => TryParseCharacterKey(entry.Key?.Href))
            .Where(key => key.HasValue)
            .Select(key => key!.Value)
            .Distinct()
            .Take(MaxWowCharacters)
            .ToList();

        if (characterKeys.Count == 0)
        {
            _logger.LogInformation("Battle.net account {BattleTag} has no World of Warcraft characters.", userData.BattleTag);
            return;
        }

        // One failing character must not discard the whole account payload (identity + all other
        // characters + heroes), so every fetch carries its own error boundary.
        var fetchTasks = characterKeys.Select(key => FetchWowCharacterSafelyAsync(key.RealmSlug, key.Name, accessToken, region, cancellationToken));
        var characters = (await Task.WhenAll(fetchTasks)).Where(c => c is not null).Cast<BattleNetWowCharacter>().ToList();

        foreach (var character in characters.OrderByDescending(c => c.Level).ThenByDescending(c => c.LastLoginTimestamp))
        {
            userData.WowCharacters.Add(character);
        }

        var mainAvatar = userData.WowCharacters
            .Select(c => c.AvatarUrl)
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

        if (mainAvatar is not null)
        {
            userData.AvatarUrl = mainAvatar;
        }
    }

    /// <summary>
    /// Retrieves a single World of Warcraft character profile and its character media (avatar art),
    /// converting transport and parsing failures into a null result so the remaining characters of
    /// the same account survive.
    /// </summary>
    /// <param name="realmSlug">The realm slug of the character.</param>
    /// <param name="characterName">The name of the character.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>The parsed character, or null when the profile cannot be read.</returns>
    private async Task<BattleNetWowCharacter?> FetchWowCharacterSafelyAsync(
        string realmSlug,
        string characterName,
        string accessToken,
        string region,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchWowCharacterAsync(realmSlug, characterName, accessToken, region, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not read WoW character {Realm}/{Character}; skipping it.",
                realmSlug,
                characterName);
            return null;
        }
    }

    /// <summary>
    /// Retrieves a single World of Warcraft character profile and its character media (avatar art).
    /// </summary>
    /// <param name="realmSlug">The realm slug of the character.</param>
    /// <param name="characterName">The character name.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>The parsed character, or null when the profile cannot be read.</returns>
    private async Task<BattleNetWowCharacter?> FetchWowCharacterAsync(
        string realmSlug,
        string characterName,
        string accessToken,
        string region,
        CancellationToken cancellationToken)
    {
        string characterUrl =
            $"https://{region}.api.blizzard.com/profile/wow/character/{realmSlug}/{Uri.EscapeDataString(characterName)}"
            + $"?namespace=profile-{region}&locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(characterUrl, accessToken, cancellationToken), "GetCharacterProfile", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Battle.net API returned status code {StatusCode} for WoW character {Realm}/{Character}.",
                response.StatusCode,
                realmSlug,
                characterName);
            return null;
        }

        var summary = await response.Content.ReadFromJsonAsync<BattleNetWowCharacterSummaryResponse>(cancellationToken: cancellationToken);
        if (summary is null)
        {
            return null;
        }

        return new BattleNetWowCharacter
        {
            Name = string.IsNullOrWhiteSpace(summary.Name) ? characterName : summary.Name!,
            RealmSlug = string.IsNullOrWhiteSpace(summary.Realm?.Slug) ? realmSlug : summary.Realm?.Slug,
            Realm = summary.Realm?.Name ?? string.Empty,
            CharacterClass = summary.CharacterClass?.Name ?? string.Empty,
            Faction = summary.Faction?.Name ?? string.Empty,
            Level = summary.Level,
            AverageItemLevel = summary.AverageItemLevel,
            EquippedItemLevel = summary.EquippedItemLevel,
            GuildName = summary.Guild?.Name,
            LastLoginTimestamp = NormalizeUnixTimestamp(summary.LastLoginTimestamp),
            AvatarUrl = await FetchWowCharacterAvatarAsync(realmSlug, characterName, accessToken, region, cancellationToken)
        };
    }

    /// <summary>
    /// Retrieves the character media assets and picks the avatar image (falling back to bust or profile art).
    /// </summary>
    /// <param name="realmSlug">The realm slug of the character.</param>
    /// <param name="characterName">The character name.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region (also the profile namespace).</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>The avatar image URL, or null when media cannot be read.</returns>
    private async Task<string?> FetchWowCharacterAvatarAsync(
        string realmSlug,
        string characterName,
        string accessToken,
        string region,
        CancellationToken cancellationToken)
    {
        string mediaUrl =
            $"https://{region}.api.blizzard.com/profile/wow/character/{realmSlug}/{Uri.EscapeDataString(characterName)}/character-media"
            + $"?namespace=profile-{region}&locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(mediaUrl, accessToken, cancellationToken), "GetCharacterMedia", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "Battle.net API returned status code {StatusCode} for the WoW character media of {Realm}/{Character}.",
                response.StatusCode,
                realmSlug,
                characterName);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<BattleNetWowCharacterMediaResponse>(cancellationToken: cancellationToken);
        var assets = payload?.Assets;
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        foreach (var preferredKey in new[] { "avatar", "bust", "profile" })
        {
            var match = assets.FirstOrDefault(a =>
                string.Equals(a.Key, preferredKey, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(a.Value));

            if (match?.Value is not null)
            {
                return match.Value;
            }
        }

        return null;
    }
}
