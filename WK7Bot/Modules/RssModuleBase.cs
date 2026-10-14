using Discord.Interactions;

namespace WK7Bot.Modules;

/// <summary>
/// Abstract base for all RSS-related interaction modules so the <c>/rss</c> slash-command group
/// is declared exactly once and inherited by every concrete submodule.
/// </summary>
[Group("rss", "Befehle zur Verwaltung von RSS-Newsfeeds und Abonnements")]
public abstract class RssModuleBase : InteractionModuleBase<SocketInteractionContext>
{
}
