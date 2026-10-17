# WK7Bot

A modular Discord automation bot and Home Assistant Add-On built on .NET 10 — slash commands, RSS syndication, presence enrichment, wishlist deals, recipes, meetups, a weekend digest with polls, weather warnings and waste reminders in one background-service framework.

[![Run Unit Tests](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml/badge.svg)](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml)
[![Add repository to Home Assistant][repository-badge]][repository-url]

## Features

- **Discord** — `Discord.Interactions` with auto-registered modules (`InteractionHandlingService`), interactive select menus and go/pass buttons. Every interaction gets a reply: failed preconditions, command errors and unparseable commands are answered instead of timing out with "Die Anwendung reagiert nicht".
- **Message routing** — scheduled posts go to the test server while a feature is listed in `servers.testing_features`, otherwise the WK7 server (all-guild fallback when no ID is configured). **Error notifications always go to the test server only** — never the WK7 server.
- **RSS syndication** — per-feed intervals, backlog-free first poll, duplicate suppression by `LastItemGuid`/`LastPublishedDate`, `/rss add|list|remove|dashboard` with auto-created role + private channel and role-based subscription menus.
- **Leipzig waste reminders** — Stadtreinigung `.ics` parsing, daily confirmation and Monday overview, `/check-waste`, restart-safe dedup per guild.
- **Spontan-Treff** — `/spontan-treff` posts a meetup with an `@everyone` ping (graceful 403 fallback), go/pass buttons that re-render live, and a 30-minute default window auto-closed by `SpontanTreffExpiryService`.
- **Native Discord events** — Spontan-Treff and the Thursday weekend digest create a guild scheduled event (external) so members get native reminders; `DiscordScheduledEventCleanupService` deletes them after their end time (`discord_events_enabled`).
- **Weekend digest** — Thursdays 14:15 the curated leipzig.de digest posts with a native Discord poll, retrying up to 4×.
- **DWD warnings** — 2-minute CAP feed polling, point-in-polygon matching, German embeds with onset countdown into `#⛈️weather-warnings`.
- **Steam & Battle.net presence** — token-based mapping (no passwords), retries and caches, published to MQTT sensors for Home Assistant.
- **Wishlist deal alerts** — 12-hour Steam wishlist polling through IsThereAnyDeal, historical-low detection, and on-demand `/deals` lookups. Alerts post to `deal_alert_channel_name` (default `game-deals`); the minimum non-historical-low discount defaults to 60%.
- **Account linking** — `/link-steam`, `/link-battlenet`, `/link-status` and `/link-remove`; new mappings are stored in SQLite and mirrored into the Home Assistant add-on options through the Supervisor API (`link_auto_write_options`).
- **Steam wishlist links** — `/wishlist-add <url>` parses Steam profile and wishlist URLs, resolves vanity names through the Steam Web API, and links the account.
- **Weather** — `/weather [entity]` reads a configurable Home Assistant weather entity (`weather_entity`) and posts current conditions plus an optional forecast.
- **Leaderboards** — `/leaderboard` ranks linked members by Steam playtime, Steam achievements, WoW level or Diablo Paragon; a weekly post goes to `leaderboard_channel_name` on `leaderboard_post_weekday` at `leaderboard_post_time`.
- **Per-user deal watchlist** — `/watch`, `/unwatch` and `/watchlist` with background price checks and DM alerts (`deal_watchlist_*` options).
- **Prometheus metrics** — `GET /metrics` exposes command, error, feature-health, uptime and per-operation HTTP request/failure/duration metrics when `metrics_enabled` is true; the game API clients, the Supervisor client and the dependency health checks feed the HTTP counters.
- **OPML import/export** — `/rss export` downloads every registered feed as an OPML 2.0 file; `/rss import <datei>` creates feeds from an OPML attachment (max 15 entries, existing names are skipped).
- **Feed preview & pause** — `/rss test <url>` previews the newest items of any feed URL without registering it; `/rss pause`/`/rss resume` stop and restart polling while keeping the channel, role and baseline.
- **Runtime feature toggles** — `/config feature <key> <on|off>` (administrator) applies a `FeatureGate` override immediately and persists it to the add-on options via the Supervisor API; `/config list` shows every switch and whether a runtime override is active.
- **Profile cards** — `/profile [user]` renders Steam persona, 14-day playtime and achievements plus the WoW characters and Diablo heroes of the linked Battle.net account.
- **Ad-hoc polls** — `/poll <frage> <optionen>` posts a native Discord poll (2-10 answers, 1-168 h, optional multiselect); answers are separated by `|` or line breaks.
- **Food & recipes** — DuckDuckGo/Chefkoch web search with JSON-LD parsing, Gemini-generated renal-safe recipes, monthly produce calendar, weekly seasonal recipe, and `/check-food` nutrition/renal-safety checks via Open Food Facts.
- **Home Assistant notifier** — listens for MQTT notify messages and forwards them to configured Discord channels or DMs.
- **Alexa mention notifications** — forwards Discord mentions to Alexa through the configured getnotify.me endpoint.
- **Error notifications** — every `Error`/`Critical` log is buffered, throttled (5 min per signature) and posted to `#🤖WK7Errors❗` on the test server, pinging `error_notify_user_ids`, with DM fallback. Plain-text mirror includes an 8-character stack-trace hash for quick grepping.
- **Command usage & rate limiting** — every slash command is logged (`CommandUsageLog`); `/stats` shows the top commands of the last 7 days. Per-user cooldowns protect expensive commands such as `/health`, `/ha-status` and `/deals`.
- **Startup configuration validation** — `StartupConfigurationValidator` checks the Discord token, DWD postal code, Gemini/Steam/Battle.net keys, MQTT host and error recipients after login and posts a report (brief confirmation when everything is valid).
- **Database maintenance** — `DatabaseMaintenanceService` runs SQLite maintenance daily, including WAL checkpointing and orphan cleanup.
- **MQTT health sensors** — uptime, Discord connection state, guild count and a per-feature *enabled*/*problem* sensor for Home Assistant, with a last-will testament so a killed process reports OFFLINE.
- **External dependency health checks** — `/health` reports reachability and latency for Home Assistant, DWD, Steam, Gemini, Open Food Facts, and IsThereAnyDeal alongside background service status.
- **Gemini circuit breaker** — after 3 consecutive Gemini API failures, the circuit opens for 5 minutes, preventing retry storms when the API is unavailable.
- **Consolidated feature gate** — all feature flag decisions (DI registration + runtime) resolve through a single `FeatureGate` service, eliminating divergence between the two previous decision points.
- **Optional HTTP test API** — when `test_api_enabled` is true and `test_api_key` (or `WK7_TEST_API_KEY`) is set, `GET /test/commands` lists supported commands and `POST /test/commands/{command}` runs one through a synthetic interaction context. Useful for smoke-testing commands without a Discord client.

## Slash Commands

| Command | Description | Permission |
| --- | --- | --- |
| `/rss add <name> <url>` | Register a feed + role + private read-only channel | `ManageChannels` |
| `/rss list` | List all registered feeds | everyone |
| `/rss remove <name>` | Remove a feed and clean up its channel/role | `ManageChannels` |
| `/rss dashboard` | Post the interactive subscription menu | `ManageRoles` |
| `/rss pause <name>` | Pause polling for a feed (keeps channel/role/baseline) | `ManageChannels` |
| `/rss resume <name>` | Resume polling for a paused feed | `ManageChannels` |
| `/rss test <url>` | Preview the newest items of a feed URL | everyone |
| `/rss export` | Download all feeds as an OPML 2.0 file | `ManageChannels` |
| `/rss import <datei>` | Import feeds from an OPML attachment | `ManageChannels` |
| `/check-waste [days] [datum]` | Query Leipzig waste dates | everyone |
| `/recipe query:<text>` | Web-search a recipe and post it to `#🍎food` | everyone |
| `/recipe-seasonal [query]` | Search seeded with seasonal ingredients | everyone |
| `/recipe-generate` | Generate a seasonal renal-safe recipe via Gemini | everyone |
| `/seasonal-produce [month]` | Post the seasonal produce calendar | everyone |
| `/check-food <query>` | Check a food product's nutrition and renal safety via Open Food Facts | everyone |
| `/deals` | Check Steam wishlist deals on demand | everyone |
| `/link-steam <steam>` | Link a Steam account from SteamID64 or profile URL | everyone |
| `/link-battlenet <refresh_token> [region] [battle_tag]` | Link a Battle.net account | everyone |
| `/link-status` | Show your linked accounts | everyone |
| `/link-remove <provider>` | Remove a Steam or Battle.net link | everyone |
| `/wishlist-add <url>` | Link a Steam wishlist by URL | everyone |
| `/weather [entity]` | Current weather from a Home Assistant weather entity | everyone |
| `/leaderboard [type] [limit]` | Steam/Battle.net leaderboard for linked members | everyone |
| `/watch <game> [min_discount] [target_price]` | Watch a game for price drops | everyone |
| `/unwatch <game>` | Remove a game from your watchlist | everyone |
| `/watchlist` | List your watched games | everyone |
| `/spontan-treff <plan> [ort] [minuten]` | Meetup with ping + go/pass buttons | everyone |
| `/profile [user]` | Steam/Battle.net profile card of a linked member | everyone |
| `/poll <frage> <optionen> [dauer_stunden] [mehrfachauswahl]` | Native Discord poll | everyone |
| `/config feature <key> <on\|off>` | Enable/disable a feature at runtime and persist it | `Administrator` |
| `/config list` | List all feature switches and their state | `Administrator` |
| `/ping` | Gateway latency | everyone |
| `/ha-status` | Test the Home Assistant Supervisor API | everyone |
| `/stats` | Top commands of the last 7 days | everyone |
| `/health` | Background service + external dependency status | everyone |
| `/help` | List all available commands | everyone |

## Hosted Services

| Service | Purpose | Feature flag |
| --- | --- | --- |
| `DiscordBotWorker` | Gateway login/start, log forwarding | always on |
| `InteractionHandlingService` | Module registration, interaction routing and guaranteed replies | always on |
| `StartupConfigurationValidator` | Validate critical configuration and post a startup report | always on |
| `DatabaseMaintenanceService` | Daily SQLite maintenance (WAL checkpoint, cleanup) | always on |
| `RssPollingBackgroundService` | Poll feeds, post embeds | `rss_polling_enabled` |
| `HomeAssistantNotifierService` | MQTT notify → channels/DMs | `home_assistant_notifier_enabled` |
| `LeipzigWasteBackgroundService` | Daily/weekly waste reminders | `leipzig_waste_enabled` |
| `AlexaMentionNotificationService` | Mentions → Alexa API | `alexa_notifications_enabled` |
| `DiscordPresenceMqttService` | Presence snapshots → MQTT | `discord_presence_mqtt_enabled` |
| `FoodPublisherService` | Produce calendar + weekly recipe | `food_service_enabled` |
| `SpontanTreffExpiryService` | Close overdue meetups | `spontan_treff_enabled` |
| `WeekendDigestBackgroundService` | Thursday digest + poll | `weekend_digest_enabled` |
| `DwdWarningBackgroundService` | CAP polling and warnings | `dwd_warning_enabled` |
| `ErrorNotificationDispatcher` | Error embeds to the test server (DM fallback) | `error_notifications_enabled` |
| `BotStatusMqttService` | Uptime + per-feature health to MQTT | `mqtt_bot_status_enabled` |
| `WishlistDealBackgroundService` | 12-hour wishlist price polling + deal alerts | `wishlist_deal_alerts_enabled` |
| `LeaderboardBackgroundService` | Weekly leaderboard post | `leaderboard_enabled` |
| `DealWatchlistBackgroundService` | Per-user deal watchlist price checks + DM alerts | `deal_watchlist_enabled` |
| `DiscordScheduledEventCleanupService` | Delete finished Discord scheduled events | `discord_events_enabled` |

`WeekendDigestDispatcher` is registered as a singleton helper used by `WeekendDigestBackgroundService` for channel resolution and per-guild dispatch deduplication.

Flags resolve from `Wk7Bot:features`, then root-level `features`, then default enabled.

## MQTT Health Sensors

`BotStatusMqttService` publishes a device *WK7 Bot* with a last-will on `wk7bot/status`:

| Entity | States |
| --- | --- |
| `sensor.wk7_bot_status` | uptime in seconds; attributes `version`, `started_at_utc`, `uptime_seconds`, `discord_connection`, `guilds`, `features_total`, `features_problem` |
| `binary_sensor.wk7_bot_<feature>_enabled` | ON while the feature flag is set |
| `binary_sensor.wk7_bot_<feature>_problem` | ON while the feature's last pass failed; attributes `last_success_utc`, `last_failure_utc`, `last_error` |

`<feature>` ∈ `food`, `rss`, `leipzig_waste`, `weekend_digest`, `dwd_warning`, `spontan_treff`. All entities expire to `unavailable` within 2–5 minutes if the heartbeat stops, so a dead bot is visible immediately.

Example dashboard cards (Mushroom + grid layout):

```yaml
- type: custom:mushroom-chips-card
  alignment: end
  chips:
    - type: entity
      entity: sensor.wk7_bot_status
      name: WK7 Bot
      icon: mdi:robot
      icon_color: |-
        {% if is_state('sensor.wk7_bot_status', 'unavailable') %}red
        {% elif state_attr('sensor.wk7_bot_status', 'discord_connection') != 'Connected' %}orange
        {% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7_bot_rss_problem
      name: RSS
      icon: mdi:rss
      icon_color: |-
        {% if is_state('binary_sensor.wk7_bot_rss_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7_bot_leipzig_waste_problem
      name: Müll
      icon: mdi:trash-can
      icon_color: |-
        {% if is_state('binary_sensor.wk7_bot_leipzig_waste_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7_bot_weekend_digest_problem
      name: Digest
      icon: mdi:calendar-weekend
      icon_color: |-
        {% if is_state('binary_sensor.wk7_bot_weekend_digest_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7_bot_dwd_warning_problem
      name: DWD
      icon: mdi:weather-lightning
      icon_color: |-
        {% if is_state('binary_sensor.wk7_bot_dwd_warning_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7_bot_food_problem
      name: Food
      icon: mdi:food
      icon_color: |-
        {% if is_state('binary_sensor.wk7_bot_food_problem', 'on') %}red{% else %}green{% endif %}
```

## Configuration

Bound from `appsettings.json`, environment variables or the Add-On `config.yaml`; the `Wk7Bot` section supplies defaults and root-level keys override per key. The add-on schema lives in `WK7Bot/config.yaml`; application-level options not listed there (`deal_alert_channel_name`, `deal_check_interval_hours`, `deal_min_discount_percent`, `startup_report_verbose`, `http_timeouts.fast/default/slow`) can be supplied through `appsettings.json`, environment variables, or user secrets.

```yaml
discord_token: "YOUR_DISCORD_BOT_TOKEN"
log_level: "Warning"                     # Debug, Information, Warning, Error
mqtt_host: "core-mosquitto"
mqtt_port: 1883
steam_api_key: ""
battlenet_client_id: ""
battlenet_client_secret: ""
battlenet_region: "eu"
battlenet_locale: "de_DE"
gemini_api_key: ""
test_api_enabled: false                  # optional /test API
test_api_key: ""                         # required when test_api_enabled is true
weather_entity: "weather.home"
metrics_enabled: true
link_auto_write_options: true
deal_alert_channel_name: "game-deals"
deal_check_interval_hours: 12
deal_min_discount_percent: 60
deal_watchlist_check_interval_hours: 6
deal_watchlist_min_discount_percent: 40
leaderboard_channel_name: "leaderboard"
leaderboard_post_weekday: "Monday"
leaderboard_post_time: "18:00"
http_timeouts:
  fast: 10
  default: 30
  slow: 45
error_notify_user_ids:
  - "162201162257399808"
discord_dm_user_ids: []
discord_steam_mappings: []               # [{ discord_user_id: "...", steam_id: "..." }]
discord_battlenet_mappings: []           # [{ discord_user_id: "...", refresh_token: "..." }]
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
  mqtt_bot_status_enabled: true
  wishlist_deal_alerts_enabled: true
  leaderboard_enabled: true
  deal_watchlist_enabled: true
servers:
  wk7_server_id: "YOUR_WK7_SERVER_ID"    # scheduled messages
  test_server_id: "YOUR_BOT_TEST_SERVER_ID"  # testing_features + all error reports
  testing_features: []                   # e.g. [ "dwd_warning" ]
dwd_warning:
  postal_code: "01234"
  latitude: 00.00
  longitude: 00.00
  lead_minutes: 30
```

`servers.*_server_id` ship as placeholders; an unparsable or empty ID counts as "not configured", so a fresh install never breaks. Environment variables (`DISCORD_BOT_TOKEN`, `SUPERVISOR_TOKEN`, `WK7_TEST_API_KEY`) and .NET user secrets also work.

### Optional HTTP test API

When `test_api_enabled` is true and a non-empty key is configured (`test_api_key` or `WK7_TEST_API_KEY`), the bot exposes:

- `GET /test/commands` — list of supported test commands.
- `POST /test/commands/{command}` — run a command with `{ "args": { ... } }`; the legacy `POST /test/run/{command}` route is also kept for compatibility.

Every request must carry `X-WK7-Test: <key>`. The API is off by default and stays inactive while the flag is false or the key is empty.

## Getting Started

```bash
git clone https://github.com/DevilDracus/WK7Bot.git
cd WK7Bot
dotnet run --project WK7Bot
```

Health check: `GET /health`. Docker: `docker build -f WK7Bot/Dockerfile -t wk7bot WK7Bot`. As an Add-On: add the repository above, install **WK7Bot**, set `discord_token` and MQTT, start.

On Windows override `ConnectionStrings:DefaultConnection` to a writable path (e.g. `Data Source=wk7bot.db`) unless `C:\data` can be created.

## Testing

```bash
dotnet test WK7Bot.sln
```

**644 tests** cover API mapping and retry behaviour against stubbed HTTP handlers, options binding, error-queue throttling, interaction routing and guaranteed replies, SQLite DDL replay for restart-safe dispatch, circuit breaker logic, feature gate resolution, DM-context module safety, command cooldowns and usage metrics, startup validation, and all embed/poll/button builders. Last verified: 644 passed, 0 failed.

## Stack

.NET 10 (C# 14) · `Discord.Net` / `Discord.Interactions` · EF Core + SQLite · `MQTTnet` · Polly 8 · `CodeHollow.FeedReader`, `Ical.Net`, JSON-LD recipes · DuckDuckGo/Chefkoch search · Open Food Facts · Steam Web API, Battle.net OAuth and IsThereAnyDeal · DWD CAP open data · Google Gemini · xUnit, Moq, EF InMemory · Docker, Home Assistant Supervisor Add-On.

## License

Copyright © 2026 **DevilDracus** — [GNU AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html). Please credit [WK7Bot](https://github.com/DevilDracus/WK7Bot) when building upon this project.

[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
