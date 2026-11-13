# WK7Bot

A modular Discord automation bot and Home Assistant add-on built on .NET 10. Slash commands, RSS syndication, Steam and Battle.net enrichment, deal alerts, weather and waste reminders, polls, reminders and stats — all behind one feature-gate and a background-service framework.

[![Run Unit Tests](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml/badge.svg)](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml)
[![Add repository to Home Assistant][repository-badge]][repository-url]

## Highlights

- **Discord** — `Discord.Interactions` with auto-registered modules. Every interaction is answered: failed preconditions, command errors and unparseable input all return a reply instead of a Discord timeout.
- **RSS syndication** — per-feed intervals, backlog-free first poll, duplicate suppression, OPML import/export, pause/resume, and an auto-provisioned role plus private channel with subscription menus.
- **Steam & Battle.net** — account linking, presence snapshots published to Home Assistant over MQTT, wishlist deal alerts through IsThereAnyDeal, achievement progress, discount history and cross-store price comparison.
- **Home Assistant** — native add-on packaging, Supervisor option mirroring, MQTT health sensors with last-will, and Prometheus metrics on `/metrics`.
- **Daily life** — DWD weather and pollen/UV warnings, Leipzig waste collection reminders, seasonal produce calendar and recipe generation.
- **Community** — meetups with go/pass buttons and native Discord events, a weekend digest with polls, trivia, birthdays, personal reminders and leaderboards.
- **Operability** — startup configuration validation, `/health` dependency checks, per-feature health sensors, and a throttled error channel that every `Error`/`Critical` log reaches.

## Quick Start

Requires the **.NET 10 SDK**.

```bash
git clone https://github.com/DevilDracus/WK7Bot.git
cd WK7Bot
dotnet run --project WK7Bot
```

**As a Home Assistant add-on:** add this repository to the add-on store, install **WK7Bot**, set `discord_token`, configure MQTT, and start.

**As a container:**

```bash
docker build -f WK7Bot/Dockerfile -t wk7bot WK7Bot
```

Health check: `GET /health` · Metrics: `GET /metrics`

On Windows, override `ConnectionStrings:DefaultConnection` to a writable path (for example `Data Source=wk7bot.db`) unless `C:\data` can be created.

## Configuration

Options bind from `appsettings.json`, environment variables or the add-on `config.yaml`. The `Wk7Bot` section supplies defaults; root-level keys override per key. The complete annotated schema lives in `WK7Bot/config.yaml`.

```yaml
discord_token: "YOUR_DISCORD_BOT_TOKEN"
log_level: "Warning"
mqtt_host: "core-mosquitto"
mqtt_port: 1883
steam_api_key: ""
battlenet_client_id: ""
battlenet_client_secret: ""
gemini_api_key: ""
itad_api_key: ""
metrics_enabled: true
servers:
  wk7_server_id: "YOUR_WK7_SERVER_ID"        # scheduled message target
  test_server_id: "YOUR_BOT_TEST_SERVER_ID"  # testing features and all error reports
  testing_features: []
dwd_warning:
  postal_code: "01234"
  latitude: 00.00
  longitude: 00.00
features:
  rss_polling_enabled: true
  dwd_warning_enabled: true
  wishlist_deal_alerts_enabled: true
  # remaining flags default to enabled
```

Server IDs ship as placeholders and count as "not configured" while empty or unparsable, so a fresh install never breaks on them. Flags resolve from `Wk7Bot:features`, then root-level `features`, then default enabled, and can be switched at runtime with `/config feature <key> <on|off>`. Environment variables (`DISCORD_BOT_TOKEN`, `SUPERVISOR_TOKEN`, `WK7_TEST_API_KEY`) and .NET user secrets also work.

## Commands

### RSS

| Command | Description |
| --- | --- |
| `/rss add`, `/rss remove` | Register a feed with its role and private channel, or remove both |
| `/rss list`, `/rss dashboard` | List feeds or post the subscription menu |
| `/rss pause`, `/rss resume` | Stop or restart polling while keeping channel, role and baseline |
| `/rss test` | Preview a feed without registering it |
| `/rss export`, `/rss import` | OPML export or import |

### Steam & Battle.net

| Command | Description |
| --- | --- |
| `/link-steam`, `/link-battlenet`, `/link-status`, `/link-remove` | Manage account links |
| `/deals` | On-demand wishlist deal lookup |
| `/watch`, `/unwatch`, `/watchlist` | Personal price watchlist with DM alerts |
| `/achievements` | Steam achievement progress |
| `/price-history`, `/compare-prices` | Discount history and cross-store comparison |
| `/release-remind`, `/release-unremind`, `/release-list` | Release-date reminders |
| `/wishlist-add`, `/wishlist-add-appid`, `/wishlist-list-appid`, `/wishlist-remove-appid` | Wishlist management |
| `/leaderboard`, `/profile` | Steam and Battle.net statistics |

### Daily Life

| Command | Description |
| --- | --- |
| `/weather` | Current conditions from a Home Assistant weather entity |
| `/pollen` | DWD pollen hazard and UV index |
| `/check-waste` | Leipzig waste collection dates |
| `/recipe`, `/recipe-seasonal`, `/recipe-generate`, `/seasonal-produce`, `/check-food` | Recipes, produce calendar and nutrition checks |

### Community

| Command | Description |
| --- | --- |
| `/spontan-treff` | Meetup with ping and go/pass buttons |
| `/poll` | Native Discord poll |
| `/quiz`, `/trivia-leaderboard` | Trivia rounds and weekly ranking |
| `/birthday-set`, `/birthday-show`, `/birthday-remove` | Birthday tracking |
| `/remind`, `/reminders`, `/remind-cancel` | Personal reminders |

### Operations

| Command | Description | Permission |
| --- | --- | --- |
| `/config feature`, `/config list` | Runtime feature switches | `Administrator` |
| `/health`, `/ha-status`, `/ping` | Dependency, Supervisor and latency checks | everyone |
| `/stats` | Top commands of the last 7 days | everyone |
| `/help` | Command index | everyone |

## Architecture

- **Hosted services** — one background service per feature area, each gated by a feature flag and reporting health through `FeatureHealthTracker`.
- **Delivery** — Components V2 cards via `CardBuilder` and `CardSender`, with automatic fallback to embeds wherever a card would drop content, mentions or native polls.
- **Persistence** — EF Core on SQLite with restart-safe dispatch deduplication and daily compressed backups.
- **Resilience** — Polly policies for external APIs, a Gemini circuit breaker, and a throttled error notification pipeline.

### MQTT health sensors

`BotStatusMqttService` publishes a *WK7 Bot* device with a last will on `wk7bot/status`:

| Entity | States |
| --- | --- |
| `sensor.wk7_bot_status` | uptime in seconds; attributes `version`, `discord_connection`, `guilds`, `features_total`, `features_problem` |
| `binary_sensor.wk7_bot_<feature>_enabled` | ON while the feature flag is set |
| `binary_sensor.wk7_bot_<feature>_problem` | ON while the last pass failed |

Entities expire to `unavailable` within minutes if the heartbeat stops, so a dead bot is visible immediately.

## Testing

```bash
dotnet test WK7Bot.sln
```

**1130 tests** cover API mapping and retry behaviour against stubbed HTTP handlers, options binding, error-queue throttling, interaction routing and guaranteed replies, SQLite schema replay, circuit breaker logic, feature gate resolution, command cooldowns, startup validation, DWD pollen and UV parsing, OPML round-trips, Components V2 card structure, and the embed, poll and button builders.

## Stack

.NET 10 (C# 14) · `Discord.Net` 3.19 (Interactions, Components V2) · EF Core + SQLite · `MQTTnet` · Polly 8 · `CodeHollow.FeedReader` · `Ical.Net` · Steam Web API · Battle.net OAuth · IsThereAnyDeal · Open Food Facts · DWD open data · Google Gemini · xUnit, Moq, EF InMemory · Docker · Home Assistant Supervisor add-on.

## Troubleshooting

- **DWD warnings never post** — check that `dwd_warning.postal_code` is set, that `servers.wk7_server_id` is a real ID, and that `dwd_warning_enabled` is on. DWD warnings are episodic; silence is normal when nothing is active.
- **Wishlist deal alerts never post** — requires a non-empty `itad_api_key`, at least one Discord to Steam link, real server IDs, and a **public** Steam wishlist ("Game details" must be Public).
- **Error channel is empty but something feels broken** — an empty channel only means no `Error` or `Critical` log was emitted. Idle services with missing keys post nothing and log nothing; check the startup validation report and the per-feature `_problem` sensors.
- **`no such column` SQLite crash loop** — a schema-ordering bug: an index or constraint was added before its column. Move the DDL after `AddColumnIfMissingAsync`.

## License

Copyright © 2026 **DevilDracus** — [GNU AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html). Please credit [WK7Bot](https://github.com/DevilDracus/WK7Bot) when building upon this project.

[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
