# WK7Bot

A modular Discord automation bot and Home Assistant Add-On built on .NET 10. WK7Bot integrates Discord slash commands, MQTT event pipelines, Steam Rich Presence monitoring, RSS feed syndication, AI-generated seasonal food and renal-safe recipes, and local utility schedule automation into a unified, extensible background framework.

[![Add repository to Home Assistant][repository-badge]][repository-url]

## Overview

WK7Bot runs as a standalone containerized service or as a native Home Assistant Supervisor Add-On. It uses strongly-typed `IOptions<T>` configuration, hosted background services, and asynchronous event pipelines to handle real-time notifications, Home Assistant automation triggers, and community interactions.

## Key Features

### Discord Integration & Slash Commands
- **`Discord.Interactions` framework** built on `Discord.Net` with reflection-based command module auto-registration (`InteractionHandlingService`).
- **Guild & global commands** – a `Discord:TestGuildId` (or root `TestGuildId`) setting enables instant guild-scoped registration for development; otherwise commands register globally.
- **Interactive UI** – select menus and role management for RSS subscription dashboards.
- **Duplicate protection** – `/rss add` rejects a name that already exists before creating channels/roles.

### RSS Feed Syndication
- `RssPollingBackgroundService` polls configured feeds every 5 minutes and publishes rich embeds to dedicated per-feed channels.
- **First-poll baseline seeding** – when a feed has never been polled, the newest item is recorded silently; the historical backlog is *not* posted to Discord.
- Duplicate suppression via `LastItemGuid` / `LastPublishedDate` tracking (pure logic in `FeedDeltaCalculator`).
- `/rss` commands to add/remove feeds (auto-creating a role + private read-only channel) and post a subscription dashboard.
- Interactive select menu (`RssComponentModule`) for users to subscribe/unsubscribe.
- Descriptions are HTML-stripped, entity-decoded, and truncated to 500 characters (`FeedTextFormatter`).

### Leipzig Waste Collection Reminders
- Downloads and parses `.ics` calendar streams from *Stadtreinigung Leipzig* (UTF-8 BOM and leading whitespace tolerated).
- Maps waste categories (*Restabfall*, *Papier*, *Wertstoffe*, *Biogut*) to German display labels and color-coded visuals (`WasteSummaryMapper`).
- Daily confirmation + Monday weekly overview reminders (`LeipzigWasteBackgroundService`) in a `🗑️leipzig-waste` channel.
- **Restart-safe deduplication** – every daily/weekly dispatch per guild is recorded in the `WasteDispatchLogs` table (`WasteDispatchRepository`), so restarting the bot never re-sends today's messages; guilds whose send failed are retried on the next tick, while successful guilds stay suppressed.
- **ICS download caching** – a successfully validated calendar is cached for 1 hour (`IMemoryCache`), so weekly overviews and multi-day `/check-waste` ranges reuse one HTTP download instead of fetching the feed per date; failures are never cached.
- On-demand lookups via the `/check-waste` slash command.

### Steam Rich Presence Enrichment
- Maps Discord user IDs to Steam IDs (`DiscordSteamMappings`).
- Fetches player summaries (including the full-size Steam avatar, exposed as `SteamAvatarUrl`), recently played games, and enriched achievement data (icons, hidden flag + unlock timestamps) from the Steam Web API with a 24h schema cache.
- **Display-friendly extras** – `SteamUserData.PlaytimeDisplay` formats the 14-day playtime in German (`15,8 Std.` / `45 Min.` / `0 Std.`), every recent game carries `AchievementsUnlocked`/`AchievementsTotal` progress counters (derived from the cached schema), and the currently active game gets the same counters (`CurrentGameAchievementsUnlocked`/`CurrentGameAchievementsTotal`).
- **Resilient achievement fetching** – achievement-endpoint calls are limited to 2 concurrent requests and retried with linear backoff (up to 3 attempts) on `429`/`5xx`; other errors (e.g. `400` for games without stats) fail fast without retry.
- **Privacy fallback** – when `GetPlayerAchievements` returns `403` (target user's *Game details* setting is friends-only/private, which the Web API refuses), the bot falls back to `IPlayerService/GetTopAchievementsForGames`, which still returns the user's unlocked achievements (name, description, icon, global unlock percentage via `player_percent_unlocked`) — just without unlock timestamps, so those achievements sort after timestamped ones.
- **No cache poisoning** – only *successful* schema responses are cached for 24h; failures are never cached, so transient Steam errors self-heal on the next fetch.
- **Fetch cooldown** – `SteamDataCache` serves Steam data from a 60s per-user cache, so Discord presence-update storms hit Steam at most once per minute per user.
- **Diagnostics** – previously silent failure paths (no games to fetch, non-success responses, `playerstats.success=false` with Steam's error string) are logged, and per-game unlocked-achievement counts are logged at debug level. A `403` from `GetPlayerAchievements` is logged with an actionable hint (the target user's *Game details* privacy setting is likely not Public — friends-only is not enough for the Web API) plus the response body snippet, and a follow-up log reports how many achievements the fallback recovered.

### AI Food & Recipe Automation (Google Gemini)
- `GeminiFoodService` calls the Google Gemini `generateContent` API using JSON-schema structured output to produce German-language seasonal produce lists and dialysis / kidney-transplant-safe weekly recipes.
- **Model fallback & resilience** – a primary model is tried first and a lite fallback model second; transient `429`/`503` responses are retried with exponential backoff, and a missing `gemini_api_key` short-circuits before any HTTP call.
- `/recipe` and `/seasonal-produce` post rich embeds (ingredients, steps, qualitative diet tags such as *Kaliumarm*/*Phosphatarm*, renal safety notes, German month names) to the `#🍎food` channel.
- `FoodPublisherService` publishes automatically: the monthly produce calendar on the 1st at 09:00 and the weekly recipe on Thursdays at 15:30.
- Gated by the `food_service_enabled` feature flag.

### Home Assistant & MQTT Integration
- `HomeAssistantNotifierService` registers every writable Discord text channel and configured DM user as an MQTT `notify` entity using Home Assistant Auto-Discovery.
- Sending to `homeassistant/notify/wk7_<channelId>/set` (or `wk7_dm_<userId>/set`) pushes a message to the target Discord channel/DM.
- `DiscordPresenceMqttService` publishes live user status/activity as MQTT sensors (`wk7bot/presence/users/<userId>`), enriched with Steam data when enabled. The payload carries the full `SteamData` object plus flattened top-level attributes (`CurrentGameTitle`, `CurrentGameAppId`, `PlaytimeLastTwoWeeksMinutes`, `PlaytimeDisplay`, `PersonaState`, `SteamAvatarUrl`) so Lovelace cards can reference them directly — the dashboard view is kept in `HASS.yaml` itself (`wk7` view, regenerated from the local-only `HASS_DiscordView.yaml` and spliced in place, so no manual paste is needed): presence cards with the Discord state on the main button plus a Discord-header-style Steam identity row (avatar on the left, `<steamname> (Steam)` name, online status painted underneath by CSS), game/playtime shortcut rows (the Spielzeit row opens the detail popup — there is no separate `>` button anymore), and detail popups whose markup deliberately only uses what Home Assistant's markdown sanitiser keeps (`<img width height>`, base64 data-URI brand glyphs, `<font>` status chips, `ha-alert`, the Font Awesome `<ha-icon icon="fab:discord">` logo — never `style=` attributes, inline `<svg>`, or non-existent icon names such as `mdi:discord`). The activity box shows the currently played game's art via Steam's `header.jpg` (230x107) and falls back to the Discord-provided `GameThumbnailUrl` when Steam has no app id for it. Popup headers are emitted as a single line joined with explicit `<br>` because `marked` swallows raw line breaks inside HTML blocks, which is what collapsed the header into one squished line.
- `AlexaMentionNotificationService` forwards mentions of a target user to the getnotify.me Alexa notification API.
- Comes with Supervisor configuration for running as a native Add-On.

## Technology Stack

- **Framework:** .NET 10 (C# 14)
- **Discord:** `Discord.Net`, `Discord.Interactions`
- **Persistence:** Entity Framework Core with SQLite (stored at `/data/wk7bot.db` in the Add-On)
- **Messaging:** `MQTTnet`, HTTP client pipelines
- **Parsing:** `CodeHollow.FeedReader`, `Ical.Net`
- **AI:** Google Gemini `generateContent` (structured JSON schema, primary + fallback model)
- **Testing:** xUnit, Moq, EF Core InMemory
- **Deployment:** Docker, Home Assistant Supervisor Add-On

## Project Structure

```text
WK7Bot
├── Core
│   ├── Entities
│   │   ├── RssDashboardSetting.cs
│   │   ├── RssFeed.cs
│   │   └── WasteDispatchLog.cs                # Persisted per-guild waste dispatch records
│   ├── Interfaces
│   │   ├── IRssRepository.cs
│   │   └── IWasteDispatchRepository.cs
│   └── Utilities
│       ├── DietTagFormatter.cs                # Diet-tag identifiers → German embed labels
│       ├── FeedDeltaCalculator.cs           # Pure RSS baseline/new-item decisions
│       ├── FeedTextFormatter.cs             # HTML strip + truncation for embeds
│       ├── NameSanitizer.cs                 # Channel slugs + MQTT-safe names
│       └── WasteSummaryMapper.cs            # ICS summary → German display label
├── Extensions
│   └── ServiceCollectionExtensions.cs       # DI wiring for DB, Discord, HTTP clients, hosted services
├── Infrastructure
│   └── Data
│       ├── BotDbContext.cs
│       ├── RssRepository.cs
│       └── WasteDispatchRepository.cs
├── Models
│   ├── FoodModels.cs                         # Seasonal produce + renal recipe models (Gemini schema)
│   ├── SteamAchievement.cs
│   ├── SteamRecentGame.cs
│   ├── SteamUserData.cs
│   └── UserPresenceEntity.cs
├── Modules
│   ├── FoodModule.cs                         # /recipe and /seasonal-produce slash commands
│   ├── LeipzigWasteModule.cs
│   ├── RssCommandsModule.cs
│   ├── RssComponentModule.cs
│   └── SystemModule.cs
├── Options
│   ├── AlexaNotificationOptions.cs
│   ├── DiscordSteamMappingOptions.cs
│   ├── FeatureOptions.cs                    # All toggles default to enabled
│   └── Wk7BotOptions.cs
├── Services
│   ├── Interfaces
│   │   ├── IGeminiFoodService.cs
│   │   ├── IHomeAssistantService.cs
│   │   ├── ILeipzigWasteService.cs
│   │   └── ISteamService.cs
│   ├── AlexaMentionNotificationService.cs
│   ├── DiscordBotWorker.cs
│   ├── DiscordPresenceMqttService.cs
│   ├── FoodPublisherService.cs               # Scheduled Gemini posts to #🍎food
│   ├── GeminiFoodService.cs                  # Gemini REST client with model fallback
│   ├── HomeAssistantNotifierService.cs
│   ├── HomeAssistantService.cs
│   ├── InteractionHandlingService.cs
│   ├── LeipzigWasteBackgroundService.cs
│   ├── LeipzigWasteService.cs
│   ├── RssParserService.cs
│   ├── RssPollingBackgroundService.cs
│   ├── SteamDataCache.cs                     # 60s per-user Steam fetch cooldown
│   └── SteamService.cs
├── Program.cs                               # Host bootstrap, options binding, /health endpoint
├── appsettings.json
├── appsettings.Development.json
├── config.yaml                              # Home Assistant Add-On configuration
├── Dockerfile
└── WK7Bot.csproj

WK7Bot.Tests                                 # xUnit test project (included in WK7Bot.sln)
├── DietTagFormatterTests.cs
├── FeedDeltaCalculatorTests.cs
├── FeedTextFormatterTests.cs
├── FoodModuleTests.cs                       # Slash-command behaviour incl. embed content
├── FormattingUtilityTests.cs
├── GeminiFoodServiceTests.cs                # Gemini envelope parsing, fallback, retries
├── LeipzigWasteBackgroundServiceTests.cs    # Restart dedupe, daily/weekly kinds, partial-failure retry
├── LeipzigWasteServiceTests.cs              # ICS parsing, mapping, feed download caching
├── NameSanitizerTests.cs
├── OptionsBindingTests.cs
├── RssRepositoryTests.cs
├── ServiceCollectionExtensionsTests.cs
├── SteamDataCacheTests.cs                    # Cooldown caching, TTL expiry, null caching
├── SteamServiceTests.cs                      # API mapping, retry/backoff, schema cache, log assertions
├── WasteDispatchRepositoryTests.cs           # Dispatch state persistence (EF InMemory)
└── WasteSummaryMapperTests.cs
```

## Slash Commands

| Command | Description | Permission |
| --- | --- | --- |
| `/rss add <name> <url>` | Registers a feed, creates a role + private read-only channel (rejects duplicate names) | `ManageChannels` |
| `/rss remove <name>` | Removes a feed and cleans up its channel + role | `ManageChannels` |
| `/rss dashboard` | Posts the interactive subscription select menu | `ManageRoles` |
| `/check-waste [days] [datum]` | Queries Leipzig waste collection dates (`DD.MM.YYYY` or `YYYY-MM-DD`) | everyone |
| `/recipe` | Generates a seasonal renal & transplant-safe recipe and posts it to `#🍎food` | everyone |
| `/seasonal-produce [month]` | Posts the seasonal fruit/vegetable/herb/nut calendar (month 1-12, default: current) to `#🍎food` | everyone |
| `/ping` | Gateway latency test | everyone |
| `/ha-status` | Tests Home Assistant Supervisor API connectivity | everyone |

Both food commands are ephemeral while Gemini is queried, post the result embed into `#🍎food`, and report a clear error when the channel or the Gemini API key is missing.

## Hosted Services

| Hosted Service | Purpose | Feature flag |
| --- | --- | --- |
| `DiscordBotWorker` | Logs in and connects the Discord gateway, forwards `Discord.Net` logs | always on |
| `InteractionHandlingService` | Auto-registers command modules and routes slash command interactions | always on |
| `RssPollingBackgroundService` | Polls RSS feeds and posts update embeds to Discord | `rss_polling_enabled` |
| `HomeAssistantNotifierService` | Bridges MQTT notify commands to Discord channels / DMs | `home_assistant_notifier_enabled` |
| `LeipzigWasteBackgroundService` | Sends daily and weekly waste collection reminders | `leipzig_waste_enabled` |
| `AlexaMentionNotificationService` | Forwards target-user mentions to the Alexa notification API | `alexa_notifications_enabled` |
| `DiscordPresenceMqttService` | Publishes Discord presence snapshots to MQTT for Home Assistant | `discord_presence_mqtt_enabled` |
| `FoodPublisherService` | Posts the monthly produce calendar and weekly recipe to `#🍎food` via Gemini | `food_service_enabled` |

Feature flags are resolved from the `Wk7Bot:features` section when present (appsettings.json), otherwise from root-level `features` (Home Assistant `options.json`). Services re-check the bound options at runtime, so disabling a flag always takes effect.

## Configuration

Configuration is bound from `appsettings.json`, environment variables, or the Home Assistant Add-On `config.yaml`. The `Wk7Bot` section in `appsettings.json` (and root-level keys in `config.yaml`/`options.json`) is read as `Wk7BotOptions`. All option properties use `ConfigurationKeyName` snake_case aliases to match the Add-On schema.

### Home Assistant `config.yaml`

```yaml
discord_token: "YOUR_DISCORD_BOT_TOKEN"
mqtt_host: "core-mosquitto"
mqtt_port: 1883
mqtt_username: ""
mqtt_password: ""
steam_api_key: ""
gemini_api_key: ""                             # Google AI Studio key for /recipe and /seasonal-produce
discord_steam_mappings:
  - discord_user_id: "123456789012345678"
    steam_id: "76561198000000000"
discord_dm_user_ids:
  - "123456789012345678"
alexa_notification:
  target_user_id: "YOUR_DISCORD_USER_ID"
  api_token: "YOUR_GETNOTIFY_TOKEN"
  api_secret: "YOUR_GETNOTIFY_SECRET"
  endpoint_url: "https://api.getnotify.me/v1/notify"
features:
  rss_polling_enabled: true
  home_assistant_notifier_enabled: true
  leipzig_waste_enabled: true
  alexa_notifications_enabled: true
  discord_presence_mqtt_enabled: true
  steam_presence_enabled: true
  food_service_enabled: true
```

### `appsettings.json` (same keys under `Wk7Bot`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/data/wk7bot.db"
  },
  "LeipzigWaste": {
    "IcsFeedUrl": "https://stadtreinigung-leipzig.de/..."
  },
  "Wk7Bot": {
    "discord_token": "YOUR_DISCORD_BOT_TOKEN",
    "mqtt_host": "core-mosquitto",
    "mqtt_port": 1883,
    "steam_api_key": "YOUR_STEAM_WEB_API_KEY",
    "gemini_api_key": "YOUR_GOOGLE_AI_STUDIO_KEY",
    "discord_steam_mappings": [
      {
        "discord_user_id": "123456789012345678",
        "steam_id": "76561198000000000"
      }
    ],
    "discord_dm_user_ids": [ "123456789012345678" ],
    "alexa_notification": {
      "target_user_id": "YOUR_DISCORD_USER_ID",
      "api_token": "YOUR_GETNOTIFY_TOKEN",
      "api_secret": "YOUR_GETNOTIFY_SECRET",
      "endpoint_url": "https://api.getnotify.me/v1/notify"
    },
    "features": {
      "rss_polling_enabled": true,
      "home_assistant_notifier_enabled": true,
      "leipzig_waste_enabled": true,
      "alexa_notifications_enabled": true,
      "discord_presence_mqtt_enabled": true,
      "steam_presence_enabled": true,
      "food_service_enabled": true
    }
  }
}
```

If no `Wk7Bot` section exists, `Program.cs` binds `Wk7BotOptions` from the configuration **root** instead — this is what the shipped `appsettings.json` does (it only carries logging, the connection string, and the Leipzig ICS URL). In that case all bot options come from environment variables or the Home Assistant `/data/options.json`.

Secrets may also be supplied via environment variables (`DISCORD_BOT_TOKEN`, `SUPERVISOR_TOKEN`, etc.) or .NET user secrets.

### Important environment variables

| Variable | Used by | Purpose |
| --- | --- | --- |
| `DISCORD_BOT_TOKEN` | `DiscordBotWorker` | Fallback token when `discord_token` is empty |
| `SUPERVISOR_TOKEN` | `HomeAssistantService` | Bearer token for Supervisor-proxied HA API |
| `ASPNETCORE_ENVIRONMENT` | Host | Set to `Development` for local runs |

## Getting Started

### Local Development

1. Clone and enter the repository:
   ```bash
   git clone https://github.com/DevilDracus/WK7Bot.git
   cd WK7Bot
   ```
2. Put your token in `appsettings.Development.json` / user secrets (see config above — never commit real tokens).
3. Build & run:
   ```bash
   dotnet restore
   dotnet build
   dotnet run --project WK7Bot
   ```
4. Health check: `GET http://localhost:5220/health`

HTTPS development profile is also available (`https://localhost:7066`).

> **Note:** The default SQLite path is `/data/wk7bot.db`. On Windows local development, override `ConnectionStrings:DefaultConnection` to a writable path (e.g. `Data Source=wk7bot.db`) unless you run with privileges to create `C:\data`.

### Running Tests

```bash
dotnet test WK7Bot.sln
# or
dotnet test WK7Bot.Tests/WK7Bot.Tests.csproj
```

The suite covers Steam API mapping and resilience (bounded retry on `429`/`5xx`, no-retry on `400`, failed-schema non-caching, failure-path log assertions — against a stubbed HTTP handler), the `SteamDataCache` per-user cooldown (TTL expiry, null caching), RSS repository CRUD (EF InMemory), ICS parsing (including BOM) and ICS feed download caching (single download across lookups, failures and invalid payloads not cached), restart-safe waste dispatch (persisted per-guild dedup, independent daily/weekly kinds, partial-failure retry, missing-channel skip — via a testable subclass and EF InMemory), feature-flag registration (including `food_service_enabled`), options binding, the Gemini food service (structured-output parsing, model fallback, transient-error retries, cancellation — against a stubbed HTTP handler), the `FoodModule` slash commands (channel resolution, embed content, error paths, via Moq), and pure utilities (`FeedDeltaCalculator`, `WasteSummaryMapper`, `FeedTextFormatter`, `NameSanitizer`, `DietTagFormatter`).

### Docker

The Dockerfile lives in `WK7Bot/`, so the build context must be that directory:

```bash
docker build -f WK7Bot/Dockerfile -t wk7bot WK7Bot
docker run -d \
  --name wk7bot \
  -v "$(pwd)/WK7Bot/appsettings.json:/app/appsettings.json" \
  wk7bot
```

### Home Assistant Add-On

1. Add the custom repository to your supervisor:
   [![Add repository to Home Assistant][repository-badge]][repository-url]
2. Search for **WK7Bot** in the Add-On Store.
3. Configure your tokens in the Add-On **Configuration** tab and click **Start**.

## Health & Operations

| Endpoint | Description |
| --- | --- |
| `GET /health` | Returns `200 OK` with `WK7 Bot is healthy.` when the process is up |

Logs from `Discord.Net` are forwarded into the ASP.NET logging pipeline and appear in the add-on log.

## Continuous Integration

`.github/workflows/tests.yml` restores, builds, and runs the xUnit suite with the .NET 10 SDK on every push and pull request to `main`, `master`, and `develop`; the `trx` test results are uploaded as a build artifact.

## License & Copyright

Copyright © 2026 **DevilDracus**

Distributed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. Any derivative work or hosted service modifications must maintain source availability under this license.

### Attribution

When building upon or referencing this project, please credit the original repository:
[WK7Bot on GitHub](https://github.com/DevilDracus/WK7Bot)

[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
