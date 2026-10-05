# WK7Bot

A modular Discord automation bot and Home Assistant Add-On built on .NET 10. WK7Bot integrates Discord slash commands, MQTT event pipelines, Steam Rich Presence monitoring, RSS feed syndication, and local utility schedule automation into a unified, extensible background framework.

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
- On-demand lookups via the `/check-waste` slash command.

### Steam Rich Presence Enrichment
- Maps Discord user IDs to Steam IDs (`DiscordSteamMappings`).
- Fetches player summaries, recently played games, and enriched achievement data (icons + unlock timestamps) from the Steam Web API with a 24h schema cache.

### Home Assistant & MQTT Integration
- `HomeAssistantNotifierService` registers every writable Discord text channel and configured DM user as an MQTT `notify` entity using Home Assistant Auto-Discovery.
- Sending to `homeassistant/notify/wk7_<channelId>/set` (or `wk7_dm_<userId>/set`) pushes a message to the target Discord channel/DM.
- `DiscordPresenceMqttService` publishes live user status/activity as MQTT sensors (`wk7bot/presence/users/<userId>`), enriched with Steam data when enabled.
- `AlexaMentionNotificationService` forwards mentions of a target user to the getnotify.me Alexa notification API.
- Comes with Supervisor configuration for running as a native Add-On.

## Technology Stack

- **Framework:** .NET 10 (C# 14)
- **Discord:** `Discord.Net`, `Discord.Interactions`
- **Persistence:** Entity Framework Core with SQLite (stored at `/data/wk7bot.db` in the Add-On)
- **Messaging:** `MQTTnet`, HTTP client pipelines
- **Parsing:** `CodeHollow.FeedReader`, `Ical.Net`
- **Testing:** xUnit, Moq, EF Core InMemory
- **Deployment:** Docker, Home Assistant Supervisor Add-On

## Project Structure

```text
WK7Bot
├── Core
│   ├── Entities
│   │   ├── RssDashboardSetting.cs
│   │   └── RssFeed.cs
│   ├── Interfaces
│   │   └── IRssRepository.cs
│   └── Utilities
│       ├── FeedDeltaCalculator.cs           # Pure RSS baseline/new-item decisions
│       ├── FeedTextFormatter.cs             # HTML strip + truncation for embeds
│       ├── NameSanitizer.cs                 # Channel slugs + MQTT-safe names
│       └── WasteSummaryMapper.cs            # ICS summary → German display label
├── Extensions
│   └── ServiceCollectionExtensions.cs       # DI wiring for DB, Discord, HTTP clients, hosted services
├── Infrastructure
│   └── Data
│       ├── BotDbContext.cs
│       └── RssRepository.cs
├── Models
│   ├── SteamAchievement.cs
│   ├── SteamRecentGame.cs
│   ├── SteamUserData.cs
│   └── UserPresenceEntity.cs
├── Modules
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
│   │   ├── IHomeAssistantService.cs
│   │   ├── ILeipzigWasteService.cs
│   │   └── ISteamService.cs
│   ├── AlexaMentionNotificationService.cs
│   ├── DiscordBotWorker.cs
│   ├── DiscordPresenceMqttService.cs
│   ├── HomeAssistantNotifierService.cs
│   ├── HomeAssistantService.cs
│   ├── InteractionHandlingService.cs
│   ├── LeipzigWasteBackgroundService.cs
│   ├── LeipzigWasteService.cs
│   ├── RssParserService.cs
│   ├── RssPollingBackgroundService.cs
│   └── SteamService.cs
├── Program.cs                               # Host bootstrap, options binding, /health endpoint
├── appsettings.json
├── appsettings.Development.json
├── config.yaml                              # Home Assistant Add-On configuration
├── Dockerfile
└── WK7Bot.csproj

WK7Bot.Tests                                 # xUnit test project (included in WK7Bot.sln)
├── FeedDeltaCalculatorTests.cs
├── FeedTextFormatterTests.cs
├── FormattingUtilityTests.cs
├── LeipzigWasteServiceTests.cs
├── NameSanitizerTests.cs
├── OptionsBindingTests.cs
├── RssRepositoryTests.cs
├── ServiceCollectionExtensionsTests.cs
├── SteamServiceTests.cs
└── WasteSummaryMapperTests.cs
```

## Slash Commands

| Command | Description | Permission |
| --- | --- | --- |
| `/rss add <name> <url>` | Registers a feed, creates a role + private read-only channel (rejects duplicate names) | `ManageChannels` |
| `/rss remove <name>` | Removes a feed and cleans up its channel + role | `ManageChannels` |
| `/rss dashboard` | Posts the interactive subscription select menu | `ManageRoles` |
| `/check-waste [days] [datum]` | Queries Leipzig waste collection dates (`DD.MM.YYYY` or `YYYY-MM-DD`) | everyone |
| `/ping` | Gateway latency test | everyone |
| `/ha-status` | Tests Home Assistant Supervisor API connectivity | everyone |

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
      "steam_presence_enabled": true
    }
  }
}
```

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

The suite covers Steam API mapping, RSS repository CRUD (EF InMemory), ICS parsing (including BOM), feature-flag registration, options binding, and pure utilities (`FeedDeltaCalculator`, `WasteSummaryMapper`, `FeedTextFormatter`, `NameSanitizer`).

### Docker

```bash
docker build -t wk7bot .
docker run -d \
  --name wk7bot \
  -v "$(pwd)/appsettings.json:/app/appsettings.json" \
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

## Refactoring Backlog

A tracked list of improvement candidates with unique IDs lives in [REFACTORINGS.md](REFACTORINGS.md). RF-001 through RF-005 have been implemented (pure utility extraction + dead-code removal) and are covered by the test suite.

## License & Copyright

Copyright © 2026 **DevilDracus**

Distributed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. Any derivative work or hosted service modifications must maintain source availability under this license.

### Attribution

When building upon or referencing this project, please credit the original repository:
[WK7Bot on GitHub](https://github.com/DevilDracus/WK7Bot)

[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
