# WK7Bot

A modular Discord automation bot and Home Assistant Add-On built on .NET 10 — slash commands, RSS syndication, presence enrichment, recipes, meetups, a weekend digest with polls, weather warnings and waste reminders in one background-service framework.

[![Run Unit Tests](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml/badge.svg)](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml)
[![Add repository to Home Assistant][repository-badge]][repository-url]

## Features

- **Discord** — `Discord.Interactions` with auto-registered modules (`InteractionHandlingService`), interactive select menus and go/pass buttons. Every interaction gets a reply: failed preconditions, command errors and unparseable commands are answered instead of timing out with "Die Anwendung reagiert nicht".
- **Message routing** — scheduled posts go to the test server while a feature is listed in `servers.testing_features`, otherwise the WK7 server (all-guild fallback when no ID is configured). **Error notifications always go to the test server only** — never the WK7 server.
- **RSS syndication** — per-feed intervals, backlog-free first poll, duplicate suppression by `LastItemGuid`/`LastPublishedDate`, `/rss add|remove|dashboard` with auto-created role + private channel and role-based subscription menus.
- **Leipzig waste reminders** — Stadtreinigung `.ics` parsing, daily confirmation and Monday overview, `/check-waste`, restart-safe dedup per guild.
- **Spontan-Treff** — `/spontan-treff` posts a meetup with an `@everyone` ping (graceful 403 fallback), go/pass buttons that re-render live, and a 30-minute default window auto-closed by `SpontanTreffExpiryService`.
- **Weekend digest** — Thursdays 14:15 the curated leipzig.de digest posts with a native Discord poll, retrying up to 4×.
- **DWD warnings** — 2-minute CAP feed polling, point-in-polygon matching, German embeds with onset countdown into `#⛈️weather-warnings`.
- **Steam & Battle.net presence** — token-based mapping (no passwords), retries and caches, published to MQTT sensors for Home Assistant.
- **Food & recipes** — DuckDuckGo/Chefkoch web search with JSON-LD parsing, Gemini-generated renal-safe recipes, monthly produce calendar and weekly seasonal recipe.
- **Error notifications** — every `Error`/`Critical` log is buffered, throttled (5 min per signature) and posted to `#🤖WK7Errors❗` on the test server, pinging `error_notify_user_ids`, with DM fallback.
- **MQTT health sensors** — uptime, Discord connection state, guild count and a per-feature *enabled*/*problem* sensor for Home Assistant, with a last-will testament so a killed process reports OFFLINE.

## Slash Commands

| Command | Description | Permission |
| --- | --- | --- |
| `/rss add <name> <url>` | Register a feed + role + private read-only channel | `ManageChannels` |
| `/rss remove <name>` | Remove a feed and clean up its channel/role | `ManageChannels` |
| `/rss dashboard` | Post the interactive subscription menu | `ManageRoles` |
| `/check-waste [days] [datum]` | Query Leipzig waste dates | everyone |
| `/recipe query:<text>` | Web-search a recipe and post it to `#🍎food` | everyone |
| `/recipe-seasonal [query]` | Search seeded with seasonal ingredients | everyone |
| `/recipe-generate` | Generate a seasonal renal-safe recipe via Gemini | everyone |
| `/seasonal-produce [month]` | Post the seasonal produce calendar | everyone |
| `/spontan-treff <plan> [ort] [minuten]` | Meetup with ping + go/pass buttons | everyone |
| `/ping` | Gateway latency | everyone |
| `/ha-status` | Test the Home Assistant Supervisor API | everyone |

## Hosted Services

| Service | Purpose | Feature flag |
| --- | --- | --- |
| `DiscordBotWorker` | Gateway login/start, log forwarding | always on |
| `InteractionHandlingService` | Module registration, interaction routing and guaranteed replies | always on |
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

Flags resolve from `Wk7Bot:features`, then root-level `features`, then default enabled.

## MQTT Health Sensors

`BotStatusMqttService` publishes a device *WK7 Bot* with a last-will on `wk7bot/status`:

| Entity | States |
| --- | --- |
| `sensor.wk7bot_status` | uptime in seconds; attributes `version`, `started_at_utc`, `uptime_seconds`, `discord_connection`, `guilds`, `features_total`, `features_problem` |
| `binary_sensor.wk7bot_<feature>_enabled` | ON while the feature flag is set |
| `binary_sensor.wk7bot_<feature>_problem` | ON while the feature's last pass failed; attributes `last_success_utc`, `last_failure_utc`, `last_error` |

`<feature>` ∈ `food`, `rss`, `leipzig_waste`, `weekend_digest`, `dwd_warning`, `spontan_treff`. All entities expire to `unavailable` within 2–5 minutes if the heartbeat stops, so a dead bot is visible immediately.

Example dashboard cards (Mushroom + grid layout):

```yaml
- type: custom:mushroom-chips-card
  alignment: end
  chips:
    - type: entity
      entity: sensor.wk7bot_status
      name: WK7 Bot
      icon: mdi:robot
      icon_color: |-
        {% if is_state('sensor.wk7bot_status', 'unavailable') %}red
        {% elif state_attr('sensor.wk7bot_status', 'discord_connection') != 'Connected' %}orange
        {% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7bot_rss_problem
      name: RSS
      icon: mdi:rss
      icon_color: |-
        {% if is_state('binary_sensor.wk7bot_rss_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7bot_leipzig_waste_problem
      name: Müll
      icon: mdi:trash-can
      icon_color: |-
        {% if is_state('binary_sensor.wk7bot_leipzig_waste_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7bot_weekend_digest_problem
      name: Digset
      icon: mdi:calendar-weekend
      icon_color: |-
        {% if is_state('binary_sensor.wk7bot_weekend_digest_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7bot_dwd_warning_problem
      name: DWD
      icon: mdi:weather-lightning
      icon_color: |-
        {% if is_state('binary_sensor.wk7bot_dwd_warning_problem', 'on') %}red{% else %}green{% endif %}
    - type: entity
      entity: binary_sensor.wk7bot_food_problem
      name: Food
      icon: mdi:food
      icon_color: |-
        {% if is_state('binary_sensor.wk7bot_food_problem', 'on') %}red{% else %}green{% endif %}
```

## Configuration

Bound from `appsettings.json`, environment variables or the Add-On `config.yaml`; the `Wk7Bot` section supplies defaults and root-level keys override per key. The full schema with defaults lives in `WK7Bot/config.yaml`.

```yaml
discord_token: "YOUR_DISCORD_BOT_TOKEN"
mqtt_host: "core-mosquitto"
mqtt_port: 1883
steam_api_key: ""
battlenet_client_id: ""
battlenet_client_secret: ""
battlenet_region: "eu"
gemini_api_key: ""
error_notify_user_ids:
  - "162201162257399808"
features:
  rss_polling_enabled: true
  mqtt_bot_status_enabled: true          # uptime + per-feature health sensors
  error_notifications_enabled: true
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

`servers.*_server_id` ship as placeholders; an unparsable or empty ID counts as "not configured", so a fresh install never breaks. Environment variables (`DISCORD_BOT_TOKEN`, `SUPERVISOR_TOKEN`) and .NET user secrets also work.

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

551 tests cover API mapping and retry behaviour against stubbed HTTP handlers, options binding, error-queue throttling, interaction routing and guaranteed replies, SQLite DDL replay for restart-safe dispatch, and all embed/poll/button builders.

## Stack

.NET 10 (C# 14) · `Discord.Net` / `Discord.Interactions` · EF Core + SQLite · `MQTTnet` · `CodeHollow.FeedReader`, `Ical.Net`, JSON-LD recipes · DuckDuckGo/Chefkoch search · Steam Web API and Battle.net OAuth · DWD CAP open data · Google Gemini · xUnit, Moq, EF InMemory · Docker, Home Assistant Supervisor Add-On.

## License

Copyright © 2026 **DevilDracus** — [GNU AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html). Please credit [WK7Bot](https://github.com/DevilDracus/WK7Bot) when building upon this project.

[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
