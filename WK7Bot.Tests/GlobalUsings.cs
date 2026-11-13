// Resolve the common Options.Create ambiguity between Microsoft.Extensions.Options.Options
// and the WK7Bot.Options namespace by aliasing the option types used across the test project.
global using AlexaNotificationOptions = WK7Bot.Options.AlexaNotificationOptions;
global using DiscordBattleNetMappingOptions = WK7Bot.Options.DiscordBattleNetMappingOptions;
global using DiscordSteamMappingOptions = WK7Bot.Options.DiscordSteamMappingOptions;
global using DwdWarningOptions = WK7Bot.Options.DwdWarningOptions;
global using FeatureKeys = WK7Bot.Options.FeatureKeys;
global using FeatureOptions = WK7Bot.Options.FeatureOptions;
global using ServersOptions = WK7Bot.Options.ServersOptions;
global using WeekendDigestOptions = WK7Bot.Options.WeekendDigestOptions;
global using Wk7BotOptions = WK7Bot.Options.Wk7BotOptions;
