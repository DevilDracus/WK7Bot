using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WK7Bot.Models;
using WK7Bot.Models.BattleNet;
using WK7Bot.Options;
namespace WK7Bot.Services;

public partial class BattleNetService
{

    /// <summary>
    /// Reads the Diablo III heroes of the account. Blizzard does not publish a Diablo IV profile API,
    /// so this section stays empty for D4-only accounts.
    /// </summary>
    /// <param name="userData">The user data model to populate.</param>
    /// <param name="accessToken">The user access token.</param>
    /// <param name="region">The API region.</param>
    /// <param name="cancellationToken">A cancellation token to monitor for task cancellation.</param>
    /// <returns>A task tracking the asynchronous operation.</returns>
    private async Task EnrichDiabloHeroesAsync(BattleNetUserData userData, string accessToken, string region, CancellationToken cancellationToken)
    {
        string url = $"https://{region}.api.blizzard.com/d3/profile/{Uri.EscapeDataString(userData.BattleTag)}/?locale={Locale}";

        using var response = await SendBnetApiWithRetryAsync(() => SendAuthorizedAsync(url, accessToken, cancellationToken), "GetDiabloProfile", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Battle.net API returned 403 Forbidden for the Diablo III profile of {BattleTag}. "
                    + "The refresh token was likely issued without the 'd3.profile' scope; re-authorize with that scope granted to show Diablo heroes.",
                    userData.BattleTag);
            }
            else
            {
                _logger.LogWarning(
                    "Battle.net API returned status code {StatusCode} for the Diablo III profile of {BattleTag}; skipping Diablo heroes.",
                    response.StatusCode,
                    userData.BattleTag);
            }

            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<BattleNetDiabloProfileResponse>(cancellationToken: cancellationToken);
        var heroes = payload?.Heroes;
        if (heroes is null || heroes.Count == 0)
        {
            return;
        }

        userData.DiabloHeroes = heroes
            .Select(hero => new BattleNetDiabloHero
            {
                Name = hero.Name ?? string.Empty,
                HeroClass = hero.Class ?? string.Empty,
                Level = hero.Level ?? 0,
                ParagonLevel = hero.ParagonLevel ?? 0,
                Seasonal = hero.Seasonal,
                Hardcore = hero.Hardcore,
                Dead = hero.Dead
            })
            .ToList();
    }
}
