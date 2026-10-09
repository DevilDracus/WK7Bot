# WK7Bot

A modular Discord automation bot and Home Assistant Add-On built on .NET 10. It combines slash commands, MQTT event pipelines, Steam and Battle.net presence enrichment, RSS syndication, web-searched and AI-generated recipes, spontaneous meetups, a weekly Leipzig culture digest with voting polls, local DWD weather warnings and waste-collection reminders in one extensible background framework.

[![Run Unit Tests](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml/badge.svg)](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml)
[![Add repository to Home Assistant][repository-badge]][repository-url]

## Overview

WK7Bot runs as a standalone container or as a native Home Assistant Supervisor Add-On. It uses strongly-typed `IOptions<T>` configuration, hosted background services and asynchronous event pipelines for real-time notifications, Home Assistant automation triggers and community interactions.

## Features

### Discord
- `Discord.Interactions` on `Discord.Net` with reflection-based module auto-registration (`InteractionHandlingService`); guild-scoped while `Discord:TestGuildId` (or root `TestGuildId`) is set, global otherwise.
- Interactive UI: select menus for RSS subscriptions, one-tap go/pass buttons for meetups.

### Automatic message routing
- Every automatic post (`WeekendDigestBackgroundService`, `DwdWarningBackgroundService`, `LeipzigWasteBackgroundService`, `FoodPublisherService`) goes to the **WK7 server** (`servers.wk7_server_id`).
- Features listed in `servers.testing_features` (e.g. `["dwd_warning"]`) post to the **bot test server** (`servers.test_server_id`) instead, so test traffic never reaches the community server.
- While no server ID is configured, posts go to every guild (previous behaviour) and a warning is logged. Command-driven posts (`/rss`, `/recipe*`, `/spontan-treff`, HA notify) are unaffected.

### RSS feed syndication
- Polls configured feeds every 5 minutes; the first poll records the newest item silently instead of flooding the channel with backlog.
- Duplicate suppression via `LastItemGuid` / `LastPublishedDate` (`FeedDeltaCalculator`); descriptions are HTML-stripped, entity-decoded and truncated (`FeedTextFormatter`).
- `/rss add|remove|dashboard` manage feeds with auto-created role + private read-only channel; users subscribe via the interactive select menu (`RssComponentModule`).

### Leipzig waste collection reminders
- Parses Stadtreinigung Leipzig `.ics` streams (BOM and leading whitespace tolerated) and maps categories to German display labels (`WasteSummaryMapper`); validated calendars are cached for 1 hour so one download serves all lookups.
- Daily confirmation + Monday weekly overview in `#🗑️leipzig-waste`; on-demand lookups via `/check-waste`.
- **Restart-safe dedup** – per-guild dispatch state lives in `WasteDispatchLogs`, so restarts never re-send today's messages; guilds whose send failed retry on the next tick.

### Spontan-Treff (spontaneous meetups)
- `/spontan-treff plan [ort] [minuten]` posts a meetup with an `@everyone` ping into `#🎉spontan-treff` (created automatically when missing) with **✅ Auf dem Weg** / **🚫 Absagen** buttons.
- Tap the same button again to undo, the other one to switch sides; every change re-renders the shared embed (`SpontanTreffMessageBuilder`).
- 30-minute default open window (1–1440 via `minuten`); `@everyone`-ping failures (`403`) fall back to a plain message; `SpontanTreffExpiryService` closes overdue meetups every minute and strips their buttons.

### Leipzig weekend & culture digest
- Every **Thursday at 14:15** posts the curated digest for the upcoming weekend into `#📅wochenende`: market-first picks ranked by `WeekendDigestCurator` from the leipzig.de weekend and month-overview pages (parsed by `WeekendEventParser`, merged, URL-deduped, Fri–Sun filtered).
- Renders one embed field per pick plus a native Discord poll ("Wohin gehen wir am Wochenende?") whose duration covers the time until Sunday 23:59; single-pick weeks post without a poll.
- **Restart-safe dedup** (dispatch kind `weekend_digest`), retries every 15 minutes (max 4 attempts), zero-event weekends are marked handled without posting, and a build-once cache keeps retries from re-fetching leipzig.de.

### Local DWD rain & storm warnings
- Checks the DWD open-data CAP feed every 2 minutes and posts a German embed — onset countdown, validity window, area, severity colour and official instructions — into `#⛈️weather-warnings` when a warning for the configured location is at most `lead_minutes` ahead.
- Point-in-polygon location matching (with `area_names` as fallback for polygon-free warnings) and case-insensitive event matching for German *and* English codes after umlaut/separator normalisation.
- **Restart-safe dedup** via `WarningDispatchLogs`, pruned after 7 days; stays idle with a log hint while `dwd_warning.postal_code` is empty.

### Steam & Battle.net presence enrichment
- Maps Discord users to Steam IDs (`discord_steam_mappings`) or Battle.net refresh tokens (`discord_battlenet_mappings`) — no passwords are stored, and Blizzard tokens are revocable from the user's account page at any time.
- **Steam**: player summaries with avatar, recently played games and enriched achievements (2-concurrent fetching, retry on `429`/`5xx`, `403` privacy fallback via `GetTopAchievementsForGames`, 60s per-user cooldown, only successful schemas cached 24h).
- **Battle.net**: WoW characters (level, class, faction, item level, guild, avatar art) and Diablo III heroes; per-mapping `region` override and localized class/faction/realm names. Blizzard publishes no player API for Diablo IV, Hearthstone or Overwatch 2 — those sections stay empty by design.
- Presence snapshots are published to MQTT sensors (`wk7bot/presence/users/<userId>`) for Home Assistant (dashboard view in `HASS.yaml`), and writable Discord channels/DMs are registered as MQTT `notify` entities via auto-discovery. `AlexaMentionNotificationService` optionally forwards mentions to the getnotify.me API.

### Food & recipe automation
- `/recipe query:<text>` queries DuckDuckGo's keyless HTML endpoint, downloads candidates and parses schema.org JSON-LD `Recipe` metadata (times, yield, ingredients, steps, hero image). DuckDuckGo bot-challenge pages (HTTP 202) fall back to the Chefkoch search; only when *both* providers fail does the command report the search as temporarily unavailable and point at `/recipe-generate`.
- `/recipe-generate` produces seasonal dialysis/transplant-safe recipes via Gemini structured output (primary + fallback model, exponential backoff on `429`/`503`, missing key short-circuits); `/recipe-seasonal` seeds the web search with two weighted seasonal terms (`SeasonalTermPicker`), and a format-only Gemini pass restructures searched recipes without inventing diet tags.
- `FoodPublisherService` posts the monthly produce calendar (1st, 09:00) and a seasonal web recipe (Thursdays 15:30) to `#🍎food`; all embeds share `RecipeEmbedBuilder` with its budget-aware field layout.

### Error notifications
- `DiscordErrorLoggerProvider` captures every `Error`/`Critical` log event (the regular add-on logs stay untouched) and `Program.cs` routes process-level escapes into the same bounded `ErrorNotificationQueue`.
- `ErrorNotificationDispatcher` posts a red embed (`⚠️ Fehler in <Kontext>`, exception, message, stack trace, context, timestamp) into the dedicated `#🤖WK7Errors❗` channel — created automatically when missing, read-only for members, following the same find-or-create pattern as the other feature channels — and pings every user in `error_notify_user_ids`. Identical reports are throttled to one post per unique signature every 5 minutes and delivered in rate-limit-friendly batches. Toggle with `features.error_notifications_enabled`.
- While `error_notifications` is listed in `servers.testing_features`, the channel is created on the bot test server instead of the WK7 server. When no channel can be resolved or created (missing Manage-Channels permission, no shared guild, failing post), the dispatcher falls back to delivering the embed as a direct message, so reports are never lost.
- Startup schema statements run on a raw ADO connection, so their expected outcomes (`duplicate column name`, `database is locked` while a previous process shuts down) are retried or ignored instead of reaching the notification channel.

## Slash Commands

| Command | Description | Permission |
| --- | --- | --- |
| `/rss add <name> <url>` | Registers a feed, creates a role + private read-only channel (rejects duplicate names) | `ManageChannels` |
| `/rss remove <name>` | Removes a feed and cleans up its channel + role | `ManageChannels` |
| `/rss dashboard` | Posts the interactive subscription select menu | `ManageRoles` |
| `/check-waste [days] [datum]` | Queries Leipzig waste collection dates (`DD.MM.YYYY` or `YYYY-MM-DD`) | everyone |
| `/recipe query:<text>` | Searches the web for a recipe, parses its structured data and posts it with a source link to `#🍎food` | everyone |
| `/recipe-seasonal [query]` | Searches for a recipe seeded with two randomly weighted seasonal ingredients | everyone |
| `/recipe-generate` | Generates a seasonal renal & transplant-safe recipe and posts it to `#🍎food` | everyone |
| `/seasonal-produce [month]` | Posts the seasonal fruit/vegetable/herb/nut calendar (1-12, default: current) to `#🍎food` | everyone |
| `/spontan-treff <plan> [ort] [minuten]` | Announces a meetup with an `@everyone` ping and go/pass buttons in `#🎉spontan-treff` (open 1-1440 min, default 30) | everyone |
| `/ping` | Gateway latency test | everyone |
| `/ha-status` | Tests Home Assistant Supervisor API connectivity | everyone |

Food commands run ephemerally while the lookup executes, post the result embed into `#🍎food`, and report a clear error when the channel or the Gemini API key is missing. `/recipe` and `/recipe-seasonal` need no API key of their own (a Gemini key only improves formatting).

## Hosted Services

| Hosted Service | Purpose | Feature flag |
| --- | --- | --- |
| `DiscordBotWorker` | Logs in and connects the Discord gateway, forwards `Discord.Net` logs | always on |
| `InteractionHandlingService` | Auto-registers command modules and routes slash command interactions | always on |
| `RssPollingBackgroundService` | Polls RSS feeds and posts update embeds to Discord | `rss_polling_enabled` |
| `HomeAssistantNotifierService` | Bridges MQTT notify commands to Discord channels / DMs | `home_assistant_notifier_enabled` |
| `LeipzigWasteBackgroundService` | Sends daily and weekly waste collection reminders | `leipzig_waste_enabled` |
| `AlexaMentionNotificationService` | Forwards target-user mentions to the Alexa notification API | `alexa_notifications_enabled` |
| `DiscordPresenceMqttService` | Publishes Discord presence snapshots to MQTT for Home Assistant (Steam/Battle.net enriched) | `discord_presence_mqtt_enabled` |
| `FoodPublisherService` | Posts the monthly produce calendar and the weekly seasonal web recipe to `#🍎food` | `food_service_enabled` |
| `SpontanTreffExpiryService` | Closes overdue spontaneous meetups and removes their buttons | `spontan_treff_enabled` |
| `WeekendDigestBackgroundService` | Posts the Thursday 14:15 weekend digest with a native voting poll | `weekend_digest_enabled` |
| `DwdWarningBackgroundService` | Polls the DWD CAP feed every 2 minutes and posts local rain/storm warnings | `dwd_warning_enabled` |
| `ErrorNotificationDispatcher` | Posts buffered error reports as embeds to `#🤖WK7Errors❗` (DM fallback) | `error_notifications_enabled` |

Feature flags resolve from the `Wk7Bot:features` section (appsettings.json), otherwise root-level `features` (`options.json`), otherwise default enabled — services re-check the bound options at runtime, so disabling a flag always takes effect.

## Key internals

- **Idempotent dispatch** – every scheduled post check-then-inserts into its dispatch table. Lost insert races are swallowed by `DatabaseWriteGuard`, which classifies only primary-key/unique violations (never NOT NULL/CHECK/FK failures) via SQLite's extended error codes plus a message fallback.
- **Embed limits** – `EmbedText` is the single source for Discord's 256/1024/4096/char boundaries and the shared truncation all message builders use.
- **Feature channels** – `ChannelResolver` implements the shared find-or-create pattern (case-insensitive name match; topic and permission overwrites apply only when the channel is actually created, with `HttpException` handled as a warning so a missing permission never escalates into the error-DM queue).
- **Configuration binding** – the `Wk7Bot` section fills defaults, the configuration root (Home Assistant `options.json`) overrides per key, and server targeting goes through `AutomaticTargetResolver`.

## Technology Stack

- **Framework:** .NET 10 (C# 14)
- **Discord:** `Discord.Net`, `Discord.Interactions`
- **Persistence:** Entity Framework Core with SQLite (`/data/wk7bot.db` in the Add-On)
- **Messaging:** `MQTTnet`, HTTP client pipelines
- **Parsing:** `CodeHollow.FeedReader`, `Ical.Net`, schema.org JSON-LD recipes (`System.Text.Json`)
- **Web search:** DuckDuckGo HTML endpoint (keyless) with the Chefkoch search as fallback provider
- **Game platforms:** Steam Web API; Battle.net OAuth 2.0 + Blizzard profile APIs
- **Weather warnings:** Deutscher Wetterdienst open-data CAP alerts (zipped XML, point-in-polygon matching)
- **AI:** Google Gemini `generateContent` (structured JSON schema, primary + fallback model)
- **Testing:** xUnit, Moq, EF Core InMemory
- **Deployment:** Docker, Home Assistant Supervisor Add-On

## Project Structure

```text
WK7Bot
├── Core
│   ├── Entities
│   │   ├── FoodDispatchLog.cs               # Persisted per-guild food dispatch records
│   │   ├── RssDashboardSetting.cs
│   │   ├── RssFeed.cs
│   │   ├── SpontanTreff.cs                  # Short-lived meetup with go/pass button answers
│   │   ├── SpontanTreffResponse.cs          # Per-user answer (composite key)
│   │   ├── WarningDispatchLog.cs            # Per-guild DWD warning dispatch records
│   │   └── WasteDispatchLog.cs              # Persisted per-guild waste/weekend digest records
│   ├── Exceptions
│   │   └── RecipeSearchUnavailableException.cs
│   ├── Interfaces                          # Repository contracts for all persistence
│   ├── Utilities
│   │   ├── AutomaticTargetResolver.cs      # Feature → WK7 server / bot test server routing
│   │   ├── CapWarningParser.cs             # CAP XML → warning model, polygon + event matching
│   │   ├── ChannelResolver.cs              # Shared feature-channel find-or-create + permissions
│   │   ├── DietTagFormatter.cs             # Diet-tag identifiers → German embed labels
│   │   ├── DwdWarningMessageBuilder.cs
│   │   ├── EmbedText.cs                    # Discord embed limits + shared truncation
│   │   ├── ErrorEmbedBuilder.cs
│   │   ├── FeedDeltaCalculator.cs          # Pure RSS baseline/new-item decisions
│   │   ├── FeedTextFormatter.cs            # HTML strip + truncation for embeds
│   │   ├── NameSanitizer.cs
│   │   ├── RecipeEmbedBuilder.cs           # Shared recipe embeds (search vs. generated)
│   │   ├── SeasonalTermPicker.cs           # Weighted random seasonal-term selection
│   │   ├── SpontanTreffMessageBuilder.cs
│   │   ├── WasteSummaryMapper.cs           # ICS summary → German display label
│   │   ├── WeekendDigestCurator.cs         # Market-first Fri–Sun pick ranking
│   │   ├── WeekendDigestMessageBuilder.cs
│   │   └── WeekendEventParser.cs           # leipzig.de event-card HTML → WeekendEvent list
│   ├── Extensions
│   │   └── ServiceCollectionExtensions.cs  # DI wiring for DB, Discord, HTTP clients, hosted services
│   ├── Infrastructure
│   │   └── Data
│   │       ├── BotDbContext.cs
│   │       ├── DatabaseWriteGuard.cs      # Classifies duplicate-key write failures for idempotent dispatch
│   │       ├── FoodDispatchRepository.cs
│   │       ├── RssRepository.cs
│   │       ├── SpontanTreffRepository.cs
│   │       ├── WarningDispatchRepository.cs
│   │       └── WasteDispatchRepository.cs
│   ├── Models                              # Steam/Battle.net payloads, CapWarning, FoodModels, WeekendEvent
│   ├── Modules                             # Interaction modules (Food, LeipzigWaste, Rss*, SpontanTreff*, System)
│   ├── Options                             # Strongly-typed options incl. feature flags + server routing
│   ├── Services
│   │   ├── Interfaces                      # Fetch contracts for external APIs
│   │   ├── AlexaMentionNotificationService.cs
│   │   ├── BattleNetDataCache.cs            # 60s per-refresh-token fetch cooldown
│   │   ├── BattleNetService.cs              # OAuth refresh flow + userinfo/WoW/Diablo profile reads
│   │   ├── DiscordBotWorker.cs
│   │   ├── DiscordErrorLoggerProvider.cs
│   │   ├── DiscordPresenceMqttService.cs
│   │   ├── DwdWarningBackgroundService.cs   # 2-minute DWD poll → #⛈️weather-warnings
│   │   ├── DwdWarningService.cs
│   │   ├── ErrorNotificationDispatcher.cs
│   │   ├── ErrorNotificationQueue.cs        # Bounded buffer with signature throttle
│   │   ├── FoodPublisherService.cs
│   │   ├── GeminiFoodService.cs
│   │   ├── HomeAssistantNotifierService.cs
│   │   ├── HomeAssistantService.cs
│   │   ├── InteractionHandlingService.cs
│   │   ├── LeipzigWasteBackgroundService.cs
│   │   ├── LeipzigWasteService.cs
│   │   ├── LeipzigWeekendEventSource.cs
│   │   ├── MqttConnectionCoordinator.cs    # Shared MQTT connect/reconnect handling
│   │   ├── RssParserService.cs
│   │   ├── RssPollingBackgroundService.cs
│   │   ├── SpontanTreffExpiryService.cs
│   │   ├── SteamDataCache.cs                # 60s per-user Steam fetch cooldown
│   │   ├── SteamService.cs
│   │   ├── SystemRandomSource.cs           # Random.Shared-backed IRandomSource
│   │   ├── WebRecipeSearchService.cs       # DuckDuckGo + Chefkoch + JSON-LD recipe parsing
│   │   └── WeekendDigestBackgroundService.cs
│   └── Program.cs                           # Host bootstrap, options binding, schema init, global error hooks
└── WK7Bot.Tests/                            # xUnit + Moq suite mirroring the source structure
```

## Configuration

Configuration is bound from `appsettings.json`, environment variables, or the Home Assistant Add-On `config.yaml`: the `Wk7Bot` section (appsettings) and root-level keys (`config.yaml` / `options.json`) both map to `Wk7BotOptions` via snake_case `ConfigurationKeyName` aliases, with the root taking precedence per key. `appsettings.json` only carries logging, the connection string and the Leipzig ICS URL. The full option schema with defaults lives in `WK7Bot/config.yaml`.

Platform credentials are optional: without `steam_api_key` the Steam enrichment is skipped, and without Battle.net client credentials (or with an empty `discord_battlenet_mappings`) the Battle.net enrichment logs a warning and skips — Discord, MQTT and every other feature keep working either way.

### Home Assistant `config.yaml`

```yaml
discord_token: "YOUR_DISCORD_BOT_TOKEN"
mqtt_host: "core-mosquitto"
mqtt_port: 1883
mqtt_username: ""
mqtt_password: ""
steam_api_key: ""
battlenet_client_id: ""                         # OAuth client ID from develop.battle.net
battlenet_client_secret: ""                     # OAuth client secret
battlenet_region: "eu"                          # us, eu, kr or tw (per-mapping region wins)
battlenet_locale: "de_DE"                       # Localizes WoW class/faction/realm names
gemini_api_key: ""                             # Google AI Studio key: seasonal produce, generated recipes, recipe formatting
discord_steam_mappings:
  - discord_user_id: "123456789012345678"
    steam_id: "76561198000000000"
discord_battlenet_mappings:
  - discord_user_id: "123456789012345678"
    refresh_token: "YOUR_BATTLE_NET_REFRESH_TOKEN"
    region: ""                                  # Optional per-account override (us, eu, kr, tw)
    battle_tag: ""                              # Optional fallback when userinfo is unreachable
discord_dm_user_ids:
  - "123456789012345678"
error_notify_user_ids:
  - "162201162257399808"                        # User IDs receiving captured errors as DM embeds
leipzig_waste_ics_feed_url: ""                  # Optional root override of LeipzigWaste:IcsFeedUrl
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
  battlenet_presence_enabled: true
  food_service_enabled: true
  spontan_treff_enabled: true
  weekend_digest_enabled: true
  dwd_warning_enabled: true
  error_notifications_enabled: true
servers:
  wk7_server_id: "YOUR_WK7_SERVER_ID"             # WK7 server: receives all automatic messages
  test_server_id: "YOUR_BOT_TEST_SERVER_ID"       # Bot test server: features listed below post here
  testing_features: []                            # e.g. [ "dwd_warning", "error_notifications" ]
dwd_warning:
  postal_code: "01234"                          # 5-digit German postal code to watch
  latitude: 00.00                               # Used for point-in-polygon matching
  longitude: 00.00
  lead_minutes: 00                              # Post a warning when onset is at most this far ahead
  area_names:                                   # Fallback labels when a warning carries no polygon
    - "City Name"
    - "City"
  events:                                       # Matched case-insensitively (umlauts/separators normalised)
    - "STARKREGEN"
    - "HAGEL"
    - "GEWITTER"
    - "STURM"
    - "BÖEN"
    - "BOEEN"
    - "HEAVY_RAIN"
    - "HAIL"
    - "THUNDER"
    - "STORM"
```

`servers.wk7_server_id` and `servers.test_server_id` ship as placeholders (`YOUR_WK7_SERVER_ID` / `YOUR_BOT_TEST_SERVER_ID`) — enter the real IDs in the add-on **Configuration** tab (Home Assistant stores them in `/data/options.json`); an unparsable or empty ID counts as "not configured", so the placeholders never break a fresh install. Secrets may also come from environment variables (`DISCORD_BOT_TOKEN`, `SUPERVISOR_TOKEN`, …) or .NET user secrets.

#### Acquiring a Battle.net refresh token

1. At [develop.battle.net](https://develop.battle.net) create an application with client type **Confidential** and a redirect URI (e.g. `http://localhost:8080`); copy the **Client ID** and **Client Secret** into `battlenet_client_id` / `battlenet_client_secret`.
2. Open the authorize URL in a browser, approve the scopes `openid offline_access wow.profile d3.profile` — **without `offline_access` the token endpoint returns no refresh token** — and copy the `code` query parameter from the redirect URI (the page itself does not need to load):

   ```text
   https://us.battle.net/oauth/authorize?client_id=<CLIENT_ID>&redirect_uri=http://localhost:8080&response_type=code&scope=openid%20offline_access%20wow.profile%20d3.profile&state=wk7
   ```

3. Exchange the code (host region does not matter):

   ```bash
   curl -X POST https://us.battle.net/oauth/token \
     -u "<CLIENT_ID>:<CLIENT_SECRET>" \
     -d grant_type=authorization_code \
     -d code="<CODE>" \
     -d redirect_uri="http://localhost:8080"
   ```

4. Put the returned `refresh_token` into `discord_battlenet_mappings[].refresh_token`. It can be revoked any time under *Account → Settings → Security*; the bot then logs a warning and skips Battle.net data.

| Environment variable | Used by | Purpose |
| --- | --- | --- |
| `DISCORD_BOT_TOKEN` | `DiscordBotWorker` | Fallback token when `discord_token` is empty |
| `SUPERVISOR_TOKEN` | `HomeAssistantService` | Bearer token for Supervisor-proxied HA API |
| `ASPNETCORE_ENVIRONMENT` | Host | Set to `Development` for local runs |

## Getting Started

### Local development

```bash
git clone https://github.com/DevilDracus/WK7Bot.git
cd WK7Bot
dotnet run --project WK7Bot
```

Put your token in `appsettings.Development.json` or user secrets (never commit real tokens). Health check: `GET http://localhost:5220/health` (HTTPS profile on `https://localhost:7066`). The default SQLite path is `/data/wk7bot.db` — on Windows override `ConnectionStrings:DefaultConnection` to a writable path (e.g. `Data Source=wk7bot.db`) unless you can create `C:\data`.

### Docker

The build context must be the `WK7Bot` directory:

```bash
docker build -f WK7Bot/Dockerfile -t wk7bot WK7Bot
docker run -d --name wk7bot \
  -v "$(pwd)/WK7Bot/appsettings.json:/app/appsettings.json" \
  wk7bot
```

### Home Assistant Add-On

1. Add the custom repository to your supervisor: [![Add repository to Home Assistant][repository-badge]][repository-url]
2. Search for **WK7Bot** in the Add-On Store.
3. Configure your tokens in the **Configuration** tab and click **Start** — `discord_token` and MQTT are required; Steam/Battle.net credentials are optional.

Installs pull the pre-built image `ghcr.io/devildracus/wk7bot:<version>` (`aarch64`/`amd64` multi-arch) instead of compiling on the device. To release a new version, bump `version:` in `WK7Bot/config.yaml` and push — the build workflow skips publishing while that version tag already exists.

## Testing

```bash
dotnet test WK7Bot.sln
```

The suite covers every layer above: external API mapping and resilience against stubbed HTTP handlers (Steam, Battle.net OAuth/userinfo/WoW/Diablo parsing with retries and failure logging, Gemini envelopes, DuckDuckGo/Chefkoch search and JSON-LD recipe parsing, ICS parsing and download caching), the CQRS-style plumbing (options binding, feature-flag registration, error-queue throttling/capacity, interaction routing), the persistence layer with SQLite DDL replay of the startup sequence for restart-safe dispatch, and all embed/poll builders, meetup button handling, expiry sweep and interactive select menus via Moq.

## Health & Operations

| Endpoint | Description |
| --- | --- |
| `GET /health` | Returns `200 OK` with `WK7 Bot is healthy.` when the process is up |

`Discord.Net` logs are forwarded into the ASP.NET logging pipeline and appear in the add-on log; Battle.net problems surface there as a single `Warning` (missing credentials) or with endpoint status and BattleTag (OAuth/API failures) without failing the whole presence payload. Every `Error`/`Critical` event is additionally delivered as a red embed (to `#🤖WK7Errors❗`, or by DM when that channel is unavailable — see [Error notifications](#error-notifications)), so critical failures reach you even when nobody is watching the add-on log.

## Continuous Integration

`.github/workflows/tests.yml` builds and runs the xUnit suite with the .NET 10 SDK on every push and pull request to `main`, `master`, and `develop` (badge at the top). `.github/workflows/builder.yaml` publishes the add-on container for `amd64`/`aarch64` to `ghcr.io/devildracus/wk7bot` on changes under `WK7Bot/**`, tagged with the `version` from `WK7Bot/config.yaml` (plus `latest`) and skipped while that tag exists. The GHCR package must be set to **public** once so the Supervisor can pull it anonymously.

## License & Copyright

Copyright © 2026 **DevilDracus**

Distributed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. Any derivative work or hosted service modifications must maintain source availability under this license.

### Attribution

When building upon or referencing this project, please credit the original repository:
[WK7Bot on GitHub](https://github.com/DevilDracus/WK7Bot)

[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
