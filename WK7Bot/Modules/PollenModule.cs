namespace WK7Bot.Modules;

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using WK7Bot.Core.Utilities;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Shows the DWD pollen hazard index for the configured region together with the UV index of the
/// configured city.
/// </summary>
public class PollenModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IPollenService _pollenService;
    private readonly ILogger<PollenModule> _logger;

    /// <summary>Initializes a new instance of the <see cref="PollenModule"/> class.</summary>
    /// <param name="pollenService">The DWD pollen/UV service.</param>
    /// <param name="logger">The logger instance.</param>
    public PollenModule(IPollenService pollenService, ILogger<PollenModule> logger)
    {
        _pollenService = pollenService ?? throw new ArgumentNullException(nameof(pollenService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Posts the current pollen and UV forecast.</summary>
    /// <returns>A task representing the command response operation.</returns>
    [SlashCommand("pollen", "Zeigt Pollenflug und UV-Index von heute bis übermorgen.")]
    public async Task PollenAsync()
    {
        await DeferAsync(ephemeral: true);

        var pollen = await _pollenService.GetPollenAsync();
        var uv = await _pollenService.GetUvAsync();

        var embed = PollenEmbedBuilder.Build(pollen, uv);
        if (embed is null)
        {
            _logger.LogWarning("Pollen and UV feeds were both unavailable for /pollen.");
            await FollowupAsync("❌ Der DWD-Pollenflug-Index ist derzeit nicht erreichbar.", ephemeral: true);
            return;
        }

        await FollowupAsync(components: CardBuilder.FromEmbed(embed), flags: MessageFlags.ComponentsV2, ephemeral: true);
    }
}
